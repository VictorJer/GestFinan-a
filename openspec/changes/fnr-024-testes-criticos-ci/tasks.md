# Tasks

## 1. Gate de planejamento FNR-024

- [x] 1.1 Conferir a rastreabilidade FNR-024 ↔ PRD RF02/03/04/05/07/14/17, TDD §§9/14/19 e backlog §5; apresentar proposal/design/tasks para revisão e registrar a aprovação explícita pré-código, verificando que não há aprovação presumida das revisões pendentes BACKLOG 0.88/TDD 0.40 nem aceite integrado da FNR-021/M-03.
- [x] 1.2 Preparar na feature branch a revisão factual necessária de BACKLOG/TDD para o contrato de checks e o estado integrado do PR #44, se a conferência indicar alteração canônica; verificar o diff e obter aprovação própria antes de código dependente, preservando histórico e pendências.

## 2. Recuperar as provas PostgreSQL

- [x] 2.1 Atualizar o setup de migration em `PostgreSqlFinancialIntegrityTests` para aplicar o schema exigido pelo `SyncRun` atual; reconciliar a contagem de `Category` para 10 (7 categorias iniciais semeadas por FNR-016 + 3 do cenário), mantendo a asserção contra gravações indevidas e todas as asserções de FK/propriedade/restrição; verificar execução isolada sem falhas nem casos ignorados em PostgreSQL de teste allowlisted.
- [x] 2.2 Atualizar apenas o setup de migration em `PostgreSqlFinancialTransactionAtomicityTests`, preservando gravação anterior, falha injetada, rollback e conferência em contexto novo; verificar execução isolada sem falhas nem casos ignorados no mesmo tipo de banco descartável.
- [x] 2.3 Registrar em `docs/AMBIENTE.md` o antes/depois e a identidade do schema, com contagens reais dos dois testes e limites; verificar que `git diff` não altera migrations históricas nem minimiza as asserções, sem chamar 179/181 de aprovação retroativa.

## 3. Checks backend reproduzíveis

- [x] 3.1 Criar workflow de checks para PR à `main` e `push` na `main`, com job backend sem PostgreSQL, SDK de `global.json`, restore bloqueado, build e filtro explícito excluindo somente classes com PostgreSQL e `MqP01CompatibilityTests`; verificar o comando local equivalente com seleção não vazia, saída de falha propagada e nenhum passo de deploy.
- [x] 3.2 Criar job PostgreSQL 18 isolado com health, banco de teste allowlisted e `FINORA_TEST_CONNECTION_STRING` somente no processo de testes; executar seleção PostgreSQL ampla, excluindo somente as classes com broker RabbitMQ real `PostgreSqlRabbitMqSyncRunIntegrationTests` e `MqP01CompatibilityTests`, e verificando em execução que há testes de integridade, rollback, importação, autenticação, visão e recorrência sem falhas/ignorados.
- [x] 3.3 Documentar em `docs/AMBIENTE.md` os comandos reproduzíveis dos dois jobs, filtros exatos, resultados e a exclusão explícita das duas provas de broker real, mantendo MQ-P01 como gate independente e Pluggy simulada; verificar que não há segredos, destinos persistentes nem alegação de broker real.

## 4. Check frontend reproduzível

- [x] 4.1 Criar job frontend de PR com Node 24, `npm ci`, Vitest sem watch e build Angular de produção (e desenvolvimento se requerido pelo gate documental aprovado); verificar a execução local equivalente, incluindo casos de sessão, lista, visão, falhas e recorrência, com seleção não vazia e propagação de exit code.
- [x] 4.2 Registrar em `docs/AMBIENTE.md` comandos e resultados do frontend, distinguindo HTTP simulado de navegador/API reais; verificar no diff a ausência de alteração de comportamento das telas e de dependência para o CI que não tenha sido validada.

## 5. Integração e gates observáveis

- [ ] 5.1 Validar a sintaxe do workflow e, após autorização específica para commits/PR, executá-lo em PR da feature branch para `main`, conferindo nomes, contagens e resultado efetivo dos três checks na plataforma; o disparo no commit integrado só pode ser conferido após integração autorizada, sem declará-lo concluído antecipadamente.
- [x] 5.2 Conferir as regras/rulesets aplicáveis à `main` e registrar os checks exigidos versus os apenas executados; propor/ativar obrigatoriedade somente após autorização e permissões correspondentes, verificando a configuração real sem declarar deploy bloqueado por CI enquanto FNR-026/027 não implementarem a seleção do commit.
- [x] 5.3 Executar validação integrada proporcional (`openspec validate fnr-024-testes-criticos-ci --strict`, `git diff --check`, builds e suítes dos jobs) e apresentar matriz de resultado/pendência para aceite humano; verificar que a exclusão de `MqP01CompatibilityTests` e `PostgreSqlRabbitMqSyncRunIntegrationTests` está explícita e vinculada ao gate MQ-P01 separado, e que publicação, provas reais e falhas que eventualmente surgirem não foram ocultadas por filtros adicionais.

## Workflow follow-up

- Após aprovação de implementação, organizar commits locais autorizados com o committer e abrir PR vinculado a FNR-024, PRD, TDD e esta mudança; não presumir autorização de merge ou publicação.
- Após validação/aceite e integração conforme o fluxo do projeto, arquivar a mudança OpenSpec; não há delta specs para sincronizar.
