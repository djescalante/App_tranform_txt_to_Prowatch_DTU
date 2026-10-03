using System.Text.Json;

namespace UsuariosRetirados.Server.Services;

public interface ISchemaValidator
{
    SchemaValidationResult ValidateHeader(string[] header, string schemaJsonPath);
}

public record SchemaValidationResult(
    bool IsValid,
    bool MissingRequired,
    List<string> MissingRequiredColumns,
    List<string> MissingColumns,
    List<string> ExtraColumns,
    int ExpectedTotal,
    int ActualTotal
);

public class SchemaValidator : ISchemaValidator
{
    private static readonly string[] RequiredColumns = ["ESTADO", "DOCUMENTO", "NOMBRE SOCIEDAD", "FECHA EVENTO"];

    public SchemaValidationResult ValidateHeader(string[] header, string schemaJsonPath)
    {
        var actualSet = new HashSet<string>(header.Select(h => h.Trim()), StringComparer.OrdinalIgnoreCase);

        var missingRequired = RequiredColumns.Where(r => !actualSet.Contains(r)).ToList();

        List<string> expectedColumns = [];
        if (File.Exists(schemaJsonPath))
        {
            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(schemaJsonPath));
                if (doc.RootElement.TryGetProperty("Columnas", out var colElement))
                {
                    expectedColumns = colElement.EnumerateArray()
                        .Select(e => e.GetString() ?? string.Empty)
                        .Where(s => !string.IsNullOrWhiteSpace(s))
                        .ToList();
                }
            }
            catch
            {
                // Fallback to required
            }
        }

        var expectedSet = new HashSet<string>(expectedColumns.Select(c => c.Trim()), StringComparer.OrdinalIgnoreCase);

        var missing = expectedSet.Where(e => !actualSet.Contains(e)).ToList();
        var extra = actualSet.Where(a => expectedSet.Count > 0 && !expectedSet.Contains(a)).ToList();

        bool isValid = missingRequired.Count == 0;

        return new SchemaValidationResult(
            IsValid: isValid,
            MissingRequired: missingRequired.Count > 0,
            MissingRequiredColumns: missingRequired,
            MissingColumns: missing,
            ExtraColumns: extra,
            ExpectedTotal: expectedColumns.Count,
            ActualTotal: header.Length
        );
    }
}
