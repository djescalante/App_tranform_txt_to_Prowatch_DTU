using Microsoft.Data.Sqlite;

namespace PWExtendedApp.Server.Services.Ocupacion;

/// <summary>
/// Acceso a la base SQLite de "Ocupación Edificios" (prowatch.db). Es una base
/// aparte de usuarios_retirados.db y conserva el esquema de la app Python original,
/// así que la base existente se usa tal cual.
/// </summary>
public class OcupacionStore
{
    private const string Schema = """
        CREATE TABLE IF NOT EXISTS archivos (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            nombre TEXT NOT NULL,
            ruta TEXT,
            hash_sha256 TEXT UNIQUE,
            filas_leidas INTEGER DEFAULT 0,
            filas_insertadas INTEGER DEFAULT 0,
            filas_duplicadas INTEGER DEFAULT 0,
            filas_error INTEGER DEFAULT 0,
            cargado_en TEXT DEFAULT (datetime('now', 'localtime'))
        );

        CREATE TABLE IF NOT EXISTS registros (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            archivo_id INTEGER REFERENCES archivos(id) ON DELETE CASCADE,
            fecha TEXT,
            nombres TEXT,
            apellidos TEXT,
            panel TEXT,
            sede_administrativa TEXT,
            cedula TEXT,
            tarjeta_acceso TEXT,
            ciudad TEXT,
            empresa TEXT,
            first_swipe TEXT,
            last_swipe TEXT,
            fingerprint TEXT
        );

        CREATE INDEX IF NOT EXISTS idx_registros_fecha ON registros(fecha);
        CREATE INDEX IF NOT EXISTS idx_registros_cedula ON registros(cedula);
        CREATE INDEX IF NOT EXISTS idx_registros_empresa ON registros(empresa);
        CREATE INDEX IF NOT EXISTS idx_registros_ciudad ON registros(ciudad);
        CREATE INDEX IF NOT EXISTS idx_registros_sede ON registros(sede_administrativa);
        CREATE INDEX IF NOT EXISTS idx_registros_panel ON registros(panel);
        CREATE INDEX IF NOT EXISTS idx_registros_fecha_empresa ON registros(fecha, empresa);
        CREATE INDEX IF NOT EXISTS idx_registros_archivo ON registros(archivo_id);
        CREATE UNIQUE INDEX IF NOT EXISTS idx_registros_fingerprint ON registros(fingerprint);
        """;

    private readonly string _connectionString;

    public string DbPath { get; }

    public OcupacionStore(IConfiguration config, IWebHostEnvironment env)
    {
        var configured = config["Ocupacion:DbPath"];
        if (string.IsNullOrWhiteSpace(configured)) configured = Path.Combine("data", "prowatch.db");
        DbPath = Path.GetFullPath(Path.IsPathRooted(configured)
            ? configured
            : Path.Combine(env.ContentRootPath, configured));

        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = DbPath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            DefaultTimeout = 60
        }.ToString();
    }

    public SqliteConnection Open()
    {
        var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "PRAGMA foreign_keys = ON;";
        cmd.ExecuteNonQuery();
        return conn;
    }

    /// <summary>Crea la base y el esquema si no existen (idempotente).</summary>
    public void EnsureSchema()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(DbPath)!);
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = Schema;
        cmd.ExecuteNonQuery();
    }
}
