# Design

## Context

Ver [proposal.md](proposal.md). O projeto usa `global.json` com SDK .NET 10.0.402 e Microsoft.Testing.Platform (MSTest.Sdk 4.5.3); o frontend usa Angular 22/Vitest 5.1.0, Node 24 e `package-lock.json`. Ainda não há workflow em `.github/workflows/`. A fixture `DisposablePostgresMigrationFixture` só aceita `FINORA_TEST_CONNECTION_STRING` em loopback e database `finora_integration_test`, recriando o schema `public` após validação. As duas provas legadas em `PostgreSqlFinancialIntegrityTests` e `PostgreSqlFinancialTransactionAtomicityTests` migram até `CoreMigrationId`, embora gravem `SyncRun` com a coluna posterior `AttemptDeadlineAtUtc`; o resultado histórico 179/181 não comprova as asserções dessas provas. Há testes de broker real separados (`MqP01CompatibilityTests`, `PostgreSqlRabbitMqSyncRunIntegrationTests`), com gate MQ-P01 próprio.

## Goals / Non-Goals

**Goals:**

- Obter resultados reprodutíveis de build, testes sem banco, integração PostgreSQL e frontend em checks de PR distintos; falha ou zero testes selecionados não pode aparecer como aprovação.
- Executar integralmente integridade/rollback com schema atual e preservar o isolamento dos dados e asserções existentes.
- Permitir configurar checks exigidos na integração e deixar explícita a diferença entre workflow presente, checks reportados e proteção de branch efetivamente habilitada.

**Non-Goals:**

- Alterar contratos HTTP, regras financeiras, migrations históricas, modelo de domínio, configuração de produção ou comportamento de deploy.
- Usar Pluggy, Neon, banco pessoal ou broker RabbitMQ real no CI desta mudança; sua validação e eventual aceitação de risco seguem gates próprios.
- Transformar a medição local de 5.000 movimentos em limite temporal flutuante no CI, impor percentual de cobertura ou tomar os 179/181 históricos como resultado novo.

## Decisions

### D1 — Workflow de PR com checks estáveis e sem publicação

Criar workflow de GitHub Actions para `pull_request` à `main` e `push` na `main` sem filtro de caminhos, com jobs de nomes estáveis para backend sem PostgreSQL, backend PostgreSQL e frontend. O `push` executa checks no commit integrado, mas não dispara deploy por este workflow; é necessário evitar auto-deploy paralelo na futura FNR-026/027. Usar permissões mínimas de leitura, instalações fixadas pelos manifests/locks e comandos adequados ao runner MTP (sem presumir opções do VSTest). Builds da solução e do Angular antecedem suas suítes; registrar contagens/saída reais e falhar em exit code não zero, seleção vazia ou teste ignorado que seja obrigatório. Não criar passo de deploy, migration em destino ou gatilho `pull_request_target`. Alternativa de checks filtrados por caminhos foi rejeitada porque um workflow ignorado pode deixar required checks pendentes; alternativa de único job dificulta identificar a fronteira que falhou.

### D2 — PostgreSQL isolado e recorte explícito do broker

O job PostgreSQL usa serviço PostgreSQL 18 em runner Linux, health check e acesso via `127.0.0.1` no runner efêmero, com database `finora_integration_test`, usuário/senha sintéticos efêmeros e `FINORA_TEST_CONNECTION_STRING` apenas no processo de testes. Reutilizar a barreira existente de host/database antes de qualquer reset; nunca usar `DATABASE_URL` nem segredos de produção. Executar suíte PostgreSQL incluindo migrations, API, importação, segurança, contagens e recorrência, excluindo **somente** as duas classes que exigem broker RabbitMQ real: `PostgreSqlRabbitMqSyncRunIntegrationTests` (broker/vhost próprios) e `MqP01CompatibilityTests` (gate MQ-P01). Nenhum job de CI provisiona broker; ambas as provas permanecem gates separados e não são simuladas. O job sem PostgreSQL também exclui `MqP01CompatibilityTests`, além das classes dependentes de PostgreSQL. Documentar precisamente as exclusões e não relatar integração de transporte. Alternativas: broker no mesmo CI (bloqueado pelo gate MQ-P01 e pelo ciclo de vida de vhost/filas) ou mocks como prova de RabbitMQ real (inválidos). Testes PostgreSQL que usem transportes em memória permanecem elegíveis.

### D3 — Atualizar schema das duas fixtures sem perder a prova histórica

Somente nos dois testes apontados, substituir a preparação até `CoreMigrationId` por aplicação das migrations necessárias ao modelo `SyncRun` atual, com `MigrateAsync()` no banco **descartável de teste**, preservando reset validado e todas as asserções de FK, proprietário, ON DELETE e rollback após gravação prévia. Aplicar a cadeia completa também executa a migration FNR-016, que semeia sete categorias iniciais; na prova de integridade, a contagem final preserva a checagem contra gravações indevidas e passa a esperar 10 categorias (sete do catálogo + três do cenário). Verificar os dois casos individualmente antes da suíte ampla; se a evolução do schema revelar outra incompatibilidade nas premissas dos testes, corrigi-la explicitamente sem enfraquecer o critério ou editar migrations anteriores. Alternativa de inserir manualmente `AttemptDeadlineAtUtc` ou suprimir o `SyncRun` foi rejeitada por produzir schema fictício ou reduzir a prova. O `MigrateAsync()` aqui não é migration no startup da API.

### D4 — Gates de integração/publicação com evidência observável

Definir os três nomes de checks e verificar, após um PR de validação, se as regras/rulesets da `main` exigem cada um; solicitar/realizar configuração de proteção apenas com autorização aplicável e permissões disponíveis, sem bypass. Se indisponível ou não configurada, registrar isso como gate pendente, não como aprovado. A publicação automática e a seleção do commit validado dependem de FNR-026/027: esta mudança define o contrato de checks, não conecta provedores nem garante bloqueio de auto-deploy ainda não configurado. Alternativa de confiar somente no merge manual ou em build local foi rejeitada por não fornecer gate verificável no PR.

## Risks / Trade-offs

- [Runner/serviço indisponível ou mudança de versões do GitHub Actions] → usar versões verificadas de ações, pins/locks do projeto e registrar indisponibilidade como falha de infraestrutura, não como teste verde.
- [Fixtures PostgreSQL compartilham schema e reinicializam `public`] → executar teste de integração em processo/serviço exclusivo, respeitando `[DoNotParallelize]` e a allowlist; nunca apontar para banco persistente/remoto.
- [Suíte ampla revela falhas adicionais após corrigir as duas primeiras] → manter o job vermelho, classificar causas e apresentar revisão de escopo antes de ampliar mudanças; não ocultar falhas com filtros adicionais.
- [Proteção da `main` ou deploy nativo não configurado] → reportar estado concreto dos checks/rulesets; não afirmar publicação segura enquanto FNR-026/027 não validarem seu próprio mecanismo de deploy.
- [CI sem RabbitMQ real ou Pluggy real] → rotular essa fronteira como não comprovada; manter MQ-P01/MQ-P02/MQ-P04 e validação real/hospedada como pendências separadas.

## Migration Plan

Não há migration de aplicação ou mudança de dados. A futura entrega ocorre em feature branch: conferir os dois testes no PostgreSQL descartável, executar suítes e builds, submeter o workflow no PR e observar cada check na plataforma. Habilitar required checks somente após confirmar nomes e permissões, com aprovação específica para alterações de regras; não alterar configuração remota por inferência do plano. Em caso de falha do CI, corrigir na branch ou manter PR bloqueado, sem reduzir cobertura por exclusão implícita. Rollback do workflow pelo processo normal de PR, preservando evidências e sem reverter migrations históricas.
