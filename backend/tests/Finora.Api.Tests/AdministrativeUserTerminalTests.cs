using Finora.Api.AdministrativeUsers;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Finora.Api.Tests;

// Exercita a coleta protegida com um console roteirizado, sem depender de TTY real.
[TestClass]
public sealed class AdministrativeUserTerminalTests
{
    // Confirma que caracteres são preservados literalmente, as teclas são interceptadas e nenhum segredo é escrito.
    [TestMethod]
    public void ReadCredentialsPreservesExactPasswordWithoutEcho()
    {
        const string email = " Admin@Example.com ";
        const string password = " Pa$$!\t?é😀 ";
        var console = new ScriptedAdministrativeUserConsole(
            TypeLine(email)
                .Concat(TypeLine(password))
                .Concat(TypeLine(password)));
        var buffers = new List<SensitiveInputBuffer>();

        var result = CreateTerminal(console, buffers).ReadCredentials();

        Assert.AreEqual(AdministrativeUserTerminalState.CredentialsCollected, result.State);
        Assert.IsTrue(string.Equals(email, result.Email, StringComparison.Ordinal));
        Assert.IsTrue(string.Equals(password, result.Password, StringComparison.Ordinal));
        Assert.IsTrue(console.ReadInterceptArguments.All(intercept => intercept));
        Assert.IsFalse(console.WrittenLines.Any(line => line.Contains(email, StringComparison.Ordinal)));
        Assert.IsFalse(console.WrittenLines.Any(line => line.Contains(password, StringComparison.Ordinal)));
        Assert.AreEqual(false, console.TreatControlCAsInput);
        AssertBuffersCleared(buffers);
    }

    // Backspace remove somente o último caractere, inclusive quando usado sobre um buffer vazio.
    [TestMethod]
    public void ReadCredentialsAppliesBackspaceWithoutChangingRemainingCharacters()
    {
        var console = new ScriptedAdministrativeUserConsole(
            TypeLine("admin@example.com")
                .Append(BackspaceKey())
                .Concat(TypeCharacters("SecrX"))
                .Append(BackspaceKey())
                .Concat(TypeLine("et!"))
                .Concat(TypeLine("Secret!")));

        var result = new AdministrativeUserTerminal(console).ReadCredentials();

        Assert.AreEqual(AdministrativeUserTerminalState.CredentialsCollected, result.State);
        Assert.IsTrue(string.Equals("Secret!", result.Password, StringComparison.Ordinal));
    }

    // EOF em qualquer prompt retorna estado específico e descarta todos os buffers sem entregar credenciais.
    [TestMethod]
    [DataRow(0)]
    [DataRow(1)]
    [DataRow(2)]
    public void EndOfInputAtEveryPromptDoesNotReturnCredentials(int completedPrompts)
    {
        var keys = PrefixForPrompt(completedPrompts);
        var console = new ScriptedAdministrativeUserConsole(keys);
        var buffers = new List<SensitiveInputBuffer>();

        var result = CreateTerminal(console, buffers).ReadCredentials();

        Assert.AreEqual(AdministrativeUserTerminalState.EndOfInput, result.State);
        Assert.IsNull(result.Email);
        Assert.IsNull(result.Password);
        AssertBuffersCleared(buffers);
        Assert.AreEqual(false, console.TreatControlCAsInput);
    }

    // Ctrl+C em qualquer prompt cancela a coleta e restaura a semântica original do console.
    [TestMethod]
    [DataRow(0)]
    [DataRow(1)]
    [DataRow(2)]
    public void ControlCAtEveryPromptCancelsAndClearsBuffers(int completedPrompts)
    {
        var keys = PrefixForPrompt(completedPrompts).Append(ControlCKey());
        var console = new ScriptedAdministrativeUserConsole(keys);
        var buffers = new List<SensitiveInputBuffer>();

        var result = CreateTerminal(console, buffers).ReadCredentials();

        Assert.AreEqual(AdministrativeUserTerminalState.Cancelled, result.State);
        Assert.IsNull(result.Email);
        Assert.IsNull(result.Password);
        AssertBuffersCleared(buffers);
        Assert.AreEqual(false, console.TreatControlCAsInput);
    }

    // Divergência entre senha e confirmação não retorna nenhuma credencial e ainda limpa os buffers temporários.
    [TestMethod]
    public void ConfirmationMismatchDoesNotReturnCredentialsOrExposeInput()
    {
        const string passwordSentinel = "password-private-sentinel";
        var console = new ScriptedAdministrativeUserConsole(
            TypeLine("admin-private-sentinel@example.com")
                .Concat(TypeLine(passwordSentinel))
                .Concat(TypeLine("different-private-sentinel")));
        var buffers = new List<SensitiveInputBuffer>();

        var result = CreateTerminal(console, buffers).ReadCredentials();

        Assert.AreEqual(AdministrativeUserTerminalState.ConfirmationMismatch, result.State);
        Assert.IsNull(result.Email);
        Assert.IsNull(result.Password);
        Assert.IsFalse(result.ToString().Contains(passwordSentinel, StringComparison.Ordinal));
        Assert.IsFalse(string.Join('\n', console.WrittenLines).Contains("private-sentinel", StringComparison.Ordinal));
        AssertBuffersCleared(buffers);
    }

    // Entrada ou saída redirecionada é recusada antes de ler, escrever ou alocar buffers sensíveis.
    [TestMethod]
    [DataRow(true, false)]
    [DataRow(false, true)]
    [DataRow(true, true)]
    public void RedirectedTerminalIsRejectedBeforeInteraction(bool inputRedirected, bool outputRedirected)
    {
        var console = new ScriptedAdministrativeUserConsole([])
        {
            IsInputRedirected = inputRedirected,
            IsOutputRedirected = outputRedirected
        };
        var buffers = new List<SensitiveInputBuffer>();

        var result = CreateTerminal(console, buffers).ReadCredentials();

        Assert.AreEqual(AdministrativeUserTerminalState.TerminalUnavailable, result.State);
        Assert.AreEqual(0, console.ReadInterceptArguments.Count);
        Assert.AreEqual(0, console.WrittenLines.Count);
        Assert.AreEqual(0, buffers.Count);
    }

    // Falha de leitura do terminal é tratada como indisponibilidade e nunca usa fallback com eco.
    [TestMethod]
    public void TerminalReadFailureReturnsUnavailableAndClearsBuffers()
    {
        var console = new ScriptedAdministrativeUserConsole(TypeLine("partial-private-sentinel"))
        {
            ReadFailure = new IOException("terminal-private-sentinel")
        };
        var buffers = new List<SensitiveInputBuffer>();

        var result = CreateTerminal(console, buffers).ReadCredentials();

        Assert.AreEqual(AdministrativeUserTerminalState.TerminalUnavailable, result.State);
        Assert.IsNull(result.Email);
        Assert.IsNull(result.Password);
        Assert.IsFalse(string.Join('\n', console.WrittenLines).Contains("private-sentinel", StringComparison.Ordinal));
        Assert.AreEqual(false, console.TreatControlCAsInput);
        AssertBuffersCleared(buffers);
    }

    // A indisponibilidade do console ao alternar Ctrl+C também falha fechada e limpa as alocações antecipadas.
    [TestMethod]
    public void FailureToConfigureControlCModeReturnsUnavailableAndClearsBuffers()
    {
        var console = new ScriptedAdministrativeUserConsole([]) { FailWhenEnablingControlCInput = true };
        var buffers = new List<SensitiveInputBuffer>();

        var result = CreateTerminal(console, buffers).ReadCredentials();

        Assert.AreEqual(AdministrativeUserTerminalState.TerminalUnavailable, result.State);
        Assert.AreEqual(0, console.ReadInterceptArguments.Count);
        AssertBuffersCleared(buffers);
    }

    // Cria o adaptador com buffers observáveis para provar que todos foram zerados ao sair de cada caminho.
    private static AdministrativeUserTerminal CreateTerminal(
        ScriptedAdministrativeUserConsole console,
        ICollection<SensitiveInputBuffer> buffers) =>
        new(console, () =>
        {
            var buffer = new SensitiveInputBuffer();
            buffers.Add(buffer);
            return buffer;
        });

    // Gera a entrada completa dos prompts anteriores ao ponto em que o teste injeta EOF ou Ctrl+C.
    private static IEnumerable<ConsoleKeyInfo?> PrefixForPrompt(int completedPrompts)
    {
        var keys = new List<ConsoleKeyInfo?>();
        if (completedPrompts >= 1)
        {
            keys.AddRange(TypeLine("admin@example.com"));
        }

        if (completedPrompts >= 2)
        {
            keys.AddRange(TypeLine("private-password"));
        }

        return keys;
    }

    // Converte cada UTF-16 code unit em uma tecla distinta, como ReadKey entrega ao adaptador.
    private static IEnumerable<ConsoleKeyInfo?> TypeLine(string value) =>
        TypeCharacters(value).Append(EnterKey());

    // Mantém as teclas sem Enter para inserir edição dentro do mesmo campo.
    private static IEnumerable<ConsoleKeyInfo?> TypeCharacters(string value) =>
        value.Select(character => (ConsoleKeyInfo?)new ConsoleKeyInfo(
            character,
            ConsoleKey.NoName,
            shift: false,
            alt: false,
            control: false));

    // Representa Enter como uma tecla real para exercitar o encerramento do prompt.
    private static ConsoleKeyInfo? EnterKey() =>
        new ConsoleKeyInfo('\r', ConsoleKey.Enter, shift: false, alt: false, control: false);

    // Representa Backspace sem depender de mapeamento de teclado do sistema operacional.
    private static ConsoleKeyInfo? BackspaceKey() =>
        new ConsoleKeyInfo('\b', ConsoleKey.Backspace, shift: false, alt: false, control: false);

    // Usa a combinação Ctrl+C que o adaptador reconhece quando Ctrl+C é tratado como entrada.
    private static ConsoleKeyInfo? ControlCKey() =>
        new ConsoleKeyInfo('\u0003', ConsoleKey.C, shift: false, alt: false, control: true);

    // Verifica a limpeza sem exibir o conteúdo de nenhum buffer em mensagens de falha.
    private static void AssertBuffersCleared(IEnumerable<SensitiveInputBuffer> buffers)
    {
        var materialized = buffers.ToArray();
        Assert.IsTrue(materialized.Length > 0);
        Assert.IsTrue(materialized.All(buffer => buffer.IsCleared && buffer.Length == 0));
    }

    // Simula Console com fluxo de teclas e falhas injetáveis, registrando apenas metadados de interação.
    private sealed class ScriptedAdministrativeUserConsole : IAdministrativeUserConsole
    {
        private readonly Queue<ConsoleKeyInfo?> _keys;
        private bool _treatControlCAsInput;

        public ScriptedAdministrativeUserConsole(IEnumerable<ConsoleKeyInfo?> keys) =>
            _keys = new Queue<ConsoleKeyInfo?>(keys);

        public bool IsInputRedirected { get; init; }

        public bool IsOutputRedirected { get; init; }

        public bool FailWhenEnablingControlCInput { get; init; }

        public Exception? ReadFailure { get; init; }

        public List<bool> ReadInterceptArguments { get; } = [];

        public List<string> WrittenLines { get; } = [];

        public bool TreatControlCAsInput
        {
            get => _treatControlCAsInput;
            set
            {
                if (value && FailWhenEnablingControlCInput)
                {
                    throw new IOException("console-private-sentinel");
                }

                _treatControlCAsInput = value;
            }
        }

        // Registra o uso de intercept=true e devolve EOF quando o roteiro termina.
        public ConsoleKeyInfo? ReadKey(bool intercept)
        {
            ReadInterceptArguments.Add(intercept);
            if (ReadFailure is not null)
            {
                throw ReadFailure;
            }

            return _keys.Count == 0 ? null : _keys.Dequeue();
        }

        // Mantém a saída observável para confirmar que nenhum texto digitado é ecoado.
        public void WriteLine(string value) => WrittenLines.Add(value);
    }
}
