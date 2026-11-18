using System.Security.Cryptography;
using Microsoft.IdentityModel.Tokens;

namespace Finora.Api.Authentication;

/// <summary>Snapshot imutável de configuração externa, sem exposição textual da chave ou valores recebidos.</summary>
public sealed class JwtSettings
{
    // A chave clona os bytes; a cópia temporária de decodificação é apagada pelo leitor.
    private JwtSettings(string issuer, string audience, byte[] key)
    {
        Issuer = issuer;
        Audience = audience;
        SigningKey = new SymmetricSecurityKey(key);
    }

    internal string Issuer { get; }
    internal string Audience { get; }
    internal SymmetricSecurityKey SigningKey { get; }

    // Configuração parcial/malformada produz estado indisponível, nunca chave padrão nem falha do health.
    public static JwtSettings? Read(IConfiguration configuration)
    {
        var issuer = configuration["Authentication:Jwt:Issuer"];
        var audience = configuration["Authentication:Jwt:Audience"];
        var encodedKey = configuration["Authentication:Jwt:SigningKeyBase64"];
        if (string.IsNullOrWhiteSpace(issuer) || string.IsNullOrWhiteSpace(audience) || string.IsNullOrWhiteSpace(encodedKey))
        {
            return null;
        }
        byte[] key;
        try { key = Convert.FromBase64String(encodedKey); }
        catch (FormatException) { return null; }
        try { return key.Length >= 32 ? new JwtSettings(issuer, audience, key) : null; }
        finally { CryptographicOperations.ZeroMemory(key); }
    }

    // A representação não permite que logging de configuração revele mesmo issuer/audience operacionais.
    public override string ToString() => "JwtSettings { [redacted] }";
}
