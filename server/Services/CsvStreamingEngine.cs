using System.Diagnostics;
using System.Text;
using Microsoft.VisualBasic.FileIO;
using PWExtendedApp.Server.DTOs;

namespace PWExtendedApp.Server.Services;

public record FilterResult(
    int TotalRows,
    int MalformedCount,
    int MatchedCount,
    List<EmpleadoRowDto> Rows,
    long ElapsedMs
);

/// <summary>
/// Padrón leído una sola vez: solo las columnas que usa la app, en el orden del archivo.
/// Lo mantiene en memoria <see cref="PadronCache"/> y se vuelve a leer si el archivo cambia.
/// </summary>
public record PadronSnapshot(
    string Path,
    long Length,
    DateTime LastWriteUtc,
    int TotalRows,
    int MalformedCount,
    IReadOnlyList<EmpleadoRowDto> Rows,
    long LoadMs
);

public interface ICsvStreamingEngine
{
    Encoding GetWindows1252Encoding();
    string[]? ReadHeader(string filePath);
    PadronSnapshot Load(string filePath);
    FilterResult Filter(PadronSnapshot padron, string? fechaEvento, IEnumerable<string>? sociedades,
        IEnumerable<string>? estados);
}

public class CsvStreamingEngine : ICsvStreamingEngine
{
    static CsvStreamingEngine()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    public Encoding GetWindows1252Encoding()
    {
        return Encoding.GetEncoding("Windows-1252");
    }

    /// <summary>
    /// Creates a TextFieldParser matching the historical motor configuration:
    /// delimited by comma, quoted fields honored, no whitespace trimming.
    /// </summary>
    private static TextFieldParser NewParser(string filePath, Encoding encoding)
    {
        var parser = new TextFieldParser(filePath, encoding)
        {
            TextFieldType = FieldType.Delimited,
            HasFieldsEnclosedInQuotes = true,
            TrimWhiteSpace = false
        };
        parser.SetDelimiters(",");
        return parser;
    }

    public string[]? ReadHeader(string filePath)
    {
        if (!File.Exists(filePath)) return null;

        using var parser = NewParser(filePath, GetWindows1252Encoding());
        if (parser.EndOfData) return null;

        var fields = parser.ReadFields();
        return fields?.Select(f => f.Trim()).ToArray();
    }

    /// <summary>
    /// Lee el padrón completo con TextFieldParser (una pasada) y guarda, de cada fila con las
    /// columnas requeridas, los mismos valores (Trim) que antes leía cada filtro por separado.
    /// </summary>
    public PadronSnapshot Load(string filePath)
    {
        var sw = Stopwatch.StartNew();
        var fi = new FileInfo(filePath);
        var rows = new List<EmpleadoRowDto>();
        if (!fi.Exists)
        {
            return new PadronSnapshot(filePath, 0, DateTime.MinValue, 0, 0, rows, 0);
        }

        using var parser = NewParser(filePath, GetWindows1252Encoding());

        var headerFields = parser.ReadFields();
        if (headerFields == null)
        {
            return new PadronSnapshot(filePath, fi.Length, fi.LastWriteTimeUtc, 0, 0, rows, sw.ElapsedMilliseconds);
        }

        int colEstado = -1, colDoc = -1, colSoc = -1, colFecha = -1;
        // Opcionales: si el padrón no las trae, la fila sale con nombre vacío.
        int colNombre = -1, colApellido = -1;

        for (int i = 0; i < headerFields.Length; i++)
        {
            var name = headerFields[i].Trim();
            if (colEstado == -1 && string.Equals(name, "ESTADO", StringComparison.OrdinalIgnoreCase)) colEstado = i;
            else if (colDoc == -1 && string.Equals(name, "DOCUMENTO", StringComparison.OrdinalIgnoreCase)) colDoc = i;
            else if (colSoc == -1 && string.Equals(name, "NOMBRE SOCIEDAD", StringComparison.OrdinalIgnoreCase)) colSoc = i;
            else if (colFecha == -1 && string.Equals(name, "FECHA EVENTO", StringComparison.OrdinalIgnoreCase)) colFecha = i;
            else if (colNombre == -1 && string.Equals(name, "NOMBRE EMPLEADO", StringComparison.OrdinalIgnoreCase)) colNombre = i;
            else if (colApellido == -1 && string.Equals(name, "APELLIDO EMPLEADO", StringComparison.OrdinalIgnoreCase)) colApellido = i;
        }

        if (colEstado == -1 || colDoc == -1 || colSoc == -1 || colFecha == -1)
        {
            throw new InvalidOperationException("El encabezado del archivo no contiene las columnas requeridas (ESTADO, DOCUMENTO, NOMBRE SOCIEDAD, FECHA EVENTO).");
        }

        int maxColIndex = Math.Max(Math.Max(colEstado, colDoc), Math.Max(colSoc, colFecha));
        int totalRows = 0;
        int malformed = 0;

        // Estado, sociedad y fecha se repiten mucho: se comparte una sola instancia de cada valor.
        var pool = new Dictionary<string, string>(StringComparer.Ordinal);
        string Shared(string v)
        {
            if (pool.TryGetValue(v, out var existing)) return existing;
            pool[v] = v;
            return v;
        }

        while (!parser.EndOfData)
        {
            string[]? fields;
            try
            {
                fields = parser.ReadFields();
            }
            catch (MalformedLineException)
            {
                malformed++;
                continue;
            }

            if (fields == null) continue;
            totalRows++;
            if (fields.Length <= maxColIndex) continue;

            rows.Add(new EmpleadoRowDto(
                Shared(fields[colEstado].Trim()),
                fields[colDoc].Trim(),
                Shared(fields[colSoc].Trim()),
                Shared(fields[colFecha].Trim()),
                OptionalField(fields, colNombre),
                OptionalField(fields, colApellido)));
        }

        rows.TrimExcess();
        sw.Stop();
        return new PadronSnapshot(filePath, fi.Length, fi.LastWriteTimeUtc, totalRows, malformed, rows, sw.ElapsedMilliseconds);
    }

    /// <summary>
    /// Aplica los filtros sobre el padrón en memoria. Misma semántica que la lectura en
    /// streaming original (mismo orden de filas y mismas comparaciones).
    /// </summary>
    public FilterResult Filter(PadronSnapshot padron, string? fechaEvento, IEnumerable<string>? sociedades,
        IEnumerable<string>? estados)
    {
        var sw = Stopwatch.StartNew();

        // Normalize states (expand 'Con terminación de contrato' to both with and without accent)
        var estadoSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (estados != null)
        {
            foreach (var est in estados)
            {
                var trimmed = est.Trim();
                if (string.Equals(trimmed, "Con terminación de contrato", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(trimmed, "Con terminacion de contrato", StringComparison.OrdinalIgnoreCase))
                {
                    estadoSet.Add("Con terminación de contrato");
                    estadoSet.Add("Con terminacion de contrato");
                }
                else
                {
                    estadoSet.Add(trimmed);
                }
            }
        }
        if (estadoSet.Count == 0)
        {
            estadoSet.Add("Terminated");
        }

        // Normalize societies (if empty or contains 'TODAS', allow all)
        HashSet<string>? socSet = null;
        if (sociedades != null)
        {
            var list = sociedades.Select(s => s.Trim()).Where(s => !string.IsNullOrEmpty(s)).ToList();
            if (list.Count > 0 && !list.Any(s => string.Equals(s, "TODAS", StringComparison.OrdinalIgnoreCase)))
            {
                socSet = new HashSet<string>(list, StringComparer.OrdinalIgnoreCase);
            }
        }

        string? fechaFiltro = string.IsNullOrWhiteSpace(fechaEvento) ? null : fechaEvento.Trim();

        var matched = new List<EmpleadoRowDto>();
        foreach (var row in padron.Rows)
        {
            if (!estadoSet.Contains(row.Estado)) continue;
            if (socSet != null && !socSet.Contains(row.Sociedad)) continue;
            if (fechaFiltro != null && !string.Equals(row.FechaEvento, fechaFiltro, StringComparison.OrdinalIgnoreCase)) continue;
            matched.Add(row);
        }

        sw.Stop();
        return new FilterResult(padron.TotalRows, padron.MalformedCount, matched.Count, matched, sw.ElapsedMilliseconds);
    }

    private static string OptionalField(string[] fields, int index) =>
        index >= 0 && index < fields.Length ? fields[index].Trim() : string.Empty;
}
