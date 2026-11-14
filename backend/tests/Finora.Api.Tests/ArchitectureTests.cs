using System.Xml.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

// Os testes inspecionam referências reais, sem exigir tipos artificiais nas bibliotecas.
[TestClass]
public sealed class ArchitectureTests
{
    // Cada fronteira aceita apenas as dependências diretas aprovadas em DT-38.
    [TestMethod]
    [DataRow("Finora.Domain", new string[0])]
    [DataRow("Finora.Application", new[] { "Finora.Domain" })]
    [DataRow("Finora.Infrastructure", new[] { "Finora.Application", "Finora.Domain" })]
    [DataRow("Finora.Api", new[] { "Finora.Application", "Finora.Infrastructure" })]
    public void ProductionProjectHasOnlyApprovedReferences(string project, string[] expected)
    {
        var backend = FindBackendDirectory();
        var document = XDocument.Load(Path.Combine(backend, project, $"{project}.csproj"));
        var references = document.Descendants("ProjectReference")
            .Select(reference => Path.GetFileNameWithoutExtension((string?)reference.Attribute("Include")))
            .ToArray();

        CollectionAssert.AreEquivalent(expected, references);
    }

    // A solução é o marco de localização, independente do diretório de execução e da máquina.
    private static string FindBackendDirectory()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory);
             directory is not null;
             directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Finora.slnx")))
            {
                return directory.FullName;
            }
        }

        throw new DirectoryNotFoundException("Solução backend não encontrada a partir da saída dos testes.");
    }
}
