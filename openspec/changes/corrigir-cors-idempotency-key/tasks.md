# Tasks

## 1. Gate e contrato canônico

- [ ] 1.1 Apresentar proposta, delta `api-access-boundary`, design e tarefas para aprovação explícita antes de alterar código; conferir decisão do titular registrada e escopo FNR-021/FNR-011 sem aceite implícito do explorador.
- [ ] 1.2 Preparar revisão delimitada de `docs/TDD.md` e `docs/BACKLOG.md` para registrar os dois POSTs, a exceção de cabeçalho por rota/método e o bloqueio observado; apresentar o diff para revisão antes do código dependente e conferir que aprovações e ressalvas 0.36/0.85 não foram transferidas.

## 2. Permissão CORS restrita

- [ ] 2.1 Implementar política base e política específica com seleção exata de caminho/método no ponto único de CORS da API, mantendo origens configuradas, fallback fechado e ordem antes da autenticação; verificar em host HTTP que OPTIONS `POST` das duas rotas com `Authorization, Idempotency-Key` concede somente os itens contratados sem Bearer.
- [ ] 2.2 Estender `CorsContractTests` com negativas de origem não autorizada, allowlist vazia, rota alheia ou similar, método diferente, cabeçalho adicional desconhecido e regressão de métodos/cabeçalhos anteriores; verificar os cabeçalhos concedidos (não apenas 204), ausência de credenciais e execução dos testes focais.
- [ ] 2.3 Atualizar a descrição operacional de CORS no TDD canônico conforme o comportamento efetivamente entregue, mantendo revisão pendente até aceite, e conferir sua correspondência com os cenários da delta e os testes focais.

## 3. POST efetivo e repetição idempotente

- [ ] 3.1 Testar `POST` real no host com `Origin` permitido nas duas rotas: sem Bearer deve retornar 401 com origem CORS e sem executar ação; com sessão válida e fixture sintética/infra descartável, verificar permissão de origem nas respostas 200/202 e em erro tratado sem alterar o status ou contrato da API.
- [ ] 3.2 Exercitar replay HTTP com mesma chave, titular e modo após resposta incerta e verificar referência persistida única sem nova execução; conferir também os testes existentes de `SyncIntentCoordinator` para chave reaproveitada e intenção nova distinta, sem criar atalho na UI.
- [ ] 3.3 Registrar em `docs/AMBIENTE.md` os testes executados e os resultados sintéticos de transporte e idempotência, distinguindo-os de navegador, Pluggy e serviços hospedados; verificar que nenhuma evidência fictícia é chamada de integração real.

## 4. Validação integrada e apresentação

- [ ] 4.1 Em etapa local autorizada com origem configurada e dados totalmente sintéticos, verificar no navegador o preflight e o POST de entrada, observando resposta/estado visível e ausência de erro CORS; registrar resultado ou bloqueio concreto separadamente dos testes TestServer, sem banco pessoal nem Pluggy real.
- [ ] 4.2 Executar builds/testes dos componentes afetados, regressão CORS/autenticação/sincronização e `openspec validate corrigir-cors-idempotency-key --strict --json`; conferir `git diff --check` e registrar no backlog as evidências e pendências reais, sem declarar FNR-021 integralmente aceita, suíte PostgreSQL histórica 179/181 corrigida ou publicação.

## Workflow follow-up

- Apresentar os resultados para aceite humano; somente mediante autorizações próprias encaminhar commits, PR, integração, sync e arquivamento. A validação de navegador local não substitui a dos ambientes hospedados.
