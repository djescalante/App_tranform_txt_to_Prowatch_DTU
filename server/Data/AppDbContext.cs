using Microsoft.EntityFrameworkCore;
using UsuariosRetirados.Server.Models;

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

    public static async Task SeedInitialDataAsync(AppDbContext db)
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

        // Default configs
        if (!await db.AppConfigs.AnyAsync(c => c.Key == "InputPath"))
        {
            db.AppConfigs.Add(new AppConfig
            {
                Key = "InputPath",
                Value = @"E:\CarpetaTrabajoIA\empleados\Empleados.txt",
                UpdatedAt = DateTime.UtcNow
            });
        }

        if (!await db.AppConfigs.AnyAsync(c => c.Key == "OutputDir"))
        {
            db.AppConfigs.Add(new AppConfig
            {
                Key = "OutputDir",
                Value = @"E:\CarpetaTrabajoIA\empleados\UsuariosRetiradosDTU\salidas",
                UpdatedAt = DateTime.UtcNow
            });
        }

        await db.SaveChangesAsync();
    }
}
