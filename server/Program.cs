using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using UsuariosRetirados.Server.Data;
using UsuariosRetirados.Server.Services;

var builder = WebApplication.CreateBuilder(args);

// Enable Windows Service execution when installed as a service
builder.Host.UseWindowsService();

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

// JWT Authentication
var jwtSecret = builder.Configuration["Jwt:Secret"] 
                ?? "UsuariosRetirados_SecretKey_2026_Enterprise_DTU_SecurityKey_UltraSecure!";
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
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll", policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyMethod()
              .AllowAnyHeader();
    });
});

var app = builder.Build();

// Seed initial database
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await AppDbContext.SeedInitialDataAsync(db);
}

app.UseCors("AllowAll");

// Serve frontend static files
app.UseDefaultFiles();
app.UseStaticFiles();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

// SPA Fallback
app.MapFallbackToFile("index.html");

app.Run();
