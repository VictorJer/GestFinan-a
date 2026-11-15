# Proposal

## Why

A FNR-004 comprovou a leitura de uma página real (321 registros), mas seu DTO conserva somente IDs e não valida semântica financeira ou cobertura histórica. A FNR-005 precisa estabelecer um contrato verificável antes da persistência FNR-007, da importação FNR-012 e dos indicadores FNR-018, evitando confundir pagamentos, estornos e pendências com compras efetivadas.

## What Changes

- Acrescentar uma análise local explícita do contrato, com paginação cursor-based limitada, medição de volume/duração e cobertura informada sem alegar histórico bancário completo.
- Definir contratos mínimos de transação e normalização explícita de dinheiro, moeda, datas, natureza, situação e metadados opcionais, sem armazenar respostas brutas.
- Criar regras conservadoras e exemplos inteiramente fictícios para compras, estornos, pagamentos de fatura, encargos, pendências, parcelas futuras e natureza desconhecida.
- Registrar documentação do contrato com separação entre documentação oficial, observação real, hipótese e limitação; validar a leitura real somente na execução posterior autorizada pelo titular.
- Ampliar o diagnóstico com agregados fixos por código conhecido de `operationType`, cruzamentos com direção/sinal/status, presença de `billId`, distribuição de `paymentType`, consistência de parcelas e relações entre campos de data, sempre separados entre recebidos e únicos e sem conteúdo individual.
- Revisar a hipótese D4 após conferência local sanitizada do titular: `PAGAMENTO` pode representar compra nesta conexão e não deve ser tratado exclusivamente como pagamento de fatura; distinguir natureza confirmada de um caso de qualquer regra automática e manter o gate de compra desligado enquanto não houver predicado discriminatório validado.
- Preservar `pluggy-read`, `/health` e Swagger existentes. Não implementar banco, sincronização persistente, mensageria, login, APIs financeiras, dashboard ou publicação.

## Capabilities

### New Capabilities

- `pluggy-contract-analysis`: análise local paginada, limitada e sanitizada do contrato e da cobertura disponível na origem.
- `transaction-normalization`: interpretação financeira explícita e conservadora de lançamentos do cartão, independente de HTTP e persistência.

### Modified Capabilities

Nenhuma. `pluggy-local-read` permanece restrita à prova de uma página; a análise é outro comando, sem alterar seu contrato.

## Impact

- Rastreabilidade: [backlog FNR-005](../../../docs/BACKLOG.md), critérios §5; [PRD 0.3](../../../docs/PRD.md), RF02/RF07 e §8, RNF-01/02/05/09/10, VP-02/03/04; [TDD 0.6](../../../docs/TDD.md), §§7/10/12/13, DT-09/27/34/38/40/42/43/48/49.
- Futuras alterações em `backend/Finora.Api` (comando/composição), `Finora.Application` (query/handler/contratos), `Finora.Infrastructure/Pluggy` (DTOs/transporte paginado), `Finora.Domain` (regras puras) e testes MSTest existentes. Sem novos pacotes previstos; preservar locks e baseline.
- Na implementação: documentar o contrato em `docs/PLUGGY-CONTRACT.md`, evidências em `docs/AMBIENTE.md`, status verdadeiro no backlog e instruções locais no README. Atualizar o TDD somente se descobertas alterarem decisões, preservando aprovações anteriores e submetendo a nova revisão.
- A proposta, specs e SDD originais foram aprovados e a implementação-base da FNR-005 foi aceita parcialmente. Esta revisão incremental do diagnóstico requer revisão específica do desenho antes de implementar ou usar a autorização já concedida para uma única coleta lógica. A autorização não cobre novas coletas, commits, PR, arquivamento ou avanço para outra FNR.
- A revisão D4 decorrente da conferência local é uma proposta documental pendente de aprovação. Nenhum comportamento do classificador, situação ou competência foi alterado; a evidência da natureza de um lançamento não é regra automática para outros lançamentos.
