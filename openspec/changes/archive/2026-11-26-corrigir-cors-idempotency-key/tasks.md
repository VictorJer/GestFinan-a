# Tasks

## 1. Gate e contrato canônico

- [x] 1.1 Apresentar proposta, delta `api-access-boundary`, design e tarefas para aprovação explícita antes de alterar código; conferir decisão do titular registrada e escopo FNR-021/FNR-011 sem aceite implícito do explorador.
- [x] 1.2 Preparar revisão delimitada de `docs/TDD.md` e `docs/BACKLOG.md` para registrar os dois POSTs, a exceção de cabeçalho por rota/método e o bloqueio observado; apresentar o diff para revisão antes do código dependente e conferir que aprovações e ressalvas 0.36/0.85 não foram transferidas.

## 2. Permissão CORS restrita

- [x] 2.1 Implementar política base e política específica com seleção exata de caminho/método no ponto único de CORS da API, mantendo origens configuradas, fallback fechado e ordem antes da autenticação; verificar em host HTTP que OPTIONS `POST` das duas rotas com `Authorization, Idempotency-Key` concede somente os itens contratados sem Bearer.
- [x] 2.2 Estender `CorsContractTests` com negativas de origem não autorizada, allowlist vazia, rota alheia ou similar, método diferente, cabeçalho adicional desconhecido e regressão de métodos/cabeçalhos anteriores; verificar os cabeçalhos concedidos (não apenas 204), ausência de credenciais e execução dos testes focais.
- [x] 2.3 Atualizar a descrição operacional de CORS no TDD canônico conforme o comportamento efetivamente entregue, mantendo revisão pendente até aceite, e conferir sua correspondência com os cenários da delta e os testes focais.

## 3. POST efetivo e repetição idempotente

- [x] 3.1 Testar `POST` real no host com `Origin` permitido nas duas rotas: sem Bearer deve retornar 401 com origem CORS e sem executar ação; com sessão válida e fixture sintética/infra descartável, verificar permissão de origem nas respostas 200/202 e em erro tratado sem alterar o status ou contrato da API.
- [x] 3.2 Exercitar replay HTTP com mesma chave, titular e modo após resposta incerta e verificar referência persistida única sem nova execução; conferir também os testes existentes de `SyncIntentCoordinator` para chave reaproveitada e intenção nova distinta, sem criar atalho na UI.
- [x] 3.3 Registrar em `docs/AMBIENTE.md` os testes executados e os resultados sintéticos de transporte e idempotência, distinguindo-os de navegador, Pluggy e serviços hospedados; verificar que nenhuma evidência fictícia é chamada de integração real.

## 4. Validação integrada e apresentação

- [x] 4.1 Em etapa local autorizada com origem configurada e dados totalmente sintéticos, verificar no navegador o preflight e o POST de entrada, observando resposta/estado visível e ausência de erro CORS; registrar resultado ou bloqueio concreto separadamente dos testes TestServer, sem banco pessoal nem Pluggy real.
- [x] 4.2 Executar builds/testes dos componentes afetados, regressão CORS/autenticação/sincronização e `openspec validate corrigir-cors-idempotency-key --strict --json`; conferir `git diff --check` e registrar no backlog as evidências e pendências reais, sem declarar FNR-021 integralmente aceita, suíte PostgreSQL histórica 179/181 corrigida ou publicação.

## Workflow follow-up

- Apresentar os resultados para aceite humano; somente mediante autorizações próprias encaminhar commits, PR, integração, sync e arquivamento. A validação de navegador local não substitui a dos ambientes hospedados.

## Evidências locais

### Tarefa 2.2 — contrato e middleware CORS

- `dotnet test backend/tests/Finora.Api.Tests/Finora.Api.Tests.csproj --filter FullyQualifiedName~CorsContractTests --no-restore`: **15/15 aprovados**; os 14 testes anteriormente aprovados permanecem aprovados e foi acrescentada a regressão dos métodos base.
- O host `AuthenticationApiFactory` exerce a composição ASP.NET Core em TestServer com a origem sintética `https://finora.example`. Os casos verificam origem não permitida e allowlist vazia sem `Access-Control-Allow-Origin`; ausência de `Idempotency-Key` em rota alheia/similar, barra final e método diferente; rejeição de cabeçalho desconhecido; e manutenção de `GET`/`POST`/`PATCH` com `Authorization`/`Content-Type` na política base. Os preflights das duas rotas exatas permitem os cabeçalhos contratados; respostas não habilitam credenciais CORS.
- A regressão de autenticação em rota protegida confirma `401`, challenge Bearer e nenhuma execução da action, sem Origin ou com origem permitida. Isso valida middleware/contrato no host de teste; não é ensaio de POST real com sessão nas rotas de sincronização, que permanece na tarefa 3.1.
- Limites: origem e respostas são sintéticas; TestServer não é navegador e não comprova configuração hospedada, Pluggy ou integração real. A prova de preflight/POST em navegador foi executada e está registrada separadamente na tarefa 4.1; não houve uso de dados pessoais ou serviço externo.

### Tarefa 3.1 — POST efetivo com origem

- `dotnet build backend/tests/Finora.Api.Tests/Finora.Api.Tests.csproj --no-restore --verbosity minimal`: build passou com **0 avisos/0 erros** antes da instrumentação de action; a execução focal posterior recompilou a alteração final. `dotnet test backend/tests/Finora.Api.Tests/Finora.Api.Tests.csproj --no-restore --filter 'FullyQualifiedName~SyncIntentPostsWithAllowedOriginKeepCorsOnAnonymousSuccessAndHandledErrorResponses' --verbosity minimal`: **1/1 aprovado**.
- A prova usa a composição HTTP real em `AuthenticationApiFactory`/TestServer, usuário, sessão e conexão sintéticos, PostgreSQL criado por `EnsureCreated` no Compose temporário `compose.test.yaml` (projeto exclusivo, porta dinâmica em loopback, armazenamento PostgreSQL em tmpfs) e transporte in-memory. A action filter de teste confirma zero execuções das actions após os dois 401 anônimos; leituras também confirmam zero `SyncRun`, outbox e commits de aceitação nesse ponto.
- Com `Origin: https://finora.example`, o POST automático autenticado preserva `200 not_required`, o POST manual preserva `202` e a referência persistida, e o erro tratado de execução ativa preserva `409 SYNC_RUN_ALREADY_ACTIVE`/a referência original. Todas essas respostas incluem `Access-Control-Allow-Origin` exatamente igual à origem sintética e não incluem `Access-Control-Allow-Credentials`; Bearer continua exigido.
- O Compose exclusivo foi encerrado com `down --remove-orphans`; não foram tocados os containers preexistentes de desenvolvimento ou MQ-P01. Limites: TestServer com PostgreSQL descartável não é navegador, Kestrel hospedado, serviço externo ou prova Pluggy; a validação Chromium local está registrada na tarefa 4.1. Nenhum banco persistente, Neon ou dado pessoal foi usado.

### Tarefa 3.2 — replay após resposta incerta

- `dotnet test backend/tests/Finora.Api.Tests/Finora.Api.Tests.csproj --no-restore --filter 'FullyQualifiedName~LostCommitConfirmationAfterPhysicalCommitReturnsIndeterminateAndReplayReusesPair' --verbosity minimal`: **1/1 aprovado** em PostgreSQL efêmero de loopback. O interceptor simula perda de confirmação somente depois do commit físico; o primeiro POST retorna `503 ACCEPTANCE_RESULT_INDETERMINATE`, com CORS da origem sintética. A repetição HTTP usa a mesma chave, titular, rota/modo e Origin; retorna `202` com o ID original e CORS. A persistência mantém uma `SyncRun`, não cria nova outbox e o transporte in-memory recebe uma única mensagem.
- `npm test -- --watch=false --include=src/app/auth/area/sync-intent-coordinator.spec.ts` em `frontend/`: **10/10 aprovados**. Os testes existentes confirmam replay explícito com a mesma chave e modo após falha de rede indeterminada, bloqueio de pedido paralelo, e chave diferente entre intenção on-entry e manual. Nenhum atalho ou alteração de UI foi introduzido.
- Limites: a confirmação perdida é injetada pelo interceptor local após o commit do PostgreSQL descartável, não por falha de rede externa. O frontend usa `HttpTestingController`; esta evidência não é navegador nem valida API hospedada. A prova Chromium posterior está registrada na tarefa 4.1 e usa transporte intencionalmente indisponível.

### Tarefa 4.1 — preflight e POST no navegador

- Chromium isolado percorreu login Angular e entrada autenticada em página `http://127.0.0.1:4201`, contra API Kestrel `http://127.0.0.1:5080`; origem, titular, sessão, conexão e último sucesso eram sintéticos. A API usou PostgreSQL exclusivo do `compose.test.yaml`, loopback dinâmico/tmpfs, `EnsureCreated` e sem migrations. Transporte de sincronização ficou desabilitado; não houve RabbitMQ ou Pluggy.
- O log Kestrel registrou `OPTIONS /api/connections/current/sync/on-entry` com **204** e `CORS policy execution successful`, seguido do POST real com **503 `DEPENDENCY_UNAVAILABLE`**. Chromium recebeu o corpo `application/problem+json` e `Access-Control-Allow-Origin: http://127.0.0.1:4201`; a página exibiu o estado “O resultado da solicitação não foi confirmado...” e a ação de replay. O único erro de console foi a resposta HTTP 503 esperada, sem erro CORS. A entrega de 503 foi intencional porque não se iniciou transporte.
- API, Angular, container/rede exclusivos e arquivos temporários foram encerrados/removidos; portas locais liberadas, sem tocar containers preexistentes. Essa evidência é navegador real contra API local, distinta de TestServer, mas não comprova resposta de aceitação 200/202, Pluggy, hospedagem ou ambiente persistente. A tentativa registrada antes da mudança continua somente evidência histórica pré-correção.

### Tarefa 4.2 — regressões e conferência final

- Builds finais `dotnet build backend/Finora.Api/Finora.Api.csproj --no-restore --verbosity minimal` e `dotnet build backend/tests/Finora.Api.Tests/Finora.Api.Tests.csproj --no-restore --verbosity minimal`: ambos passaram com **0 avisos/0 erros**.
- Regressão backend sem banco, via filtro que inclui `CorsContractTests`, `AuthenticationAvailabilityTests`, handlers/modelos de autenticação e testes unitários de sincronização: **59/59 aprovados**. Regressão PostgreSQL de aceitação HTTP e replay após confirmação perdida: **2/2 aprovados** em Compose exclusivo, efêmero, loopback/tmpfs; o projeto foi removido com `down --remove-orphans`.
- Em `frontend/`, `npm test -- --watch=false`: **19 arquivos/203 testes aprovados**; `npm run build -- --configuration development` e `npm run build -- --configuration production`: ambos concluídos.
- `openspec validate corrigir-cors-idempotency-key --strict --json`: **1/1 válido**, sem issues. `git diff --check`: concluído sem erros de whitespace; Git emitiu somente avisos de normalização LF→CRLF para arquivos alterados.
- A rodada ampla PostgreSQL histórica **179/181** não foi repetida nem reclassificada. Estes testes focais não demonstram compatibilidade com serviços hospedados/Pluggy nem aceitação 200/202 no navegador. Com os gates locais executados e os resultados registrados no backlog, as 10/10 tarefas técnicas estão conferidas; revisão/aceite humano e encaminhamentos permanecem separados.
