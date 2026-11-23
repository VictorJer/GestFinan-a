# Proposal

## Why

Os cartões e movimentos já podem ser persistidos com histórico e estados de importação, mas ainda não há API financeira de leitura para o titular autenticado. A FNR-015 entrega a consulta descritiva necessária à lista futura sem transformar movimentos ambíguos ou cobertura parcial em indicadores de gasto.

## What Changes

- Criar `GET /api/cards` e `GET /api/transactions` para leitura do histórico persistido do titular, com filtros combináveis, busca literal por descrição, ordenação determinística e paginação limitada.
- Retornar valor/moeda por movimento, data de origem e sua interpretação técnica, situação e natureza/motivo independentes, metadados de parcela disponíveis e categoria pessoal já vinculada, sem inferir compras ou competência.
- Informar contagem de todo o conjunto filtrado e contexto de cobertura/completude da importação separadamente, sem agregar valores monetários ou converter ausência de dados em zero financeiro.
- Garantir sessão válida, predicados de titularidade também no banco, erros sanitizados e leituras que não iniciem sincronização nem consultem a Pluggy.

## Capabilities

### New Capabilities

- `card-movement-query`: consulta autenticada de cartões e movimentos persistidos, com filtros, paginação, contagem e contexto honesto de cobertura. É a fronteira de API da FNR-015, não a capability de apresentação da mudança `explorador-movimentos-cartao-sem-total`.

### Modified Capabilities

Nenhuma. `financial-data-persistence`, `transaction-import`, `api-access-boundary`, `connection-update-policy` e `sync-execution` já fixam os contratos de armazenamento, autorização e execução consumidos por esta API; seus requisitos não mudam.

## Impact

- Rastreabilidade: FNR-015; PRD RF04/RF05 e M-03; TDD §§10–11, DT-41/DT-54. A mudança existente do explorador orienta os limites de apresentação, mas suas tarefas de API, interface e resumo não são marcadas como concluídas por esta proposta; reconciliar a sobreposição antes de executar/encerrar as duas mudanças.
- Código futuro: Controllers/DTOs na `Finora.Api`, consultas na `Finora.Application`, projeções EF Core na `Finora.Infrastructure` e testes HTTP/PostgreSQL sintéticos em `backend/tests/Finora.Api.Tests`. Sem mudança de origem, migration presumida ou dependência de broker para leitura.
- Fora do escopo: Angular e lista visual (FNR-019/020/022/023), criação/edição de categorias e observações (FNR-016/017), dashboard/resumos por natureza (FNR-018), detector de recorrência (FNR-038), total filtrado e demais agregados monetários adiados. FNR-014 continua responsável pela política de atualização; esta consulta não dispara execução.
- `docs/BACKLOG.md` 0.50 ainda descreve o encerramento FNR-014 como sem commit/PR, embora o HEAD local já contenha o commit de encerramento: reconciliar o registro factual e registrar a FNR-015 na execução, sem inventar aprovação da revisão documental 0.50 ou da nova mudança. O TDD §11/DT-41 ainda trata DTO/paginação desta rota como propostas: alinhar seu contrato às decisões da FNR-015 e obter aprovação do diff antes do código dependente. PRD permanece referência aprovada, sem revisão presumida.
