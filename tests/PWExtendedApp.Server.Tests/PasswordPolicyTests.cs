using PWExtendedApp.Server.Services;

namespace PWExtendedApp.Server.Tests;

public class PasswordPolicyTests
{
    [Theory]
    [InlineData("Segura2026x")]
    [InlineData("otraClave99")]
    public void Acepta_claves_validas(string clave)
    {
        Assert.Null(PasswordPolicy.Validate(clave, "operador1"));
    }

    [Theory]
    [InlineData("", "al menos 8")]
    [InlineData("corta1", "al menos 8")]
    [InlineData("solotexto", "letras y números")]
    [InlineData("12345678", "letras y números")]
    [InlineData("Operador123!", "inicial")]
    [InlineData("Admin123!", "inicial")]
    [InlineData("operador12", "igual al usuario")]
    public void Rechaza_claves_invalidas(string clave, string motivo)
    {
        var error = PasswordPolicy.Validate(clave, "operador12");
        Assert.NotNull(error);
        Assert.Contains(motivo, error);
    }
}
