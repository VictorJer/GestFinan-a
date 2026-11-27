# Proposal

## Why

O núcleo P0 já dispõe de testes locais, mas ainda não tem checks automatizados de PR; duas provas PostgreSQL de integridade e rollback permanecem inconclusivas por usar uma migration antiga com o modelo atual. A FNR-024 estabelece evidência reproduzível para a integração do núcleo antes de preparar imagem e publicação (FNR-025–028), sem confundir testes simulados com validação hospedada.

## What Changes

- Corrigir somente a preparação de schema das duas provas PostgreSQL legadas para executar integralmente suas asserções com migrations compatíveis, sem editar migrations históricas nem enfraquecer as verificações.
- Consolidar os testes existentes de backend (MSTest, PostgreSQL descartável e Pluggy simulada) e frontend (Vitest), incluindo os casos de `recurrence-v1`, num CI com restore/instalação reprodutíveis, builds e falha explícita em regressões. As provas que dependem de RabbitMQ real (`MqP01CompatibilityTests` e `PostgreSqlRabbitMqSyncRunIntegrationTests`) permanecem fora dos jobs de CI e no gate MQ-P01 próprio.
- Definir checks identificáveis para PRs e commits integrados à `main`, conferir como torná-los obrigatórios nas regras do repositório e documentar o bloqueio de integração/publicação quando não passarem; distinguir configuração desejada de proteção efetivamente ativada.
- Registrar resultados, exclusões justificadas e pendências sem converter a série histórica 179/181 em aprovação retroativa, nem fazer do RabbitMQ real um teste simulado de integração ou uma dependência implícita dos jobs de CI.

## Capabilities

### New Capabilities

Nenhuma. A FNR-024 altera testes, automação e evidência, não o comportamento da aplicação.

### Modified Capabilities

Nenhuma. Os requisitos existentes de persistência, segurança, consulta e recorrência permanecem inalterados; `skip_specs: true` declara explicitamente a ausência de delta comportamental.

## Impact

- Planejados para a futura implementação: `.github/workflows/`, `backend/tests/Finora.Api.Tests/` (apenas testes/fixtures), eventualmente scripts de apoio específicos e documentação operacional em `docs/BACKLOG.md`, `docs/TDD.md` e `docs/AMBIENTE.md` se necessário.
- Reutiliza `backend/Finora.slnx`, `frontend/package.json` e os locks existentes; CI com PostgreSQL de teste isolado, sem credenciais Pluggy/Neon, banco pessoal, migração em destino ou alteração de rotas/DTOs.
- Não inclui Docker de produção, configuração de Neon/Render/Cloudflare, deploy, teste de broker RabbitMQ hospedado, coleta real Pluggy ou aceite integrado FNR-021/M-03. O PR #44 já consta como integrado no GitHub, mas os documentos canônicos ainda requerem reconciliação/revisão próprias; esta proposta não os altera.
