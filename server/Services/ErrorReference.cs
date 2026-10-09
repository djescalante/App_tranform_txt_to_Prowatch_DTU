using Microsoft.AspNetCore.Diagnostics;

namespace PWExtendedApp.Server.Services;

/// <summary>
/// Errores hacia el usuario: un mensaje genérico con un código corto de referencia. El detalle
/// técnico (excepción, rutas internas) queda solo en el log, buscable por ese código.
/// </summary>
public static class ErrorReference
{
    public static string NewCode() => Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();

    public static string Message(string what, string code) =>
        $"{what} Si el problema continúa, informe al administrador la referencia {code}.";

    /// <summary>Respuesta para excepciones no controladas en la API.</summary>
    public static void UseApiExceptionHandler(this WebApplication app)
    {
        app.UseExceptionHandler(errorApp => errorApp.Run(async context =>
        {
            var error = context.Features.Get<IExceptionHandlerFeature>()?.Error;
            var code = NewCode();
            var logger = context.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("Errores");
            logger.LogError(error, "Error no controlado {Ref} en {Method} {Path}", code,
                context.Request.Method, context.Request.Path);

            context.Response.StatusCode = StatusCodes.Status500InternalServerError;
            await context.Response.WriteAsJsonAsync(new { message = Message("Ocurrió un error interno.", code) });
        }));
    }
}
