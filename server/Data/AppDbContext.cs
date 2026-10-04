using Microsoft.EntityFrameworkCore;
using UsuariosRetirados.Server.Models;
using UsuariosRetirados.Server.Services;

namespace UsuariosRetirados.Server.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
    {
    }

    public DbSet<User> Users => Set<User>();
    public DbSet<ProcessingJob> ProcessingJobs => Set<ProcessingJob>();
    public DbSet<AppConfig> AppConfigs => Set<AppConfig>();
    public DbSet<VipEmployee> VipEmployees => Set<VipEmployee>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<User>(entity =>
        {
            entity.HasIndex(u => u.Username).IsUnique();
        });

        modelBuilder.Entity<ProcessingJob>(entity =>
        {
            entity.HasIndex(j => j.CreatedAt);
        });

        modelBuilder.Entity<VipEmployee>(entity =>
        {
            // NOCASE: la unicidad y las búsquedas de cédula son case-insensitive.
            entity.Property(v => v.Cedula).UseCollation("NOCASE");
            entity.HasIndex(v => v.Cedula).IsUnique();
        });
    }

    /// <summary>
    /// EnsureCreated no altera bases existentes: agrega la tabla VIP y las columnas
    /// de auditoría de omisiones de forma idempotente.
    /// </summary>
    private static async Task EnsureSchemaUpgradesAsync(AppDbContext db)
    {
        await db.Database.ExecuteSqlRawAsync(
            """
            CREATE TABLE IF NOT EXISTS "VipEmployees" (
                "Id" INTEGER NOT NULL CONSTRAINT "PK_VipEmployees" PRIMARY KEY AUTOINCREMENT,
                "Cedula" TEXT NOT NULL COLLATE NOCASE,
                "FullName" TEXT NOT NULL,
                "CreatedByUsername" TEXT NULL,
                "CreatedAt" TEXT NOT NULL,
                "UpdatedAt" TEXT NULL
            );
            """);

        await db.Database.ExecuteSqlRawAsync(
            """CREATE UNIQUE INDEX IF NOT EXISTS "IX_VipEmployees_Cedula" ON "VipEmployees" ("Cedula");""");

        var columns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var connection = db.Database.GetDbConnection();
        await connection.OpenAsync();
        try
        {
            using var cmd = connection.CreateCommand();
            cmd.CommandText = "PRAGMA table_info('ProcessingJobs');";
            using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                columns.Add(reader.GetString(1));
            }
        }
        finally
        {
            await connection.CloseAsync();
        }

        if (!columns.Contains("VipOmittedCount"))
        {
            await db.Database.ExecuteSqlRawAsync(
                """ALTER TABLE "ProcessingJobs" ADD COLUMN "VipOmittedCount" INTEGER NOT NULL DEFAULT 0;""");
        }

        if (!columns.Contains("VipOmittedDetails"))
        {
            await db.Database.ExecuteSqlRawAsync(
                """ALTER TABLE "ProcessingJobs" ADD COLUMN "VipOmittedDetails" TEXT NULL;""");
        }
    }

    public static async Task SeedInitialDataAsync(AppDbContext db, IConfiguration? config = null)
    {
        await db.Database.EnsureCreatedAsync();
        await EnsureSchemaUpgradesAsync(db);

        if (!await db.Users.AnyAsync())
        {
            var admin = new User
            {
                Username = "admin",
                FullName = "Administrador del Sistema",
                Role = "Admin",
                PasswordHash = BCrypt.Net.BCrypt.HashPassword("Admin123!"),
                IsActive = true,
                CreatedAt = DateTime.UtcNow
            };
            db.Users.Add(admin);

            for (int i = 1; i <= 5; i++)
            {
                var op = new User
                {
                    Username = $"operador{i}",
                    FullName = $"Analista Operador {i}",
                    Role = "Operator",
                    PasswordHash = BCrypt.Net.BCrypt.HashPassword("Operador123!"),
                    IsActive = true,
                    CreatedAt = DateTime.UtcNow
                };
                db.Users.Add(op);
            }

            await db.SaveChangesAsync();
        }

        // Default configs (configurable via appsettings AppPaths:*; never hardcode machine paths)
        if (!await db.AppConfigs.AnyAsync(c => c.Key == "InputPath"))
        {
            var configured = config?["AppPaths:InputPath"];
            var value = string.IsNullOrWhiteSpace(configured)
                ? AppPaths.FindUpward("Empleados.txt") ?? string.Empty
                : configured;

            db.AppConfigs.Add(new AppConfig
            {
                Key = "InputPath",
                Value = value,
                UpdatedAt = DateTime.UtcNow
            });
        }

        if (!await db.AppConfigs.AnyAsync(c => c.Key == "OutputDir"))
        {
            var configured = config?["AppPaths:OutputDir"];
            var value = string.IsNullOrWhiteSpace(configured)
                ? AppPaths.FindUpward(AppPaths.OutputRelative) ?? string.Empty
                : configured;

            db.AppConfigs.Add(new AppConfig
            {
                Key = "OutputDir",
                Value = value,
                UpdatedAt = DateTime.UtcNow
            });
        }

        await db.SaveChangesAsync();
    }
}
