namespace Finora.Api.Authentication;

// Indica somente se a composição dispõe de um transporte persistente; não é uma prova de conectividade do broker.
public sealed record SyncRunAcceptanceAvailability(bool IsAvailable);

// Credenciais são mantidas fora do URI e nunca aparecem na representação textual desta configuração.
internal sealed record SyncRunRabbitMqSettings(Uri HostAddress, string Username, string Password, bool UseTls)
{
    public override string ToString() => "SyncRunRabbitMqSettings { Credentials = [redacted] }";

    // Configuração parcial/inválida desabilita a aceitação em vez de usar host, usuário ou transporte padrão.
    internal static SyncRunRabbitMqSettings? Read(IConfiguration configuration)
    {
        var hostAddressValue = configuration["Messaging:RabbitMq:HostAddress"];
        var username = configuration["Messaging:RabbitMq:Username"];
        var password = configuration["Messaging:RabbitMq:Password"];
        var useTlsValue = configuration["Messaging:RabbitMq:UseTls"];

        if (!Uri.TryCreate(hostAddressValue, UriKind.Absolute, out var hostAddress)
            || !string.Equals(hostAddress.Scheme, "rabbitmq", StringComparison.OrdinalIgnoreCase)
            || string.IsNullOrWhiteSpace(hostAddress.Host)
            || !string.IsNullOrEmpty(hostAddress.UserInfo)
            || !string.IsNullOrEmpty(hostAddress.Query)
            || !string.IsNullOrEmpty(hostAddress.Fragment)
            || string.IsNullOrWhiteSpace(username)
            || string.IsNullOrWhiteSpace(password)
            || !bool.TryParse(useTlsValue, out var useTls))
        {
            return null;
        }

        return new SyncRunRabbitMqSettings(hostAddress, username, password, useTls);
    }
}
