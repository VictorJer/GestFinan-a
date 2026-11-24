# Proposal

## Why

A FNR-015 já oferece movimentos filtrados, contagem e cobertura, mas ainda não há um resumo por natureza e situação para a visão descritiva prevista em RF07. A FNR-018 deve entregar essa leitura no backend sem transformar movimentos ambíguos ou uma janela parcial em indicadores financeiros validados.

## What Changes

- Adicionar `GET /api/dashboard` autenticado para contagens não monetárias por natureza e por situação, total de movimentos persistidos que correspondem ao filtro e cobertura/completude separada desse total.
- Usar o mesmo universo e filtros de `GET /api/transactions` (cartão, categoria, situação, intervalo da data técnica e busca literal), sem paginação; explicitar filtros e semântica temporal no contrato e conciliar as contagens com `filteredCount` da lista.
- Preservar explicitamente naturezas/situações desconhecidas, pendentes e parcelas futuras identificadas; contar movimentos, não compras reconhecidas. Cobertura desconhecida ou parcial nunca vira histórico bancário completo ou gasto zero.
- Não calcular, retornar ou apresentar somas, subtotais, médias, séries ou distribuição monetária por categoria/moeda; nem renda, saldo, patrimônio ou métricas de recorrência derivados dos movimentos.
- Restringir esta mudança à API e seus testes/documentação. A lista e a apresentação da visão Angular permanecem em FNR-022/023, e o detector em FNR-038; não concluir as tarefas ainda abertas da mudança do explorador apenas com esta API.

## Capabilities

### New Capabilities

- `card-movement-summary`: resumo protegido de contagens descritivas e cobertura do universo persistido filtrado, sem agregados monetários.

### Modified Capabilities

Nenhuma. `card-movement-query` já define filtros, contagem e cobertura da lista; sua semântica é reutilizada sem alterar os requisitos existentes. `transaction-annotations` permanece inalterada.

## Impact

- Rastreabilidade: FNR-018; PRD RF05/RF07, §8 e M-03; TDD §§11/13, DT-41/45/54; dependências FNR-005 revisada, FNR-015 e FNR-017. O registro histórico do BACKLOG sobre PR #31 deverá ser reconciliado em revisão documental própria antes de código dependente.
- Backend: novo caso de uso MediatR, leitor agregado de PostgreSQL/EF Core, DTO HTTP allowlist e rota `GET /api/dashboard`; preservar políticas de autorização, 400/401/404/409/429/500/503 sanitizados, filtros e cobertura já existentes. Nenhuma migration, broker, consulta Pluggy ou dependência nova é prevista.
- Testes: cenários sintéticos de filtros, datas desconhecidas, natureza/situação independentes, importação parcial, titularidade, edições de categoria, consistência lista/resumo, snapshot, ausência de agregados monetários e desempenho local com até 5.000 movimentos.
- Estado: proposta de planejamento; sem aceite antecipado da implementação, publicação, migração em destino ou validação financeira adiada.
