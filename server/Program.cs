using System.Security.Authentication;
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using UsuariosRetirados.Server;
using UsuariosRetirados.Server.Data;
using UsuariosRetirados.Server.Services;

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
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection") 
                       ?? "Data Source=usuarios_retirados.db";
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlite(connectionString));

// Business Services
builder.Services.AddSingleton<ICsvStreamingEngine, CsvStreamingEngine>();
builder.Services.AddSingleton<IExportService, ExportService>();
builder.Services.AddSingleton<ISchemaValidator, SchemaValidator>();
builder.Services.AddScoped<IJwtService, JwtService>();
builder.Services.AddMemoryCache();
builder.Services.AddSingleton<IScanCache, ScanCache>();

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
        OnMessageReceived = context =>
        {
            var accessToken = context.Request.Query["token"];
            if (!string.IsNullOrEmpty(accessToken))
            {
                context.Token = accessToken;
            }
            return Task.CompletedTask;
        }
    };
});

builder.Services.AddAuthorization();

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
app.UseAuthorization();

app.MapControllers();

// SPA Fallback
app.MapFallbackToFile("index.html");

app.Run();
