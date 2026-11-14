using Microsoft.VisualStudio.TestTools.UnitTesting;

// Hosts e leituras dos projetos são independentes e podem executar em paralelo.
[assembly: Parallelize(Scope = ExecutionScope.MethodLevel)]
