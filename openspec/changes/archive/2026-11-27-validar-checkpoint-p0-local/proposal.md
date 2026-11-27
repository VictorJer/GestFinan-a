# Proposal

## Why

O plano aprovado exige evidência e aceite explícito do checkpoint P0 local antes de iniciar FNR-029–031, mas os registros disponíveis ainda não demonstram a importação persistente com origem real nem o fluxo integrado completo da atualização na interface. É necessário confrontar os critérios do PRD §13 com provas verificáveis, preservando os bloqueios em vez de converter testes sintéticos, merges ou CI em aceite.

## What Changes

- Preparar e executar uma verificação **local e processual** do P0, rastreada ao BACKLOG §§3/5/7, PRD §13 e TDD §§14/16: inventariar evidências existentes, identificar lacunas, registrar resultados e limites sem expor informações financeiras.
- Submeter qualquer acesso à Pluggy real, banco persistente/de desenvolvimento, credenciais, migração, alteração de serviços ou uso operacional de RabbitMQ a autorização específica e confirmação do destino antes da ação; sem autorização ou pré-requisito, registrar bloqueio, não simular sucesso.
- Separar a prova da importação paginada/repetida com persistência, falhas/retomada, anotações e consultas coerentes da prova de navegação/atualização FNR-021/M-03 e do gate MQ-P01; obter aceite próprio de cada escopo, sem inferi-lo do checkpoint.
- Apresentar ao titular uma matriz sanitizada de critérios, evidências, fontes, lacunas e decisões para aceite explícito ou manutenção do bloqueio P0→P1. Eventuais defeitos ou requisitos novos serão tratados em mudanças próprias antes de novo aceite.
- Não alterar código, APIs, requisitos funcionais, infraestrutura, migrations, PRD ou TDD por antecipação; não iniciar P1, publicar nem declarar FNR-028 concluída nesta mudança.

## Capabilities

### New Capabilities

Nenhuma. O checkpoint documenta validação e decisão sobre comportamentos já especificados.

### Modified Capabilities

Nenhuma. Os contratos de `transaction-import`, `sync-execution`, `administrative-connection-provisioning` e `web-card-connection`, entre outros, não mudam; `.openspec.yaml` declara `skip_specs: true` para não criar requisitos artificiais.

## Impact

- Escopo de execução posterior: evidências sanitizadas em `docs/AMBIENTE.md` e estado factual em `docs/BACKLOG.md` §7, somente quando verificadas; artefatos de planejamento desta mudança em `openspec/changes/validar-checkpoint-p0-local/`.
- Pontos a verificar, sem alterá-los por esta proposta: comando administrativo FNR-039; importação FNR-012; execução FNR-013/014 e broker MQ-P01; API FNR-015/017/018; frontend FNR-020–023/038 e testes de integração existentes.
- Dados reais e destinos persistentes dependem de autorização operacional distinta da aprovação destes artefatos. CI FNR-024 e testes locais passados são evidências limitadas; gates hospedados FNR-025–028 e MQ-P02–04 permanecem separados.
