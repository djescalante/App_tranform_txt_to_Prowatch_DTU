using System.Text.Json;
using PWExtendedApp.Server.Services.Ocupacion;

namespace PWExtendedApp.Server.Tests;

/// <summary>
/// Lectura de Excel "Ocupación Edificios": debe coincidir campo por campo, y en el fingerprint
/// que deduplica filas, con la app Python original. Fixtures/Ocupacion/esperado.json lo calculó
/// la lógica de ingest.py (ver generar_fixtures.py) sobre los mismos archivos.
/// </summary>
public class OcupacionIngestTests
{
    private static readonly string Dir = Path.Combine(AppContext.BaseDirectory, "Fixtures", "Ocupacion");
    private static readonly JsonElement Esperado =
        JsonDocument.Parse(File.ReadAllText(Path.Combine(Dir, "esperado.json"))).RootElement;

    public static TheoryData<string> ArchivosValidos()
    {
        var data = new TheoryData<string>();
        foreach (var p in Esperado.EnumerateObject())
        {
            if (p.Value.TryGetProperty("registros", out _)) data.Add(p.Name);
        }
        return data;
    }

    private static (List<OcupacionRegistro> Registros, int Errores) Parse(string archivo)
    {
        using var fs = File.OpenRead(Path.Combine(Dir, archivo));
        return OcupacionIngest.Parse(fs, archivo);
    }

    [Theory]
    [MemberData(nameof(ArchivosValidos))]
    public void Coincide_con_la_referencia_Python(string archivo)
    {
        var esperado = Esperado.GetProperty(archivo);
        var (registros, errores) = Parse(archivo);

        Assert.Equal(esperado.GetProperty("errores").GetInt32(), errores);
        var filas = esperado.GetProperty("registros").EnumerateArray().ToList();
        Assert.Equal(filas.Count, registros.Count);

        for (int i = 0; i < filas.Count; i++)
        {
            var e = filas[i];
            var r = registros[i];
            string? S(string name) => e.GetProperty(name).ValueKind == JsonValueKind.Null ? null : e.GetProperty(name).GetString();

            Assert.Equal(S("fecha"), r.Fecha);
            Assert.Equal(S("nombres"), r.Nombres);
            Assert.Equal(S("apellidos"), r.Apellidos);
            Assert.Equal(S("panel"), r.Panel);
            Assert.Equal(S("sede_administrativa"), r.SedeAdministrativa);
            Assert.Equal(S("cedula"), r.Cedula);
            Assert.Equal(S("tarjeta_acceso"), r.TarjetaAcceso);
            Assert.Equal(S("ciudad"), r.Ciudad);
            Assert.Equal(S("empresa"), r.Empresa);
            Assert.Equal(S("first_swipe"), r.FirstSwipe);
            Assert.Equal(S("last_swipe"), r.LastSwipe);
            Assert.Equal(S("fingerprint"), r.Fingerprint);
        }
    }

    [Fact]
    public void Encabezado_con_metadatos_arriba_y_conversiones()
    {
        var (registros, errores) = Parse("Ocupacion Edificios 3-2-2026.xlsx");

        Assert.Equal(1, errores);                                        // fila sin cédula
        Assert.Equal(5, registros.Count);                                // la fila vacía se ignora
        Assert.Equal(registros[0].Fingerprint, registros[1].Fingerprint); // fila repetida = misma huella
        Assert.Equal("1098765432", registros[2].Cedula);                 // "1098765432.0" -> sin .0
        Assert.Equal("Pérez Díaz", registros[2].Apellidos);              // espacios colapsados
        Assert.Equal("2026-02-03", registros[3].Fecha);                  // sin marcaciones: fecha del nombre
        Assert.Equal("Juan Carlos", registros[4].Nombres);               // tabulador -> espacio
    }

    [Fact]
    public void Excel_sin_encabezado_reconocible_falla_con_mensaje_claro()
    {
        var ex = Assert.Throws<ExcelEstructuraException>(() => Parse("Invalido.xlsx"));
        Assert.Equal(Esperado.GetProperty("Invalido.xlsx").GetProperty("error").GetString(), ex.Message);
    }
}
