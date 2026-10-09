using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using System.Security.Authentication;
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using PWExtendedApp.Server;
using PWExtendedApp.Server.Data;
using PWExtendedApp.Server.Services;

var builder = WebApplication.CreateBuilder(args);

// Enable Windows Service execution when installed as a service
builder.Host.UseWindowsService();

// Kestrel: HTTPS-only endpoint from configuration, TLS 1.3 enforced.
// Certificate: PFX via Kestrel:Endpoints:Https:Certificate:Path + :Password
// (password from env var Kestrel__Endpoints__Https__Certificate__Password).
builder.WebHost.ConfigureKestrel((context, options) =>
{
    options.Configure(context.Configuration.GetSection("Kestrel"));
    options.AddServerHeader = false;
    options.ConfigureHttpsDefaults(https =>
    {
        https.SslProtocols = SslProtocols.Tls13;
    });
});

// Database
// Una ruta relativa se resuelve contra la carpeta de la app (no contra el directorio
// actual: como servicio de Windows sería C:\Windows\System32).
var dbBuilder = new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder(
    builder.Configuration.GetConnectionString("DefaultConnection") ?? "Data Source=usuarios_retirados.db");
if (!Path.IsPathRooted(dbBuilder.DataSource))
{
    dbBuilder.DataSource = Path.GetFullPath(Path.Combine(builder.Environment.ContentRootPath, dbBuilder.DataSource));
}
Directory.CreateDirectory(Path.GetDirectoryName(dbBuilder.DataSource)!);
var connectionString = dbBuilder.ToString();
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlite(connectionString));

// Business Services
builder.Services.AddSingleton<ICsvStreamingEngine, CsvStreamingEngine>();
builder.Services.AddSingleton<IExportService, ExportService>();
builder.Services.AddSingleton<ISchemaValidator, SchemaValidator>();
builder.Services.AddScoped<IJwtService, JwtService>();
builder.Services.AddMemoryCache();
builder.Services.AddSingleton<PadronCache>();
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<AuditService>();
builder.Services.AddSingleton(new UsersDbPath(dbBuilder.DataSource));
builder.Services.AddSingleton<BackupService>();
builder.Services.AddHostedService<BackupScheduler>();
builder.Services.AddHostedService<PadronWarmup>();

// Módulo Ocupación Edificios (base aparte: prowatch.db)
builder.Services.AddSingleton<PWExtendedApp.Server.Services.Ocupacion.OcupacionStore>();
builder.Services.AddSingleton<PWExtendedApp.Server.Services.Ocupacion.OcupacionService>();
builder.Services.AddHostedService<PWExtendedApp.Server.Services.Ocupacion.OcupacionWarmup>();

// JWT Authentication
var jwtSecret = builder.Configuration["Jwt:Secret"];
if (string.IsNullOrWhiteSpace(jwtSecret))
{
    throw new InvalidOperationException(
        "Falta configurar Jwt:Secret. Defina la variable de entorno 'Jwt__Secret' " +
        "(o el valor en appsettings.Development.json para desarrollo).");
}
var key = Encoding.UTF8.GetBytes(jwtSecret);

builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
})
.AddJwtBearer(options =>
{
    options.RequireHttpsMetadata = false;
    options.SaveToken = true;
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuerSigningKey = true,
        IssuerSigningKey = new SymmetricSecurityKey(key),
        ValidateIssuer = true,
        ValidIssuer = builder.Configuration["Jwt:Issuer"] ?? "UsuariosRetiradosServer",
        ValidateAudience = true,
        ValidAudience = builder.Configuration["Jwt:Audience"] ?? "UsuariosRetiradosClient",
        ClockSkew = TimeSpan.Zero
    };
    options.Events = new JwtBearerEvents
    {
        // El token solo se acepta en el header Authorization (nunca en la URL, donde
        // quedaría en historiales y logs).
        // El token dura 7 días: se rechaza si, después de iniciar sesión, el usuario
        // fue eliminado, desactivado o le cambiaron el rol.
        OnTokenValidated = async context =>
        {
            var idClaim = context.Principal?.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
            var roleClaim = context.Principal?.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value;
            var db = context.HttpContext.RequestServices.GetRequiredService<AppDbContext>();
            var state = int.TryParse(idClaim, out var userId)
                ? await db.Users.AsNoTracking()
                    .Where(u => u.Id == userId && u.IsActive && u.Role == roleClaim)
                    .Select(u => new { u.MustChangePassword })
                    .FirstOrDefaultAsync()
                : null;
            if (state == null)
            {
                context.Fail("Usuario eliminado, inactivo o con rol modificado.");
                return;
            }
            if (state.MustChangePassword)
            {
                context.Principal!.AddIdentity(new System.Security.Claims.ClaimsIdentity(
                    [new System.Security.Claims.Claim(MustChangePasswordClaim, "true")]));
            }
        }
    };
});

builder.Services.AddAuthorization();

// Login: máximo 10 intentos por minuto por IP (además del bloqueo por cuenta en AuthController).
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy("login", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "desconocida",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 10, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
    options.OnRejected = async (ctx, ct) =>
    {
        ctx.HttpContext.Response.ContentType = "application/json; charset=utf-8";
        await ctx.HttpContext.Response.WriteAsync(
            "{\"message\":\"Demasiados intentos de ingreso. Espere un minuto e intente de nuevo.\"}", ct);
    };
});

// Controllers & CORS
builder.Services.AddControllers();

var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
builder.Services.AddCors(options =>
{
    options.AddPolicy("AppCors", policy =>
    {
        if (allowedOrigins.Contains("*"))
        {
            policy.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader();
        }
        else if (allowedOrigins.Length > 0)
        {
            policy.WithOrigins(allowedOrigins).AllowAnyMethod().AllowAnyHeader();
        }
    });
});

var app = builder.Build();

// Excepciones no controladas: mensaje genérico con referencia; el detalle va al log.
app.UseApiExceptionHandler();

// Security headers (HSTS, CSP, anti-sniffing, anti-framing, referrer/permissions).
app.UseMiddleware<SecurityHeadersMiddleware>();

// API responses must never be cached by the browser (downloads with an old
// token were being served from the heuristic cache).
app.Use(async (context, next) =>
{
    if (context.Request.Path.StartsWithSegments("/api"))
    {
        context.Response.Headers.CacheControl = "no-store, no-cache, must-revalidate";
        context.Response.Headers.Pragma = "no-cache";
    }
    await next();
});

// Seed initial database
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await AppDbContext.SeedInitialDataAsync(db, builder.Configuration);
}

if (allowedOrigins.Length > 0)
{
    app.UseCors("AppCors");
}

// Serve frontend static files. no-cache forces revalidation (ETag/304) so
// frontend updates never get stuck in the browser cache.
app.UseDefaultFiles();
app.UseStaticFiles(new StaticFileOptions
{
    OnPrepareResponse = ctx =>
    {
        ctx.Context.Response.Headers.CacheControl = "no-cache, must-revalidate";
    }
});

app.UseAuthentication();

// Contraseña temporal (seed o asignada por un admin): solo se permite ver la sesión y cambiarla.
app.Use(async (context, next) =>
{
    if (context.User.HasClaim(MustChangePasswordClaim, "true") &&
        context.Request.Path.StartsWithSegments("/api") &&
        !context.Request.Path.StartsWithSegments("/api/auth"))
    {
        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        await context.Response.WriteAsJsonAsync(new
        {
            message = "Debe cambiar su contraseña antes de continuar.",
            mustChangePassword = true
        });
        return;
    }
    await next();
});

app.UseRateLimiter();
app.UseAuthorization();

app.MapControllers();

// SPA Fallback
app.MapFallbackToFile("index.html");

app.Run();

partial class Program
{
    private const string MustChangePasswordClaim = "must_change_password";
}
