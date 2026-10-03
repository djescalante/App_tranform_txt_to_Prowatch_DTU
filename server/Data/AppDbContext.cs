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
    }

    public static async Task SeedInitialDataAsync(AppDbContext db, IConfiguration? config = null)
    {
        await db.Database.EnsureCreatedAsync();

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
