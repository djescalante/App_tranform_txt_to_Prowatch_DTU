using System.Security.Cryptography;
using System.Text;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Caching.Memory;

namespace PWExtendedApp.Server.Services.Ocupacion;

public record OcupacionFiltro(
    string? FechaDesde, string? FechaHasta, string? Empresa, string? Ciudad,
    string? Sede, string? Panel, string? Cedula, string? Nombre)
{
    public string CacheKey => string.Join("|", FechaDesde, FechaHasta, Empresa, Ciudad, Sede, Panel, Cedula, Nombre);
}

public record ConteoItem(string Valor, long Total, string? Nombre = null);
public record ConteoDia(string Fecha, long Total);

public record OcupacionStats(
    long Total, long Personas, long Empresas, long Ciudades, long Archivos,
    string? FechaMin, string? FechaMax, List<ConteoDia> PorDia,
    List<ConteoItem> TopEmpresas, List<ConteoItem> TopCiudades, List<ConteoItem> TopCedulas,
    DateTime CalculadoEn);

public record OcupacionFiltros(List<string> Empresas, List<string> Ciudades, List<string> Sedes, List<string> Paneles);

public record OcupacionPagina(long Total, int Page, int PerPage, int Pages, List<Dictionary<string, object?>> Items);

public record OcupacionArchivo(
    long Id, string Nombre, string? Ruta, string? HashSha256, long FilasLeidas, long FilasInsertadas,
    long FilasDuplicadas, long FilasError, string? CargadoEn);

/// <summary>
/// Consultas, exportación y carga del módulo "Ocupación Edificios" (port de
/// routers/registros.py y routers/upload.py). Las estadísticas y los filtros se
/// cachean y se invalidan solo cuando cambian los datos (carga o borrado).
/// </summary>
public class OcupacionService
{
    public const int ExportLimit = 500_000;

    private static readonly Dictionary<string, string> Sortable = new()
    {
        ["fecha"] = "r.fecha",
        ["cedula"] = "r.cedula",
        ["nombres"] = "r.nombres",
        ["apellidos"] = "r.apellidos",
        ["empresa"] = "r.empresa",
        ["ciudad"] = "r.ciudad",
        ["sede"] = "r.sede_administrativa",
        ["panel"] = "r.panel",
        ["first_swipe"] = "r.first_swipe",
        ["last_swipe"] = "r.last_swipe",
    };

    public static readonly (string Column, string Label)[] ExportColumns =
    [
        ("fecha", "Fecha"),
        ("nombres", "Nombres"),
        ("apellidos", "Apellidos"),
        ("cedula", "Cedula"),
        ("empresa", "Empresa"),
        ("ciudad", "Ciudad"),
        ("sede_administrativa", "Sede Administrativa"),
        ("panel", "Panel"),
        ("tarjeta_acceso", "Tarjeta de Acceso"),
        ("first_swipe", "First Swipe of Day"),
        ("last_swipe", "Last Swipe of Day"),
    ];

    private readonly OcupacionStore _store;
    private readonly IMemoryCache _cache;
    private readonly ILogger<OcupacionService> _logger;
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private int _dataVersion;

    public OcupacionService(OcupacionStore store, IMemoryCache cache, ILogger<OcupacionService> logger)
    {
        _store = store;
        _cache = cache;
        _logger = logger;
    }

    // ---------- Filtros SQL (db.build_where) ----------

    private static (string Where, List<SqliteParameter> Params) BuildWhere(OcupacionFiltro f)
    {
        var clauses = new List<string>();
        var ps = new List<SqliteParameter>();
        void Eq(string column, string? value, string name)
        {
            if (string.IsNullOrWhiteSpace(value)) return;
            clauses.Add($"{column} = ${name}");
            ps.Add(new SqliteParameter("$" + name, value.Trim()));
        }

        if (!string.IsNullOrWhiteSpace(f.FechaDesde))
        {
            clauses.Add("fecha >= $desde");
            ps.Add(new SqliteParameter("$desde", f.FechaDesde));
        }
        if (!string.IsNullOrWhiteSpace(f.FechaHasta))
        {
            clauses.Add("fecha <= $hasta");
            ps.Add(new SqliteParameter("$hasta", f.FechaHasta));
        }
        Eq("empresa", f.Empresa, "empresa");
        Eq("ciudad", f.Ciudad, "ciudad");
        Eq("sede_administrativa", f.Sede, "sede");
        Eq("panel", f.Panel, "panel");
        Eq("cedula", f.Cedula, "cedula");
        if (!string.IsNullOrWhiteSpace(f.Nombre))
        {
            clauses.Add("(nombres LIKE $nombre OR apellidos LIKE $nombre)");
            ps.Add(new SqliteParameter("$nombre", $"%{f.Nombre.Trim()}%"));
        }
        return (clauses.Count > 0 ? " WHERE " + string.Join(" AND ", clauses) : string.Empty, ps);
    }

    /// <summary>Agrega una condición extra a un WHERE existente (corrige el "WHERE ... WHERE" del original).</summary>
    private static string And(string where, string condition) =>
        string.IsNullOrEmpty(where) ? " WHERE " + condition : where + " AND " + condition;

    private static SqliteCommand Command(SqliteConnection conn, string sql, IEnumerable<SqliteParameter> ps)
    {
        var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.CommandTimeout = 300;
        foreach (var p in ps) cmd.Parameters.Add(new SqliteParameter(p.ParameterName, p.Value));
        return cmd;
    }

    private static object? Scalar(SqliteConnection conn, string sql, IEnumerable<SqliteParameter> ps)
    {
        using var cmd = Command(conn, sql, ps);
        var v = cmd.ExecuteScalar();
        return v is DBNull ? null : v;
    }

    // ---------- Dashboard ----------

    public Task<OcupacionStats> GetStatsAsync(OcupacionFiltro f, CancellationToken ct = default)
    {
        var key = $"oc:stats:{_dataVersion}:{f.CacheKey}";
        return _cache.GetOrCreateAsync(key, entry =>
        {
            entry.SlidingExpiration = TimeSpan.FromHours(6);
            return Task.Run(() => ComputeStats(f), ct);
        })!;
    }

    private OcupacionStats ComputeStats(OcupacionFiltro f)
    {
        var (where, ps) = BuildWhere(f);
        var basee = $"FROM registros r {where}";
        using var conn = _store.Open();

        long total = 0, personas = 0, empresas = 0, ciudades = 0;
        string? fmin = null, fmax = null;
        using (var cmd = Command(conn,
            $"SELECT COUNT(*), COUNT(DISTINCT cedula), COUNT(DISTINCT empresa), COUNT(DISTINCT ciudad), " +
            $"MIN(fecha), MAX(fecha) {basee}", ps))
        using (var rd = cmd.ExecuteReader())
        {
            if (rd.Read())
            {
                total = rd.GetInt64(0); personas = rd.GetInt64(1);
                empresas = rd.GetInt64(2); ciudades = rd.GetInt64(3);
                fmin = rd.IsDBNull(4) ? null : rd.GetString(4);
                fmax = rd.IsDBNull(5) ? null : rd.GetString(5);
            }
        }

        var porDia = new List<ConteoDia>();
        using (var cmd = Command(conn, $"SELECT fecha, COUNT(*) {basee} GROUP BY fecha ORDER BY fecha", ps))
        using (var rd = cmd.ExecuteReader())
        {
            while (rd.Read()) porDia.Add(new ConteoDia(rd.IsDBNull(0) ? "" : rd.GetString(0), rd.GetInt64(1)));
        }

        List<ConteoItem> Top(string column)
        {
            var list = new List<ConteoItem>();
            using var cmd = Command(conn,
                $"SELECT {column}, COUNT(*) c FROM registros r {And(where, $"{column} != ''")} " +
                $"GROUP BY {column} ORDER BY c DESC LIMIT 10", ps);
            using var rd = cmd.ExecuteReader();
            while (rd.Read()) list.Add(new ConteoItem(rd.GetString(0), rd.GetInt64(1)));
            return list;
        }

        var topCedulas = new List<ConteoItem>();
        using (var cmd = Command(conn,
            $"SELECT cedula, MAX(nombres || ' ' || apellidos) nom, COUNT(*) c {basee} " +
            $"GROUP BY cedula ORDER BY c DESC LIMIT 10", ps))
        using (var rd = cmd.ExecuteReader())
        {
            while (rd.Read())
            {
                topCedulas.Add(new ConteoItem(rd.IsDBNull(0) ? "" : rd.GetString(0), rd.GetInt64(2),
                    rd.IsDBNull(1) ? null : rd.GetString(1)));
            }
        }

        var archivos = Convert.ToInt64(Scalar(conn, "SELECT COUNT(*) FROM archivos", []));
        return new OcupacionStats(total, personas, empresas, ciudades, archivos, fmin, fmax, porDia,
            Top("empresa"), Top("ciudad"), topCedulas, DateTime.Now);
    }

    public Task<OcupacionFiltros> GetFiltrosAsync(CancellationToken ct = default)
    {
        return _cache.GetOrCreateAsync($"oc:filtros:{_dataVersion}", entry =>
        {
            entry.SlidingExpiration = TimeSpan.FromHours(6);
            return Task.Run(() =>
            {
                using var conn = _store.Open();
                List<string> Distinct(string column)
                {
                    var list = new List<string>();
                    using var cmd = Command(conn,
                        $"SELECT DISTINCT {column} FROM registros WHERE {column} IS NOT NULL AND {column} != '' " +
                        $"ORDER BY {column} LIMIT 5000", []);
                    using var rd = cmd.ExecuteReader();
                    while (rd.Read()) list.Add(rd.GetString(0));
                    return list;
                }
                return new OcupacionFiltros(Distinct("empresa"), Distinct("ciudad"),
                    Distinct("sede_administrativa"), Distinct("panel"));
            }, ct);
        })!;
    }

    // ---------- Consulta paginada ----------

    public OcupacionPagina GetRegistros(OcupacionFiltro f, int page, int perPage, string? sort, string? order)
    {
        page = Math.Max(1, page);
        perPage = Math.Clamp(perPage, 1, 500);
        var sortCol = sort != null && Sortable.TryGetValue(sort, out var s) ? s : "r.fecha";
        var dir = string.Equals(order, "asc", StringComparison.OrdinalIgnoreCase) ? "ASC" : "DESC";
        var (where, ps) = BuildWhere(f);

        using var conn = _store.Open();
        var total = Convert.ToInt64(Scalar(conn, $"SELECT COUNT(*) FROM registros r {where}", ps));

        var items = new List<Dictionary<string, object?>>();
        var paged = new List<SqliteParameter>(ps)
        {
            new("$limit", perPage),
            new("$offset", (long)(page - 1) * perPage)
        };
        using (var cmd = Command(conn,
            "SELECT r.id, r.fecha, r.nombres, r.apellidos, r.panel, r.sede_administrativa, r.cedula, " +
            "r.tarjeta_acceso, r.ciudad, r.empresa, r.first_swipe, r.last_swipe, a.nombre AS archivo " +
            $"FROM registros r LEFT JOIN archivos a ON a.id = r.archivo_id {where} " +
            $"ORDER BY {sortCol} {dir}, r.id {dir} LIMIT $limit OFFSET $offset", paged))
        using (var rd = cmd.ExecuteReader())
        {
            while (rd.Read())
            {
                var row = new Dictionary<string, object?>();
                for (int i = 0; i < rd.FieldCount; i++) row[rd.GetName(i)] = rd.IsDBNull(i) ? null : rd.GetValue(i);
                items.Add(row);
            }
        }

        var pages = (int)Math.Ceiling(total / (double)perPage);
        return new OcupacionPagina(total, page, perPage, pages, items);
    }

    // ---------- Exportación ----------

    private IEnumerable<string?[]> ExportRows(OcupacionFiltro f)
    {
        var (where, ps) = BuildWhere(f);
        var cols = string.Join(", ", ExportColumns.Select(c => c.Column));
        using var conn = _store.Open();
        using var cmd = Command(conn,
            $"SELECT {cols} FROM registros r {where} ORDER BY r.fecha, r.cedula LIMIT {ExportLimit}", ps);
        using var rd = cmd.ExecuteReader();
        while (rd.Read())
        {
            var row = new string?[rd.FieldCount];
            for (int i = 0; i < rd.FieldCount; i++) row[i] = rd.IsDBNull(i) ? "" : Convert.ToString(rd.GetValue(i));
            yield return row;
        }
    }

    /// <summary>CSV separado por ';' con BOM UTF-8 (compatible con Excel en español).</summary>
    public async Task WriteCsvAsync(OcupacionFiltro f, Stream output, CancellationToken ct)
    {
        await using var writer = new StreamWriter(output, new UTF8Encoding(true), 1 << 16) { NewLine = "\r\n" };
        static string Quote(string? v)
        {
            v ??= "";
            return v.IndexOfAny([';', '"', '\r', '\n']) >= 0 ? "\"" + v.Replace("\"", "\"\"") + "\"" : v;
        }
        await writer.WriteLineAsync(string.Join(";", ExportColumns.Select(c => Quote(c.Label))));
        foreach (var row in ExportRows(f))
        {
            ct.ThrowIfCancellationRequested();
            await writer.WriteLineAsync(string.Join(";", row.Select(Quote)));
        }
    }

    /// <summary>
    /// XLSX escrito en streaming (SAX) a un archivo temporal: hasta 500.000 filas sin
    /// cargar la hoja completa en memoria.
    /// </summary>
    public string WriteXlsxToTempFile(OcupacionFiltro f)
    {
        var path = Path.Combine(Path.GetTempPath(), $"ocupacion_{Guid.NewGuid():N}.xlsx");
        using var doc = SpreadsheetDocument.Create(path, SpreadsheetDocumentType.Workbook);
        var wbPart = doc.AddWorkbookPart();
        var wsPart = wbPart.AddNewPart<WorksheetPart>();

        using (var w = OpenXmlWriter.Create(wsPart))
        {
            w.WriteStartElement(new Worksheet());
            w.WriteStartElement(new SheetData());

            void WriteRow(IEnumerable<string?> values)
            {
                w.WriteStartElement(new Row());
                foreach (var v in values)
                {
                    w.WriteStartElement(new Cell { DataType = CellValues.InlineString });
                    w.WriteElement(new InlineString(new Text(v ?? "") { Space = SpaceProcessingModeValues.Preserve }));
                    w.WriteEndElement();
                }
                w.WriteEndElement();
            }

            WriteRow(ExportColumns.Select(c => c.Label));
            foreach (var row in ExportRows(f)) WriteRow(row);

            w.WriteEndElement(); // SheetData
            w.WriteEndElement(); // Worksheet
        }

        wbPart.Workbook = new Workbook(new Sheets(new Sheet
        {
            Id = wbPart.GetIdOfPart(wsPart),
            SheetId = 1,
            Name = "Consulta"
        }));
        wbPart.Workbook.Save();
        return path;
    }

    // ---------- Carga y archivos ----------

    public async Task<OcupacionCargaResultado> CargarAsync(string nombre, byte[] content, CancellationToken ct)
    {
        var hash = Convert.ToHexStringLower(SHA256.HashData(content));

        // Un solo escritor a la vez: SQLite no admite escrituras concurrentes.
        await _writeLock.WaitAsync(ct);
        try
        {
            using var conn = _store.Open();
            var existing = Scalar(conn, "SELECT id FROM archivos WHERE hash_sha256 = $h", [new("$h", hash)]);
            if (existing != null)
            {
                return new OcupacionCargaResultado(nombre, "ya_cargado", Convert.ToInt64(existing));
            }

            List<OcupacionRegistro> registros;
            int errores;
            try
            {
                using var ms = new MemoryStream(content);
                (registros, errores) = OcupacionIngest.Parse(ms, nombre);
            }
            catch (ExcelEstructuraException ex)
            {
                // Mensajes propios (estructura del archivo): se muestran tal cual.
                return new OcupacionCargaResultado(nombre, "error", Mensaje: ex.Message);
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or InvalidOperationException
                                           or FormatException or ArgumentException or NotSupportedException)
            {
                var code = ErrorReference.NewCode();
                _logger.LogWarning(ex, "No se pudo leer el Excel {Nombre} (ref {Ref})", nombre, code);
                return new OcupacionCargaResultado(nombre, "error",
                    Mensaje: $"El archivo no es un Excel válido o está dañado (ref {code}).");
            }

            using var tx = conn.BeginTransaction();
            long archivoId;
            using (var cmd = conn.CreateCommand())
            {
                cmd.Transaction = tx;
                cmd.CommandText =
                    "INSERT INTO archivos (nombre, ruta, hash_sha256, filas_leidas, filas_error) " +
                    "VALUES ($n, $r, $h, $l, $e); SELECT last_insert_rowid();";
                cmd.Parameters.AddWithValue("$n", nombre);
                cmd.Parameters.AddWithValue("$r", $"upload:{nombre}");
                cmd.Parameters.AddWithValue("$h", hash);
                cmd.Parameters.AddWithValue("$l", registros.Count);
                cmd.Parameters.AddWithValue("$e", errores);
                archivoId = Convert.ToInt64(cmd.ExecuteScalar());
            }

            int insertadas = 0;
            using (var cmd = conn.CreateCommand())
            {
                cmd.Transaction = tx;
                cmd.CommandText =
                    "INSERT OR IGNORE INTO registros (archivo_id, fecha, nombres, apellidos, panel, " +
                    "sede_administrativa, cedula, tarjeta_acceso, ciudad, empresa, first_swipe, last_swipe, fingerprint) " +
                    "VALUES ($a, $f, $n, $ap, $p, $s, $c, $t, $ci, $e, $fs, $ls, $fp)";
                var p = new[] { "$a", "$f", "$n", "$ap", "$p", "$s", "$c", "$t", "$ci", "$e", "$fs", "$ls", "$fp" }
                    .Select(n => cmd.Parameters.Add(n, SqliteType.Text)).ToArray();
                p[0].SqliteType = SqliteType.Integer;
                cmd.Prepare();
                foreach (var r in registros)
                {
                    p[0].Value = archivoId;
                    p[1].Value = (object?)r.Fecha ?? DBNull.Value;
                    p[2].Value = r.Nombres; p[3].Value = r.Apellidos; p[4].Value = r.Panel;
                    p[5].Value = r.SedeAdministrativa; p[6].Value = r.Cedula; p[7].Value = r.TarjetaAcceso;
                    p[8].Value = r.Ciudad; p[9].Value = r.Empresa; p[10].Value = r.FirstSwipe;
                    p[11].Value = r.LastSwipe; p[12].Value = r.Fingerprint;
                    insertadas += cmd.ExecuteNonQuery();
                }
            }

            var duplicadas = registros.Count - insertadas;
            using (var cmd = conn.CreateCommand())
            {
                cmd.Transaction = tx;
                cmd.CommandText = "UPDATE archivos SET filas_insertadas = $i, filas_duplicadas = $d WHERE id = $id";
                cmd.Parameters.AddWithValue("$i", insertadas);
                cmd.Parameters.AddWithValue("$d", duplicadas);
                cmd.Parameters.AddWithValue("$id", archivoId);
                cmd.ExecuteNonQuery();
            }
            tx.Commit();
            Interlocked.Increment(ref _dataVersion);

            return new OcupacionCargaResultado(nombre, "cargado", archivoId, registros.Count, insertadas,
                duplicadas, errores);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    public List<OcupacionArchivo> GetArchivos()
    {
        using var conn = _store.Open();
        using var cmd = Command(conn,
            "SELECT id, nombre, ruta, hash_sha256, filas_leidas, filas_insertadas, filas_duplicadas, " +
            "filas_error, cargado_en FROM archivos ORDER BY cargado_en DESC, id DESC", []);
        using var rd = cmd.ExecuteReader();
        var list = new List<OcupacionArchivo>();
        while (rd.Read())
        {
            list.Add(new OcupacionArchivo(rd.GetInt64(0), rd.GetString(1),
                rd.IsDBNull(2) ? null : rd.GetString(2), rd.IsDBNull(3) ? null : rd.GetString(3),
                rd.IsDBNull(4) ? 0 : rd.GetInt64(4), rd.IsDBNull(5) ? 0 : rd.GetInt64(5),
                rd.IsDBNull(6) ? 0 : rd.GetInt64(6), rd.IsDBNull(7) ? 0 : rd.GetInt64(7),
                rd.IsDBNull(8) ? null : rd.GetString(8)));
        }
        return list;
    }

    /// <summary>Elimina el archivo y sus registros. Devuelve null si no existe.</summary>
    public async Task<(string Nombre, int Borrados)?> EliminarArchivoAsync(long id, CancellationToken ct)
    {
        await _writeLock.WaitAsync(ct);
        try
        {
            using var conn = _store.Open();
            var nombre = Scalar(conn, "SELECT nombre FROM archivos WHERE id = $id", [new("$id", id)]) as string;
            if (nombre == null) return null;

            using var tx = conn.BeginTransaction();
            int borrados;
            using (var cmd = conn.CreateCommand())
            {
                cmd.Transaction = tx;
                cmd.CommandTimeout = 300;
                cmd.CommandText = "DELETE FROM registros WHERE archivo_id = $id";
                cmd.Parameters.AddWithValue("$id", id);
                borrados = cmd.ExecuteNonQuery();
            }
            using (var cmd = conn.CreateCommand())
            {
                cmd.Transaction = tx;
                cmd.CommandText = "DELETE FROM archivos WHERE id = $id";
                cmd.Parameters.AddWithValue("$id", id);
                cmd.ExecuteNonQuery();
            }
            tx.Commit();
            Interlocked.Increment(ref _dataVersion);
            return (nombre, borrados);
        }
        finally
        {
            _writeLock.Release();
        }
    }
}
