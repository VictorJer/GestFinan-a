# Proposal

## Why

O titular relatou `user-create` retornando `failure=operational` antes do prompt de e-mail em Development, mesmo após configurar a conexão. A varredura de todo o assembly Application pelo host administrativo é uma causa plausível: ela registra handlers com dependências ausentes nessa composição, e a validação do contêiner em Development pode impedir a construção do host. A causa ainda precisa ser reproduzida em isolamento; a correção deve restaurar o contrato administrativo existente sem consultar o banco ou segredos do titular.

## What Changes

- Reproduzir a falha de construção do host com configuração e conexão exclusivamente sintéticas, isolando as fontes de configuração **antes** da criação do builder para não ler User Secrets, appsettings nem variáveis reais; manter `ValidateOnBuild` e `ValidateScopes` explicitamente ativos.
- Se confirmada a hipótese, limitar o registro MediatR do comando aos handlers de pré-consulta, criação e redefinição de senha; não adicionar dependências de HTTP, Pluggy ou RabbitMQ.
- Cobrir construção do host em Development e Production, resolução dos três handlers, isolamento de dependências e saída sanitizada em falha. Se a hipótese não se confirmar, registrar a evidência e revisar o desenho com o titular antes de ampliar o escopo.
- Não modificar contrato HTTP, persistência, migrations, política de senha, configurações operacionais ou decisões arquiteturais aprovadas.

## Capabilities

### New Capabilities

Nenhuma.

### Modified Capabilities

Nenhuma. `skip_specs: true`: a correção restaura os requisitos já consolidados em `administrative-user-access` (execução administrativa explícita e isolada; resultados operacionais sem vazamento), sem alterar o comportamento exigido nem criar requisitos artificiais. Rastreabilidade: FNR-008 (manutenção da entrega), PRD RF14 e TDD §5 / DT-23–DT-26, DT-38, DT-48–DT-49.

## Impact

- Planejamento e, após aprovação, `backend/Finora.Api/AdministrativeUsers/AdministrativeUserCommand.cs` e testes focais em `backend/tests/Finora.Api.Tests/AdministrativeUserCommandTests.cs` (ou arquivo coeso adjacente).
- Nenhuma dependência nova, mudança de configuração operacional, endpoint, banco, migração ou execução administrativa real. Falhas permanecem classificadas sem expor detalhes ao operador.
- Esta proposta não confirma a causa nem representa teste executado. Aprovação dos artefatos é exigida antes de implementar; sem commit, PR, acesso ao banco persistente ou publicação neste escopo.
