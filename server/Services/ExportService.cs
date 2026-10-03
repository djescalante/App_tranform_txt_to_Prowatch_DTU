using System.Text;
using ClosedXML.Excel;
using UsuariosRetirados.Server.DTOs;

namespace UsuariosRetirados.Server.Services;

public interface IExportService
{
    ExportResult GenerateOutputs(
        IEnumerable<EmpleadoRowDto> rows,
        string outputDir,
        string fechaTag,
        bool emitXlsx,
        bool emitTsv,
        bool emitDtu);
}

public record ExportResult(
    List<string> GeneratedFilePaths,
    string? DtuFileName,
    string? XlsxFileName,
    string? TsvFileName
);

public class ExportService : IExportService
{
    private static readonly UTF8Encoding Utf8WithBom = new(true);

    public ExportResult GenerateOutputs(
        IEnumerable<EmpleadoRowDto> rows,
        string outputDir,
        string fechaTag,
        bool emitXlsx,
        bool emitTsv,
        bool emitDtu)
    {
        if (!Directory.Exists(outputDir))
        {
            Directory.CreateDirectory(outputDir);
        }

        var rowList = rows.ToList();
        var generatedFiles = new List<string>();

        string safeTag = string.IsNullOrWhiteSpace(fechaTag) ? "Todas" : fechaTag.Replace('/', '-');

        string dtuName = $"Usuarios Retirados DTU al {safeTag}.txt";
        string xlsxName = $"Usuarios Retirados {safeTag}.xlsx";
        string tsvName = $"Usuarios Retirados {safeTag}.tsv";

        string dtuPath = Path.Combine(outputDir, dtuName);
        string xlsxPath = Path.Combine(outputDir, xlsxName);
        string tsvPath = Path.Combine(outputDir, tsvName);

        // 1. TXT DTU (DOCUMENTO<TAB>FECHA<TAB>T, sin encabezado)
        if (emitDtu)
        {
            using (var writer = new StreamWriter(dtuPath, false, Utf8WithBom))
            {
                foreach (var row in rowList)
                {
                    writer.WriteLine($"{row.Documento}\t{row.FechaEvento}\tT");
                }
            }
            generatedFiles.Add(dtuPath);
        }

        // 2. TSV (con encabezado, tabulado)
        if (emitTsv)
        {
            using (var writer = new StreamWriter(tsvPath, false, Utf8WithBom))
            {
                writer.WriteLine("ESTADO\tDOCUMENTO\tNOMBRE SOCIEDAD\tFECHA EVENTO");
                foreach (var row in rowList)
                {
                    writer.WriteLine($"{row.Estado}\t{row.Documento}\t{row.Sociedad}\t{row.FechaEvento}");
                }
            }
            generatedFiles.Add(tsvPath);
        }

        // 3. XLSX (ClosedXML)
        if (emitXlsx && rowList.Count > 0)
        {
            using (var workbook = new XLWorkbook())
            {
                var ws = workbook.Worksheets.Add("Usuarios Retirados");

                // Headers
                ws.Cell(1, 1).Value = "ESTADO";
                ws.Cell(1, 2).Value = "DOCUMENTO";
                ws.Cell(1, 3).Value = "NOMBRE SOCIEDAD";
                ws.Cell(1, 4).Value = "FECHA EVENTO";

                var headerRange = ws.Range(1, 1, 1, 4);
                headerRange.Style.Font.Bold = true;
                headerRange.Style.Font.FontColor = XLColor.White;
                headerRange.Style.Fill.BackgroundColor = XLColor.FromHtml("#1F4E78");
                headerRange.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

                int rowIndex = 2;
                foreach (var row in rowList)
                {
                    ws.Cell(rowIndex, 1).SetValue(row.Estado);
                    ws.Cell(rowIndex, 2).SetValue(row.Documento);
                    ws.Cell(rowIndex, 3).SetValue(row.Sociedad);

                    // Keep FECHA EVENTO as explicit string
                    ws.Cell(rowIndex, 4).SetValue(row.FechaEvento);

                    rowIndex++;
                }

                ws.SheetView.FreezeRows(1);
                ws.Columns(1, 4).AdjustToContents();

                workbook.SaveAs(xlsxPath);
            }
            generatedFiles.Add(xlsxPath);
        }

        return new ExportResult(
            GeneratedFilePaths: generatedFiles,
            DtuFileName: emitDtu ? dtuName : null,
            XlsxFileName: emitXlsx && rowList.Count > 0 ? xlsxName : null,
            TsvFileName: emitTsv ? tsvName : null
        );
    }
}
