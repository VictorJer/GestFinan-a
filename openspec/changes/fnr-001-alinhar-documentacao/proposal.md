# Proposal

## Why

O PRD ainda descreve uso apenas local e uma proposta de SQLite, enquanto o TDD e o README já registram hospedagem autenticada, PostgreSQL, categorias pessoais e observações. A FNR-001 precisa eliminar essas divergências antes da verificação de ambiente e da primeira leitura real pelo backend, sem transformar decisões registradas em evidência de implementação.

## What Changes

- Alinhar os requisitos do PRD às decisões confirmadas sobre acesso do titular, sessão, categorias, observações, histórico e atualização dos dados.
- Manter versões, arquitetura, persistência e implantação no TDD; substituir envio direto à main pelo fluxo feature branch, especificação revisada, implementação, testes, PR, integração e deploy condicionado aos checks.
- Conferir README e backlog, registrar rastreabilidade e separar aprovação documental de revisão do PR, checks e integração.
- Preservar a prova de conceito local com transações reais como primeira meta funcional, antes do dashboard.

## Capabilities

### New Capabilities

Nenhuma. Esta mudança apenas reconcilia documentação de decisões já registradas; não introduz comportamento da aplicação.

### Modified Capabilities

Nenhuma. Não existem specs principais no projeto. `skip_specs: true` dispensa deltas nesta mudança documental; requisitos das funcionalidades serão especificados nas mudanças correspondentes do backlog.

## Impact

- Documentos: `docs/PRD.md`, `docs/TDD.md`, `README.md` e `docs/BACKLOG.md`.
- Planejamento: metadados, proposta, desenho/SDD e tarefas desta mudança OpenSpec.
- Sem código, dependências, migrations, acesso a dados reais, configuração de provedores ou publicação.
- Branch: `docs/fnr-001-alinhar-documentacao`. Documentos e SDD aprovados explicitamente pelo autor em 13/11/2026 nesta conversa; commit e abertura de PR autorizados. Aprovação documental não conclui a FNR-001 nem aprova implementações futuras. Revisão do PR, checks e integração permanecem pendentes; merge, arquivamento e avanço para FNR-002 não estão autorizados nesta etapa.
