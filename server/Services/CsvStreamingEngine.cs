using System.Diagnostics;
using System.Text;
using UsuariosRetirados.Server.DTOs;

namespace UsuariosRetirados.Server.Services;

public interface ICsvStreamingEngine
{
    Encoding GetWindows1252Encoding();
    string[]? ReadHeader(string filePath);
    (int TotalRows, int MatchedCount, List<EmpleadoRowDto> SampleRows, long ElapsedMs) FilterRows(
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

    public string[]? ReadHeader(string filePath)
    {
        if (!File.Exists(filePath)) return null;

        using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 65536);
        using var reader = new StreamReader(fs, GetWindows1252Encoding());

        var line = reader.ReadLine();
        if (line == null) return null;

        return ParseCsvLine(line).ToArray();
    }

    public (int TotalRows, int MatchedCount, List<EmpleadoRowDto> SampleRows, long ElapsedMs) FilterRows(
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
            return (0, 0, matchedRows, 0);
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

        using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 131072);
        using var reader = new StreamReader(fs, GetWindows1252Encoding());

        var headerLine = reader.ReadLine();
        if (headerLine == null) return (0, 0, matchedRows, sw.ElapsedMilliseconds);

        var headerFields = ParseCsvLine(headerLine);
        int colEstado = -1, colDoc = -1, colSoc = -1, colFecha = -1;

        for (int i = 0; i < headerFields.Count; i++)
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
        int matchedCount = 0;

        string? line;
        while ((line = reader.ReadLine()) != null)
        {
            totalRows++;
            if (string.IsNullOrWhiteSpace(line)) continue;

            var fields = ParseCsvLine(line);
            if (fields.Count <= maxColIndex) continue;

            string estado = fields[colEstado].Trim();
            string soc = fields[colSoc].Trim();
            string fecha = fields[colFecha].Trim();

            if (!estadoSet.Contains(estado)) continue;
            if (socSet != null && !socSet.Contains(soc)) continue;
            if (fechaFiltro != null && !string.Equals(fecha, fechaFiltro, StringComparison.OrdinalIgnoreCase)) continue;

            matchedCount++;
            string doc = fields[colDoc].Trim();

            if (maxCollect <= 0 || matchedRows.Count < maxCollect)
            {
                matchedRows.Add(new EmpleadoRowDto(estado, doc, soc, fecha));
            }
        }

        sw.Stop();
        return (totalRows, matchedCount, matchedRows, sw.ElapsedMilliseconds);
    }

    /// <summary>
    /// Fast CSV line parser that respects quoted commas and quotes escaping.
    /// Gracefully recovers from unbalanced quotes.
    /// </summary>
    public static List<string> ParseCsvLine(string line)
    {
        var result = new List<string>(80);
        if (string.IsNullOrEmpty(line)) return result;

        var sb = new StringBuilder(64);
        bool inQuotes = false;
        int length = line.Length;

        for (int i = 0; i < length; i++)
        {
            char c = line[i];

            if (c == '"')
            {
                if (inQuotes && i + 1 < length && line[i + 1] == '"')
                {
                    sb.Append('"');
                    i++; // skip escaped quote
                }
                else
                {
                    inQuotes = !inQuotes;
                }
            }
            else if (c == ',' && !inQuotes)
            {
                result.Add(sb.ToString());
                sb.Clear();
            }
            else
            {
                sb.Append(c);
            }
        }

        result.Add(sb.ToString());
        return result;
    }
}
