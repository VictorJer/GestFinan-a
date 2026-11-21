# Proposal

## Why

A FNR-004/005 comprovou leitura local limitada e a FNR-007 criou o modelo persistente, mas ainda não há um importador que percorra todas as páginas disponíveis e grave movimentos repetidamente sem duplicar ou perder anotações. A FNR-012 entrega esse caso de uso antes de expor consulta financeira (FNR-015) ou execução durável via API/fila (FNR-013).

## What Changes

- Importar os cartões e movimentos disponíveis de uma conexão de um titular, desde a referência fixa de até 12 meses anteriores, percorrendo a paginação segura até o fim; preservar parcelas futuras recebidas e não prometer histórico bancário de doze meses.
- Reconciliar por identidade externa contextual, atualizar registros mutáveis no mesmo identificador interno, manter categoria/observação pessoais e não excluir movimentos apenas porque faltaram em resposta posterior.
- Publicar o conjunto coletado somente depois da coleta, validação e gravação consistente; falha, resposta contraditória, cursor inseguro ou cancelamento não avançam o último sucesso nem apresentam intervalo parcial como completo.
- Informar resultado técnico sanitizado, contagens e cobertura observada ou indeterminada, distintos da janela solicitada. Preservar normalização conservadora: desconhecidos e datas de origem não viram compras ou competência validada.
- Tratar reconexão sem fusão automática por nome/final do cartão; só reutilizar identidade quando houver vínculo de origem comprovável, bloqueando reconciliações ambíguas para revisão posterior.
- **Fora de escopo:** endpoints de atualização/consulta, trabalhador, outbox/RabbitMQ, posse/retomada de `SyncRun` (FNR-013), atualização periódica (FNR-014), interface, somas monetárias e detecção de recorrência. Nenhuma nova consulta ao provedor real é autorizada por este planejamento.

## Capabilities

### New Capabilities

- `transaction-import`: coleta completa e segura do intervalo disponível e publicação idempotente de movimentos do cartão com rastreabilidade de cobertura/falhas.

### Modified Capabilities

Nenhuma. `financial-data-persistence`, `pluggy-contract-analysis` e `transaction-normalization` conservam seus requisitos; o importador compõe essas capacidades sem converter a ferramenta diagnóstica em gravador.

## Impact

- Backlog FNR-012; PRD RF02/RF03, RNF-04/05/09/10; TDD §§10/12/14/19, DT-34/35/40/42 e catálogo `docs/PLUGGY-CONTRACT.md`.
- Backend: novo caso de uso em `Finora.Application`, adaptador de coleta Pluggy e persistência em `Finora.Infrastructure`, invariantes em `Finora.Domain` somente se necessárias; testes de contrato sintéticos e integração PostgreSQL descartável. Os comandos locais de análise/leitura continuam sem persistência e sem mudanças de contrato.
- Não cria rota financeira pública nem instala broker nesta entrega. O fluxo utilizável por HTTP fica dependente da FNR-013 e dos gates próprios; qualquer migration será aplicada por comando administrativo, nunca no startup da API.
- `docs/BACKLOG.md` 0.42 declara revisão documental pendente apesar da integração técnica da FNR-011; revisar esse registro separadamente antes de declarar aprovado o contexto desta mudança. A criação destes artefatos não aprova a FNR-012 nem autoriza implementação, PR ou publicação.
