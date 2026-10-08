using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using ClosedXML.Excel;

namespace UsuariosRetirados.Server.Services.Ocupacion;

public record OcupacionRegistro(
    string? Fecha, string Nombres, string Apellidos, string Panel, string SedeAdministrativa,
    string Cedula, string TarjetaAcceso, string Ciudad, string Empresa,
    string FirstSwipe, string LastSwipe, string Fingerprint);

public record OcupacionCargaResultado(
    string Nombre, string Estado, long? ArchivoId = null, int FilasLeidas = 0,
    int FilasInsertadas = 0, int FilasDuplicadas = 0, int FilasError = 0, string? Mensaje = null);

/// <summary>
/// Lectura de los Excel "Ocupación Edificios" y carga en prowatch.db.
///
/// Es un port 1:1 de app/ingest.py (Python + openpyxl). Las conversiones de celda
/// imitan a openpyxl a propósito: el fingerprint (SHA-1) que deduplica filas depende
/// del texto exacto, y debe coincidir con el de los 1,85 M de registros ya cargados.
/// </summary>
public static partial class OcupacionIngest
{
    private static readonly Dictionary<string, string> HeaderMap = new()
    {
        ["nombres"] = "nombres",
        ["apellidos"] = "apellidos",
        ["panel"] = "panel",
        ["sede administrativa"] = "sede_administrativa",
        ["cedula"] = "cedula",
        ["tarjeta de acceso"] = "tarjeta_acceso",
        ["ciudad"] = "ciudad",
        ["empresa"] = "empresa",
        ["first swipe of day"] = "first_swipe",
        ["last swipe of day"] = "last_swipe",
    };

    private static readonly DateTime ExcelEpoch = new(1899, 12, 30);

    [GeneratedRegex(@"(\d{4})-(\d{2})-(\d{2})")]
    private static partial Regex DateRe();

    [GeneratedRegex(@"(\d{1,2})-(\d{1,2})-(\d{2,4})")]
    private static partial Regex FileNameDateRe();

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRe();

    // ---------- Conversión de celdas (equivalente a openpyxl read_only + data_only) ----------

    /// <summary>
    /// Devuelve el valor como lo entregaría openpyxl: null, string, long, double, bool,
    /// PyDateTime (fecha/hora) o PyTime (solo hora).
    /// </summary>
    private static object? CellValue(IXLCell cell)
    {
        var v = cell.Value;
        switch (v.Type)
        {
            case XLDataType.Blank:
                return null;
            case XLDataType.Text:
                return v.GetText();
            case XLDataType.Boolean:
                return v.GetBoolean();
            case XLDataType.Error:
                return v.ToString();
            case XLDataType.Number:
                // openpyxl convierte a int los números sin punto decimal ni exponente.
                var n = v.GetNumber();
                return Math.Abs(n) < 9e15 && n == Math.Floor(n) ? (long)n : n;
            case XLDataType.DateTime:
            case XLDataType.TimeSpan:
                return FromExcelSerial(v.GetUnifiedNumber());
            default:
                return v.ToString();
        }
    }

    private readonly record struct PyDateTime(DateTime Value, int Micro);
    private readonly record struct PyTime(TimeSpan Value, int Micro);

    /// <summary>openpyxl.utils.datetime.from_excel: redondea la fracción a milisegundos.</summary>
    private static object FromExcelSerial(double value)
    {
        var day = Math.Floor(value);
        var fraction = value - day;
        var ms = (long)Math.Round(fraction * 86400 * 1000, MidpointRounding.ToEven);
        var diff = TimeSpan.FromMilliseconds(ms);
        if (value >= 0 && value < 1)
        {
            return new PyTime(diff, (int)(ms % 1000) * 1000);
        }
        if (value > 0 && value < 60) day += 1; // bisiesto ficticio de 1900 en Excel
        var dt = ExcelEpoch.AddDays(day).Add(diff);
        return new PyDateTime(dt, (int)(ms % 1000) * 1000);
    }

    /// <summary>str(value) de Python para los tipos que entrega openpyxl.</summary>
    private static string PyStr(object value) => value switch
    {
        string s => s,
        bool b => b ? "True" : "False",
        long l => l.ToString(CultureInfo.InvariantCulture),
        double d => PyFloatRepr(d),
        PyDateTime dt => dt.Value.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)
                         + (dt.Micro > 0 ? "." + dt.Micro.ToString("D6", CultureInfo.InvariantCulture) : ""),
        PyTime t => t.Value.ToString(@"hh\:mm\:ss", CultureInfo.InvariantCulture)
                    + (t.Micro > 0 ? "." + t.Micro.ToString("D6", CultureInfo.InvariantCulture) : ""),
        _ => value.ToString() ?? string.Empty
    };

    private static string PyFloatRepr(double d)
    {
        if (double.IsNaN(d)) return "nan";
        if (double.IsInfinity(d)) return d > 0 ? "inf" : "-inf";
        if (d == Math.Floor(d) && Math.Abs(d) < 1e16) return d.ToString("0.0", CultureInfo.InvariantCulture);
        return d.ToString("R", CultureInfo.InvariantCulture).Replace("E", "e");
    }

    // ---------- Limpieza (clean_text, clean_cedula, clean_datetime) ----------

    private static string CleanText(object? value)
    {
        if (value is null) return string.Empty;
        var text = PyStr(value).Replace('\t', ' ').Replace('\n', ' ');
        return WhitespaceRe().Replace(text, " ").Trim();
    }

    private static string CleanCedula(object? value)
    {
        switch (value)
        {
            case null:
                return string.Empty;
            case bool or long:
                return PyStr(value);
            case double d:
                return d == Math.Floor(d) && !double.IsInfinity(d)
                    ? ((decimal)d).ToString("0", CultureInfo.InvariantCulture)
                    : PyFloatRepr(d);
        }
        var text = PyStr(value).Trim();
        if (text.EndsWith(".0") && text.Length > 2 && text[..^2].All(char.IsDigit))
        {
            text = text[..^2];
        }
        return text;
    }

    private static string CleanDateTime(object? value)
    {
        if (value is null) return string.Empty;
        if (value is PyDateTime dt)
        {
            return dt.Value.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
        }
        return PyStr(value).Trim();
    }

    private static string? ExtractDate(params string?[] candidates)
    {
        foreach (var value in candidates)
        {
            if (string.IsNullOrEmpty(value)) continue;
            var m = DateRe().Match(value);
            if (m.Success) return m.Value;
        }
        return null;
    }

    private static string? DateFromFileName(string fileName)
    {
        foreach (Match m in FileNameDateRe().Matches(Path.GetFileName(fileName)))
        {
            int day = int.Parse(m.Groups[1].Value), month = int.Parse(m.Groups[2].Value);
            int year = int.Parse(m.Groups[3].Value);
            if (year < 100) year += 2000;
            else if (year is >= 200 and < 1000) year += 1900;
            if (year is < 1 or > 9999 || month is < 1 or > 12 || day < 1 || day > DateTime.DaysInMonth(year, month))
            {
                continue;
            }
            return new DateTime(year, month, day).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        }
        return null;
    }

    private static string Fingerprint(params string?[] parts)
    {
        var raw = string.Join("|", parts.Select(p => p ?? string.Empty));
        return Convert.ToHexStringLower(SHA1.HashData(Encoding.UTF8.GetBytes(raw)));
    }

    // ---------- Lectura del libro ----------

    /// <summary>Parsea el Excel. Lanza InvalidDataException si no tiene la estructura esperada.</summary>
    public static (List<OcupacionRegistro> Registros, int Errores) Parse(Stream xlsx, string fileName)
    {
        using var wb = new XLWorkbook(xlsx);
        var ws = wb.TryGetWorksheet("Sheet1", out var sheet1) ? sheet1 : wb.Worksheet(1);

        var firstRow = ws.FirstRowUsed()?.RowNumber() ?? 1;
        var lastRow = ws.LastRowUsed()?.RowNumber() ?? 0;
        var lastCol = ws.LastColumnUsed()?.ColumnNumber() ?? 0;

        // La fila de encabezado no siempre es la 1: se busca la que tenga Nombres + Cedula.
        int headerRow = -1;
        var positions = new Dictionary<string, int>();
        for (int r = firstRow; r <= Math.Min(lastRow, firstRow + 29); r++)
        {
            var cells = Enumerable.Range(1, lastCol)
                .Select(c => CleanText(CellValue(ws.Cell(r, c))).ToLowerInvariant()).ToList();
            if (!cells.Contains("cedula") || !cells.Contains("nombres")) continue;

            headerRow = r;
            for (int c = 0; c < cells.Count; c++)
            {
                if (HeaderMap.TryGetValue(cells[c], out var name)) positions.TryAdd(name, c + 1);
            }
            break;
        }

        if (headerRow < 0)
        {
            throw new InvalidDataException("No se encontro la fila de encabezado (Nombres/Cedula)");
        }
        var missing = new[] { "nombres", "apellidos", "cedula" }.Where(c => !positions.ContainsKey(c)).ToList();
        if (missing.Count > 0)
        {
            throw new InvalidDataException($"Faltan columnas obligatorias: {string.Join(", ", missing)}");
        }

        var fallbackDate = DateFromFileName(fileName);
        var registros = new List<OcupacionRegistro>();
        int errores = 0;

        for (int r = headerRow + 1; r <= lastRow; r++)
        {
            var row = ws.Row(r);
            object? Get(string name) => positions.TryGetValue(name, out var col) ? CellValue(row.Cell(col)) : null;

            bool empty = true;
            for (int c = 1; c <= lastCol && empty; c++)
            {
                if (CleanText(CellValue(row.Cell(c))) != string.Empty) empty = false;
            }
            if (empty) continue;

            var cedula = CleanCedula(Get("cedula"));
            if (cedula.Length == 0)
            {
                errores++;
                continue;
            }

            var first = CleanDateTime(Get("first_swipe"));
            var last = CleanDateTime(Get("last_swipe"));
            var fecha = ExtractDate(first, last, fallbackDate);
            string nombres = CleanText(Get("nombres")), apellidos = CleanText(Get("apellidos"));
            string panel = CleanText(Get("panel")), sede = CleanText(Get("sede_administrativa"));
            string tarjeta = CleanCedula(Get("tarjeta_acceso"));
            string ciudad = CleanText(Get("ciudad")), empresa = CleanText(Get("empresa"));

            var fp = Fingerprint(cedula, fecha, panel, sede, empresa, ciudad, tarjeta, first, last, nombres, apellidos);
            registros.Add(new OcupacionRegistro(fecha, nombres, apellidos, panel, sede, cedula, tarjeta,
                ciudad, empresa, first, last, fp));
        }

        return (registros, errores);
    }
}
