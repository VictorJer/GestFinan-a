using System.IO;

namespace Finora.Api.AdministrativeUsers;

/// <summary>Classifica a coleta interativa sem carregar textos secretos nas mensagens de estado.</summary>
internal enum AdministrativeUserTerminalState
{
    EmailCollected,
    PasswordCollected,
    CredentialsCollected,
    Cancelled,
    EndOfInput,
    ConfirmationMismatch,
    TerminalUnavailable
}

/// <summary>Contém somente a entrada necessária ao caso de uso e redige valores em sua representação textual.</summary>
internal sealed class AdministrativeUserTerminalResult
{
    private AdministrativeUserTerminalResult(
        AdministrativeUserTerminalState state,
        string? email = null,
        string? password = null)
    {
        State = state;
        Email = email;
        Password = password;
    }

    public AdministrativeUserTerminalState State { get; }

    public string? Email { get; }

    public string? Password { get; }

    // Nenhum resultado deve refletir e-mail ou credencial em logs acidentais.
    public override string ToString() =>
        $"{nameof(AdministrativeUserTerminalResult)} {{ State = {State}, Email = [redacted], Password = [redacted] }}";

    // Centraliza resultados sem credenciais para cancelamento, EOF, divergência e indisponibilidade.
    public static AdministrativeUserTerminalResult WithoutCredentials(AdministrativeUserTerminalState state) =>
        new(state);

    // Só a confirmação idêntica pode criar o resultado que entrega a senha ao próximo caso de uso.
    public static AdministrativeUserTerminalResult WithCredentials(string email, string password) =>
        new(AdministrativeUserTerminalState.CredentialsCollected, email, password);

    // Separa a coleta do e-mail da senha para permitir a pré-consulta antes de solicitar o segredo.
    public static AdministrativeUserTerminalResult WithEmail(string email) =>
        new(AdministrativeUserTerminalState.EmailCollected, email);

    // Entrega a senha somente após confirmação igual, mantendo o e-mail fora desta etapa da interação.
    public static AdministrativeUserTerminalResult WithPassword(string password) =>
        new(AdministrativeUserTerminalState.PasswordCollected, password: password);
}

/// <summary>Abstrai as chamadas estáticas de Console para permitir testes sem TTY real.</summary>
internal interface IAdministrativeUserConsole
{
    bool IsInputRedirected { get; }

    bool IsOutputRedirected { get; }

    bool TreatControlCAsInput { get; set; }

    ConsoleKeyInfo? ReadKey(bool intercept);

    void WriteLine(string value);
}

/// <summary>Adapta o Console real sem usar ReadLine ou permitir eco das teclas digitadas.</summary>
internal sealed class SystemAdministrativeUserConsole : IAdministrativeUserConsole
{
    public bool IsInputRedirected => Console.IsInputRedirected;

    public bool IsOutputRedirected => Console.IsOutputRedirected;

    public bool TreatControlCAsInput
    {
        get => Console.TreatControlCAsInput;
        set => Console.TreatControlCAsInput = value;
    }

    // intercept=true impede que e-mail, senha ou confirmação sejam desenhados no console.
    public ConsoleKeyInfo? ReadKey(bool intercept) => Console.ReadKey(intercept);

    // Os únicos textos escritos pelo adaptador são prompts fixos ou separadores sem entrada do operador.
    public void WriteLine(string value) => Console.WriteLine(value);
}

/// <summary>Coleta e-mail e senha duas vezes sem eco, limpando todos os buffers mutáveis ao terminar.</summary>
internal sealed class AdministrativeUserTerminal
{
    internal const string EmailPrompt = "User admin: e-mail:";
    internal const string PasswordPrompt = "User admin: senha:";
    internal const string ConfirmationPrompt = "User admin: confirme a senha:";

    private readonly IAdministrativeUserConsole _console;
    private readonly Func<SensitiveInputBuffer> _bufferFactory;

    // O factory é injetável para os testes observarem que buffers de caminhos concluídos e interrompidos são limpos.
    public AdministrativeUserTerminal(
        IAdministrativeUserConsole console,
        Func<SensitiveInputBuffer>? bufferFactory = null)
    {
        _console = console ?? throw new ArgumentNullException(nameof(console));
        _bufferFactory = bufferFactory ?? (() => new SensitiveInputBuffer());
    }

    // Coleta as duas etapas em sequência quando o chamador não precisa intercalar uma consulta prévia.
    public AdministrativeUserTerminalResult ReadCredentials()
    {
        var email = ReadEmail();
        if (email.State != AdministrativeUserTerminalState.EmailCollected)
        {
            return email;
        }

        var password = ReadPasswordConfirmation();
        return password.State == AdministrativeUserTerminalState.PasswordCollected
            ? AdministrativeUserTerminalResult.WithCredentials(email.Email!, password.Password!)
            : password;
    }

    // Lê o e-mail sem eco e libera seu buffer antes de o chamador executar a pré-consulta.
    public AdministrativeUserTerminalResult ReadEmail()
    {
        if (_console.IsInputRedirected || _console.IsOutputRedirected)
        {
            return AdministrativeUserTerminalResult.WithoutCredentials(
                AdministrativeUserTerminalState.TerminalUnavailable);
        }

        using var email = _bufferFactory();
        return ReadProtectedInput(() =>
        {
            _console.WriteLine(EmailPrompt);
            var state = ReadValue(email);
            return state == InputState.Completed
                ? AdministrativeUserTerminalResult.WithEmail(email.CreateString())
                : AdministrativeUserTerminalResult.WithoutCredentials(ToTerminalState(state));
        });
    }

    // Pede senha e confirmação somente depois da pré-consulta, limpando ambos os buffers em qualquer saída.
    public AdministrativeUserTerminalResult ReadPasswordConfirmation()
    {
        if (_console.IsInputRedirected || _console.IsOutputRedirected)
        {
            return AdministrativeUserTerminalResult.WithoutCredentials(
                AdministrativeUserTerminalState.TerminalUnavailable);
        }

        using var password = _bufferFactory();
        using var confirmation = _bufferFactory();
        return ReadProtectedInput(() =>
        {
            _console.WriteLine(PasswordPrompt);
            var passwordState = ReadValue(password);
            if (passwordState != InputState.Completed)
            {
                return AdministrativeUserTerminalResult.WithoutCredentials(ToTerminalState(passwordState));
            }

            _console.WriteLine(ConfirmationPrompt);
            var confirmationState = ReadValue(confirmation);
            if (confirmationState != InputState.Completed)
            {
                return AdministrativeUserTerminalResult.WithoutCredentials(ToTerminalState(confirmationState));
            }

            return password.ContentEquals(confirmation)
                ? AdministrativeUserTerminalResult.WithPassword(password.CreateString())
                : AdministrativeUserTerminalResult.WithoutCredentials(
                    AdministrativeUserTerminalState.ConfirmationMismatch);
        });
    }

    // Mantém o modo de Ctrl+C restrito à leitura protegida e falha fechada se o console não puder restaurá-lo.
    private AdministrativeUserTerminalResult ReadProtectedInput(
        Func<AdministrativeUserTerminalResult> readInput)
    {
        var originalTreatControlCAsInput = false;
        var controlCSettingChanged = false;
        var result = AdministrativeUserTerminalResult.WithoutCredentials(
            AdministrativeUserTerminalState.TerminalUnavailable);

        try
        {
            originalTreatControlCAsInput = _console.TreatControlCAsInput;
            _console.TreatControlCAsInput = true;
            controlCSettingChanged = true;
            result = readInput();
        }
        catch (Exception exception) when (IsTerminalUnavailable(exception))
        {
            result = AdministrativeUserTerminalResult.WithoutCredentials(
                AdministrativeUserTerminalState.TerminalUnavailable);
        }
        finally
        {
            // Falha ao restaurar o modo global invalida a coleta para não deixar Ctrl+C com semântica alterada.
            if (controlCSettingChanged)
            {
                try
                {
                    _console.TreatControlCAsInput = originalTreatControlCAsInput;
                }
                catch (Exception exception) when (IsTerminalUnavailable(exception))
                {
                    result = AdministrativeUserTerminalResult.WithoutCredentials(
                        AdministrativeUserTerminalState.TerminalUnavailable);
                }
            }
        }

        return result;
    }

    // Lê uma linha por teclas; Enter e Backspace são tratados sem transformar caracteres válidos da senha.
    private InputState ReadValue(SensitiveInputBuffer buffer)
    {
        while (true)
        {
            var key = _console.ReadKey(intercept: true);
            if (key is null)
            {
                return InputState.EndOfInput;
            }

            if (key.Value.Key == ConsoleKey.C
                && (key.Value.Modifiers & ConsoleModifiers.Control) != 0)
            {
                return InputState.Cancelled;
            }

            if (key.Value.Key == ConsoleKey.Enter)
            {
                _console.WriteLine(string.Empty);
                return InputState.Completed;
            }

            if (key.Value.Key == ConsoleKey.Backspace)
            {
                buffer.RemoveLast();
                continue;
            }

            if (key.Value.KeyChar != '\0')
            {
                buffer.Append(key.Value.KeyChar);
            }
        }
    }

    // Exceções de console são convertidas em falha fechada, sem fallback para entrada textual com eco.
    private static bool IsTerminalUnavailable(Exception exception) =>
        exception is IOException or InvalidOperationException or PlatformNotSupportedException;

    // EOF e Ctrl+C mantêm estados separados para que a camada de comando aplique seus códigos estáveis.
    private static AdministrativeUserTerminalState ToTerminalState(InputState state) => state switch
    {
        InputState.Cancelled => AdministrativeUserTerminalState.Cancelled,
        InputState.EndOfInput => AdministrativeUserTerminalState.EndOfInput,
        _ => AdministrativeUserTerminalState.TerminalUnavailable
    };

    // Os estados intermediários não carregam texto digitado nem regras de Identity.
    private enum InputState
    {
        Completed,
        Cancelled,
        EndOfInput
    }
}

/// <summary>Buffer UTF-16 expansível que apaga conteúdo antigo ao crescer e zera toda capacidade ao descartar.</summary>
internal sealed class SensitiveInputBuffer : IDisposable
{
    private char[] _characters = new char[32];
    private int _length;

    public int Length => _length;

    internal bool IsCleared { get; private set; } = true;

    // Acrescenta a tecla exata, expandindo com limpeza do array anterior para não reter cópias da senha.
    public void Append(char value)
    {
        EnsureCapacity(_length + 1);
        _characters[_length++] = value;
        IsCleared = false;
    }

    // Backspace apaga a posição removida imediatamente e nunca transforma ou trunca os demais caracteres.
    public void RemoveLast()
    {
        if (_length == 0)
        {
            return;
        }

        _characters[--_length] = '\0';
        IsCleared = _length == 0;
    }

    // Compara senha e confirmação diretamente nos buffers, sem criar outra string temporária.
    public bool ContentEquals(SensitiveInputBuffer other)
    {
        ArgumentNullException.ThrowIfNull(other);
        if (_length != other._length)
        {
            return false;
        }

        var difference = 0;
        for (var index = 0; index < _length; index++)
        {
            difference |= _characters[index] ^ other._characters[index];
        }

        return difference == 0;
    }

    // Cria somente a string que o caso de uso precisa antes de limpar o buffer original.
    public string CreateString() => new(_characters, 0, _length);

    // Zera cada posição alocada e marca o buffer para verificação determinística nos testes.
    public void Dispose()
    {
        Array.Clear(_characters);
        _length = 0;
        IsCleared = true;
    }

    // O buffer nunca deve ser representado como texto em logs ou falhas de teste.
    public override string ToString() => "[redacted]";

    // Ao crescer, copia somente caracteres ativos e apaga imediatamente a alocação que será abandonada.
    private void EnsureCapacity(int requiredCapacity)
    {
        if (requiredCapacity <= _characters.Length)
        {
            return;
        }

        var expanded = new char[Math.Max(requiredCapacity, checked(_characters.Length * 2))];
        Array.Copy(_characters, expanded, _length);
        Array.Clear(_characters);
        _characters = expanded;
    }
}
