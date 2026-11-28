# Tasks

## 1. Correção restrita do contrato de teste e evidência

- [x] 1.1 Conferir na branch da correção, baseada no merge `460a29d`, o contrato em `SimulationsController`, `AuthenticationAvailabilityTests`, PRD RF12, TDD §§11/13 e specs `api-exploration`/`investment-simulation`; confirmar `POST /api/simulations/calculate` protegido por Bearer. Se houver divergência, parar e apresentar o conflito antes de editar; verificar pelo diff que nenhum arquivo de código foi alterado nesta conferência.
- [x] 1.2 Atualizar somente `ExplorationTests.DevelopmentPublishesHealthContract`: incluir `/api/simulations/calculate` na lista exata e `("/api/simulations/calculate", "post")` em `protectedOperations`, com comentário de intenção em português; preservar `publicOperations`, comparações exatas, Bearer e health. Verificar com testes focais de `ExplorationTests`, `ExplorationIsolationTests` e `AuthenticationAvailabilityTests`, incluindo `DevelopmentServesSwaggerAndLocalAssets`, `DocumentationIsUnavailableOutsideDevelopment` e a operação protegida do simulador; registrar total, falhas e skips reais.
- [x] 1.3 Reproduzir a seleção do job `backend-no-postgres` de `.github/workflows/ci.yml` com `dotnet restore backend/Finora.slnx --locked-mode`, `dotnet build backend/Finora.slnx --no-restore` e `dotnet test --solution backend/Finora.slnx --no-build --no-restore --filter 'FullyQualifiedName!~PostgreSql&FullyQualifiedName!~MqP01CompatibilityTests&FullyQualifiedName!~OwnershipProbeTests&FullyQualifiedName!~ProblemDetailsContractTests' --output normal --minimum-expected-tests 1 --zero-tests-policy strict --report-trx --results-directory <diretório-temporário-local>`; verificar seleção não vazia, zero falhas/zero skips e TRX conforme a guarda `.github/scripts/Assert-TrxNoSkippedTests.ps1`, sem acessar banco/broker reais.
- [x] 1.4 Atualizar `docs/BACKLOG.md` com revisão factual própria e pendente: PR #53 integrado no merge `460a29d`, check `backend-no-postgres` do PR e CI do merge falhos em 416/417, outros jobs bem-sucedidos, além das evidências locais obtidas nesta correção e seus limites; preservar histórico e gates independentes. Verificar os links e distinguir explicitamente testes locais de checks remotos, sem declarar aprovação documental, integração da correção ou publicação.

## 2. Verificação integrada do planejamento e do diff

- [x] 2.1 Executar `openspec validate corrigir-contrato-openapi-simulador --strict` e `git diff --check`; conferir que o diff autorizado limita o código a `ExplorationTests.cs` e o registro documental a `docs/BACKLOG.md`, além dos artefatos OpenSpec, sem reduzir as asserções nem mudar API, specs principais, frontend ou CI. Registrar resultados verificáveis e pendências reais em `tasks.md`.

## Evidências e pendências

- `dotnet restore backend/Finora.slnx --locked-mode`: passou; todos os projetos estavam atualizados.
- `dotnet build backend/Finora.slnx --no-restore`: passou com 0 avisos e 0 erros.
- Testes focais de `ExplorationTests`, `ExplorationIsolationTests` e `AuthenticationAvailabilityTests`: 22 aprovados, 0 falhas e 0 ignorados; a guarda TRX passou.
- Reprodução local da seleção `backend-no-postgres`: 417 aprovados, 0 falhas e 0 ignorados; seleção não vazia e guarda TRX passou. Não foram acessados PostgreSQL, RabbitMQ, provedor ou dados financeiros reais.
- `openspec validate corrigir-contrato-openapi-simulador --strict`: válido; `skip_specs: true` foi aceito porque a mudança não altera comportamento/requisitos de capability.
- `git diff --check`: passou, com avisos de conversão LF→CRLF do Git para `ExplorationTests.cs` e `docs/BACKLOG.md`, sem erro de whitespace.
- Inspeção de escopo: o código se limita a `ExplorationTests.cs`; o registro factual está em `docs/BACKLOG.md`; somente `tasks.md` foi atualizado entre os artefatos OpenSpec. `proposal.md`, `design.md` e `.openspec.yaml` permaneceram inalterados; não há alterações staged. API, specs principais, frontend e CI não foram modificados.
- Os checks remotos históricos do PR #53 e do merge `460a29d` permanecem 416/417 em `backend-no-postgres`, com `backend-postgres` e `frontend` bem-sucedidos; são independentes das execuções locais registradas acima.
- Na conclusão das tarefas técnicas, o aceite da implementação e a aprovação documental eram pendências separadas; ambos foram confirmados posteriormente pelo titular no harness em 28/11/2026. A mudança foi então arquivada localmente, sem delta para sincronizar (`skip_specs: true`). Commits e PR próprio seguem como encaminhamento autorizado, mas ainda não executado neste registro; merge e publicação não foram autorizados.

## Workflow follow-up

- Apresentar código, evidências e revisão documental para aceite humano separado antes de solicitar qualquer commit, push ou PR.
- Após autorização própria e checks aplicáveis de um PR futuro, avaliar sync/arquivo conforme o fluxo do projeto; `skip_specs: true` não produz delta spec para sincronizar.
