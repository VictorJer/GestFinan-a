# Tasks

## 1. Reprodução isolada antes da correção

- [x] 1.1 Preparar ponto interno de construção para teste de `CreateHostBuilder` com `DisableDefaults = true` definido antes da criação do builder, sem mudar o caminho operacional; verificar em teste que, em Development e Production, as fontes não incluem User Secrets, JSON/appsettings ou ambiente e que a configuração em memória é exclusivamente sintética.
- [x] 1.2 Adicionar regressão inicial que chama `CreateHostBuilder(Development)` isolado e `BuildHost` com string sintética sem senha, forçando `ValidateOnBuild` e `ValidateScopes` via provider factory; verificar falha na versão pré-correção, registrar apenas tipo/etapa e nomes de dependências ausentes (sem valores), sem abrir conexão nem executar `user-create`. Repetir em Production com as mesmas validações; se a hipótese não for confirmada, parar e apresentar evidência para revisão do plano.

## 2. Composição mínima e regressões

- [x] 2.1 Se confirmada a causa, restringir `AddAdministrativeUserHandlers` aos três handlers de pré-consulta, criação e reset; verificar que o teste de construção agora passa em Development e Production com ambas as validações ativas, sem introduzir dependências externas nem alterar configuração runtime.
- [x] 2.2 Cobrir registro/resolução de `ISender` e dos três `IRequestHandler<,>` em escopo, ausência de handlers não administrativos e serviços HTTP/Pluggy/RabbitMQ, ausência de conexão aberta e não execução de terminal; verificar o resultado dos testes focais em configuração isolada nos dois ambientes.
- [x] 2.3 Testar falha sintética de composição em `RunAsync` usando builder isolado e console falso; verificar código 1, stdout vazio, stderr exatamente `User admin: failure=operational`, sem sentinelas de exceção/credencial, e preservar os testes existentes de argumentos e configuração inválida sem acesso a segredos reais.

## 3. Validação integrada do escopo

- [x] 3.1 Executar build da solução e os testes focais administrativos que não carreguem providers reais (incluindo os casos anteriores de CLI/Identity); conferir resultados e registrar limites sem executar testes PostgreSQL, prompts reais, migrations ou comandos administrativos.
- [x] 3.2 Executar `openspec validate corrigir-composicao-cli-administrativo --strict` e `git diff --check`; revisar o diff limitado a esta mudança, código/testes da composição e ausência de configurações/segredos/alterações não relacionadas. Apresentar evidências e pendências para revisão/aceite, sem presumir que tarefas planejadas foram realizadas.
