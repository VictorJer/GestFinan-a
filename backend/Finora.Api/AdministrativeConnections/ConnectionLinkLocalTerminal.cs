namespace Finora.Api.AdministrativeConnections;

/// <summary>Abstrai o terminal para testar a barreira de destino sem leitura real do processo.</summary>
internal interface IConnectionLinkLocalConsole
{
    bool IsInputRedirected { get; }

    bool IsOutputRedirected { get; }

    bool TreatControlCAsInput { get; set; }

    string? ReadLine();

    ConsoleKeyInfo? ReadKey(bool intercept);

    void WriteLine(string value);
}

/// <summary>Adapta o Console e não publica valores informados pelo operador.</summary>
internal sealed class SystemConnectionLinkLocalConsole : IConnectionLinkLocalConsole
{
    public bool IsInputRedirected => Console.IsInputRedirected;

    public bool IsOutputRedirected => Console.IsOutputRedirected;

    public bool TreatControlCAsInput
    {
        get => Console.TreatControlCAsInput;
        set => Console.TreatControlCAsInput = value;
    }

    // O nome do database é lido apenas para comparação em memória e nunca é ecoado pela aplicação.
    public string? ReadLine() => Console.ReadLine();

    // A confirmação final não ecoa teclas e permite converter Ctrl+C em estado controlado.
    public ConsoleKeyInfo? ReadKey(bool intercept) => Console.ReadKey(intercept);

    // Somente prompts constantes são apresentados; configuração e conteúdo digitado não são reproduzidos.
    public void WriteLine(string value) => Console.WriteLine(value);
}

// Distingue a confirmação, recusa, EOF e Ctrl+C sem transportar a entrada para o fluxo administrativo.
internal enum ConnectionLinkLocalConfirmationState
{
    Confirmed,
    Refused,
    EndOfInput,
    Cancelled,
    TerminalUnavailable
}

/// <summary>Solicita a confirmação final sem apresentar identificadores do titular ou da origem externa.</summary>
internal sealed class ConnectionLinkLocalTerminal(IConnectionLinkLocalConsole console)
{
    private const string ConfirmationPrompt =
        "Connection link local: verificações concluídas. Confirma o vínculo ao titular local? Digite SIM:";

    // A entrada só é lida depois da pré-consulta e o modo global de Ctrl+C sempre é restaurado.
    internal ConnectionLinkLocalConfirmationState ConfirmLink()
    {
        if (console.IsInputRedirected || console.IsOutputRedirected)
        {
            return ConnectionLinkLocalConfirmationState.TerminalUnavailable;
        }

        console.WriteLine(ConfirmationPrompt);
        var originalTreatControlCAsInput = false;
        var controlCSettingChanged = false;
        var result = ConnectionLinkLocalConfirmationState.TerminalUnavailable;
        try
        {
            originalTreatControlCAsInput = console.TreatControlCAsInput;
            console.TreatControlCAsInput = true;
            controlCSettingChanged = true;
            result = ReadConfirmation();
        }
        catch (Exception exception) when (IsTerminalUnavailable(exception))
        {
            result = ConnectionLinkLocalConfirmationState.TerminalUnavailable;
        }
        finally
        {
            // Uma falha ao restaurar o console invalida a confirmação e evita deixar o processo em modo alterado.
            if (controlCSettingChanged)
            {
                try
                {
                    console.TreatControlCAsInput = originalTreatControlCAsInput;
                }
                catch (Exception exception) when (IsTerminalUnavailable(exception))
                {
                    result = ConnectionLinkLocalConfirmationState.TerminalUnavailable;
                }
            }
        }

        return result;
    }

    // Lê SIM sem eco, reconhecendo Enter, Backspace, EOF e Ctrl+C como estados distintos.
    private ConnectionLinkLocalConfirmationState ReadConfirmation()
    {
        var input = new System.Text.StringBuilder(capacity: 3);
        try
        {
            while (true)
            {
                var key = console.ReadKey(intercept: true);
                if (key is null)
                {
                    return ConnectionLinkLocalConfirmationState.EndOfInput;
                }

                if (key.Value.Key == ConsoleKey.C
                    && (key.Value.Modifiers & ConsoleModifiers.Control) != 0)
                {
                    return ConnectionLinkLocalConfirmationState.Cancelled;
                }

                if (key.Value.Key == ConsoleKey.Enter)
                {
                    console.WriteLine(string.Empty);
                    return string.Equals(input.ToString(), "SIM", StringComparison.Ordinal)
                        ? ConnectionLinkLocalConfirmationState.Confirmed
                        : ConnectionLinkLocalConfirmationState.Refused;
                }

                if (key.Value.Key == ConsoleKey.Backspace)
                {
                    if (input.Length > 0)
                    {
                        input.Length--;
                    }

                    continue;
                }

                if (key.Value.KeyChar != '\0')
                {
                    input.Append(key.Value.KeyChar);
                }
            }
        }
        finally
        {
            // A confirmação não é segredo, mas limpar o buffer evita reter entrada terminal desnecessariamente.
            input.Clear();
        }
    }

    // Falhas do dispositivo são convertidas em terminal indisponível sem propagar exceções ou valores digitados.
    private static bool IsTerminalUnavailable(Exception exception) =>
        exception is IOException or InvalidOperationException or PlatformNotSupportedException;
}
