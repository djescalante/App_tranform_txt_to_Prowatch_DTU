using System.Text;
using ClosedXML.Excel;
using PWExtendedApp.Server.Services;

namespace PWExtendedApp.Server.Tests;

/// <summary>
/// Lectura del padrón y archivos DTU/TSV/XLSX. El TXT DTU lo consume ProWatch: su contenido se
/// fija aquí byte a byte. Datos de Fixtures/padron_muestra.txt (inventados, Windows-1252).
/// </summary>
public class PadronDtuTests : IDisposable
{
    private static readonly string Padron = Path.Combine(AppContext.BaseDirectory, "Fixtures", "padron_muestra.txt");
    private static readonly byte[] Bom = [0xEF, 0xBB, 0xBF];

    private readonly CsvStreamingEngine _engine = new();
    private readonly string _outDir = Path.Combine(Path.GetTempPath(), "pw_tests_" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_outDir)) Directory.Delete(_outDir, recursive: true);
    }

    private FilterResult Filtrar(string fecha, string[] sociedades, string[] estados) =>
        _engine.Filter(_engine.Load(Padron), fecha, sociedades, estados);

    private static string[] Docs(FilterResult r) => r.Rows.Select(x => x.Documento).ToArray();

    [Fact]
    public void Load_cuenta_filas_y_descarta_la_malformada()
    {
        var padron = _engine.Load(Padron);

        Assert.Equal(1, padron.MalformedCount);   // comillas sin cerrar
        Assert.Equal(11, padron.TotalRows);       // 10 completas + 1 corta
        Assert.Equal(10, padron.Rows.Count);      // la corta no tiene FECHA EVENTO
    }

    [Fact]
    public void Load_lee_Windows1252_y_recorta_espacios()
    {
        var rows = _engine.Load(Padron).Rows;

        Assert.Equal("José", rows[0].Nombres);
        Assert.Equal("Peña Núñez", rows[0].Apellidos);
        Assert.Equal("900000009", rows[8].Documento);
        Assert.Equal("Íngrid", rows[8].Nombres);
        Assert.Equal("Ñáñez", rows[8].Apellidos);
    }

    [Fact]
    public void Filtro_por_defecto_respeta_estado_sociedad_fecha_y_orden()
    {
        var r = Filtrar("28/09/2026", ["BANCOLOMBIA"], ["Terminated"]);
        Assert.Equal(["900000001", "900000002", "900000009"], Docs(r));
    }

    [Theory]
    [InlineData("Con terminación de contrato")]
    [InlineData("Con terminacion de contrato")]
    public void Con_terminacion_de_contrato_incluye_ambas_variantes(string estado)
    {
        var r = Filtrar("28/09/2026", ["BANCOLOMBIA"], [estado]);
        Assert.Equal(["900000005", "900000006"], Docs(r));
    }

    [Fact]
    public void Sociedad_TODAS_no_filtra_por_sociedad()
    {
        var r = Filtrar("28/09/2026", ["TODAS"], ["Terminated"]);
        Assert.Equal(["900000001", "900000002", "900000003", "900000009", "900000010"], Docs(r));
    }

    [Fact]
    public void Sin_estados_usa_Terminated()
    {
        var r = Filtrar("28/09/2026", ["BANCOLOMBIA"], []);
        Assert.Equal(["900000001", "900000002", "900000009"], Docs(r));
    }

    [Fact]
    public void Sin_coincidencias_devuelve_vacio()
    {
        Assert.Empty(Filtrar("01/01/2020", ["BANCOLOMBIA"], ["Terminated"]).Rows);
    }

    [Fact]
    public void TXT_DTU_tiene_el_formato_contractual_byte_a_byte()
    {
        var rows = Filtrar("28/09/2026", ["BANCOLOMBIA"], ["Terminated"]).Rows;
        var res = new ExportService().GenerateOutputs(rows, _outDir, "28/09/2026", emitXlsx: false, emitTsv: false, emitDtu: true);

        Assert.Equal("Usuarios Retirados DTU al 28-09-2026.txt", res.DtuFileName);
        var esperado = Bom.Concat(Encoding.UTF8.GetBytes(
            "900000001\t28/09/2026\tT\r\n" +
            "900000002\t28/09/2026\tT\r\n" +
            "900000009\t28/09/2026\tT\r\n")).ToArray();
        Assert.Equal(esperado, File.ReadAllBytes(Path.Combine(_outDir, res.DtuFileName!)));
    }

    [Fact]
    public void TSV_tiene_encabezado_y_columnas_fijas()
    {
        var rows = Filtrar("28/09/2026", ["BANCOLOMBIA"], ["Terminated"]).Rows;
        var res = new ExportService().GenerateOutputs(rows, _outDir, "28/09/2026", emitXlsx: false, emitTsv: true, emitDtu: false);

        var esperado = Bom.Concat(Encoding.UTF8.GetBytes(
            "ESTADO\tDOCUMENTO\tNOMBRE SOCIEDAD\tFECHA EVENTO\r\n" +
            "Terminated\t900000001\tBANCOLOMBIA\t28/09/2026\r\n" +
            "Terminated\t900000002\tBANCOLOMBIA\t28/09/2026\r\n" +
            "Terminated\t900000009\tBANCOLOMBIA\t28/09/2026\r\n")).ToArray();
        Assert.Equal(esperado, File.ReadAllBytes(Path.Combine(_outDir, res.TsvFileName!)));
    }

    [Fact]
    public void XLSX_incluye_nombres_y_deja_la_fecha_como_texto()
    {
        var rows = Filtrar("28/09/2026", ["BANCOLOMBIA"], ["Terminated"]).Rows;
        var res = new ExportService().GenerateOutputs(rows, _outDir, "28/09/2026", emitXlsx: true, emitTsv: false, emitDtu: false);

        using var wb = new XLWorkbook(Path.Combine(_outDir, res.XlsxFileName!));
        var ws = wb.Worksheet(1);
        Assert.Equal(
            ["ESTADO", "DOCUMENTO", "NOMBRE EMPLEADO", "APELLIDO EMPLEADO", "NOMBRE SOCIEDAD", "FECHA EVENTO"],
            Enumerable.Range(1, 6).Select(c => ws.Cell(1, c).GetString()).ToArray());
        Assert.Equal("José", ws.Cell(2, 3).GetString());
        Assert.Equal("Peña Núñez", ws.Cell(2, 4).GetString());
        Assert.Equal(XLDataType.Text, ws.Cell(2, 6).DataType);
        Assert.Equal("28/09/2026", ws.Cell(2, 6).GetString());
        Assert.Equal(4, ws.LastRowUsed()!.RowNumber());
    }
}
