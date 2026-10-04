using System.Diagnostics;
using System.Text;
using Microsoft.VisualBasic.FileIO;
using UsuariosRetirados.Server.DTOs;

namespace UsuariosRetirados.Server.Services;

public record FilterResult(
    int TotalRows,
    int MalformedCount,
    int MatchedCount,
    List<EmpleadoRowDto> Rows,
    long ElapsedMs
);

public interface ICsvStreamingEngine
{
    Encoding GetWindows1252Encoding();
    string[]? ReadHeader(string filePath);
    FilterResult FilterRows(
        string filePath,
        string? fechaEvento,
        IEnumerable<string>? sociedades,
        IEnumerable<string>? estados,
        int maxCollect = 0);
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

    public FilterResult FilterRows(
        string filePath,
        string? fechaEvento,
        IEnumerable<string>? sociedades,
        IEnumerable<string>? estados,
        int maxCollect = 0)
    {
        var sw = Stopwatch.StartNew();
        var matchedRows = new List<EmpleadoRowDto>();

        if (!File.Exists(filePath))
        {
            return new FilterResult(0, 0, 0, matchedRows, 0);
        }

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

        using var parser = NewParser(filePath, GetWindows1252Encoding());

        var headerFields = parser.ReadFields();
        if (headerFields == null)
        {
            sw.Stop();
            return new FilterResult(0, 0, 0, matchedRows, sw.ElapsedMilliseconds);
        }

        int colEstado = -1, colDoc = -1, colSoc = -1, colFecha = -1;

        for (int i = 0; i < headerFields.Length; i++)
        {
            var name = headerFields[i].Trim();
            if (colEstado == -1 && string.Equals(name, "ESTADO", StringComparison.OrdinalIgnoreCase)) colEstado = i;
            else if (colDoc == -1 && string.Equals(name, "DOCUMENTO", StringComparison.OrdinalIgnoreCase)) colDoc = i;
            else if (colSoc == -1 && string.Equals(name, "NOMBRE SOCIEDAD", StringComparison.OrdinalIgnoreCase)) colSoc = i;
            else if (colFecha == -1 && string.Equals(name, "FECHA EVENTO", StringComparison.OrdinalIgnoreCase)) colFecha = i;
        }

        if (colEstado == -1 || colDoc == -1 || colSoc == -1 || colFecha == -1)
        {
            throw new InvalidOperationException("El encabezado del archivo no contiene las columnas requeridas (ESTADO, DOCUMENTO, NOMBRE SOCIEDAD, FECHA EVENTO).");
        }

        int maxColIndex = Math.Max(Math.Max(colEstado, colDoc), Math.Max(colSoc, colFecha));
        int totalRows = 0;
        int malformed = 0;
        int matchedCount = 0;

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

            string estado = fields[colEstado].Trim();
            string soc = fields[colSoc].Trim();
            string fecha = fields[colFecha].Trim();

            if (!estadoSet.Contains(estado)) continue;
            if (socSet != null && !socSet.Contains(soc)) continue;
            if (fechaFiltro != null && !string.Equals(fecha, fechaFiltro, StringComparison.OrdinalIgnoreCase)) continue;

            matchedCount++;

            if (maxCollect <= 0 || matchedRows.Count < maxCollect)
            {
                matchedRows.Add(new EmpleadoRowDto(estado, fields[colDoc].Trim(), soc, fecha));
            }
        }

        sw.Stop();
        return new FilterResult(totalRows, malformed, matchedCount, matchedRows, sw.ElapsedMilliseconds);
    }
}
