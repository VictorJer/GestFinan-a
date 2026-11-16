# Proposal

## Why

A FNR-004 comprovou a leitura de uma página real (321 registros), mas seu DTO conserva somente IDs e não valida semântica financeira ou cobertura histórica. A FNR-005 estabeleceu um incremento de contrato técnico conservador para orientar a persistência FNR-007 e a importação FNR-012 sem confundir pagamentos, estornos e pendências com compras efetivadas; seu objetivo original de validar regra positiva de compra/competência não foi tecnicamente atendido. A entrega técnica e as evidências são preservadas na revisão de escopo solicitada, não transformadas em prova de validação financeira.

**Revisão de encerramento — aceita pelo titular em 16/11/2026:** o incremento técnico foi integrado pelo [PR #12](https://github.com/VictorJer/GestFinan-a/pull/12), merge `adee6998342722e905a1db98ed1423620ce77ee4`. O titular aprovou o conteúdo documental final e aceitou o encerramento técnico conservador, preservando o objetivo original 5.3 como substituído por revisão de escopo e não atendido tecnicamente. As decisões e aprovações anteriores continuam restritas aos seus escopos; nenhuma funcionalidade de tela ou detector é incorporada à FNR-005, e compra/competência não são declaradas validadas.

## What Changes

- Acrescentar uma análise local explícita do contrato, com paginação cursor-based limitada, medição de volume/duração e cobertura informada sem alegar histórico bancário completo.
- Definir contratos mínimos de transação e normalização explícita de dinheiro, moeda, datas, natureza, situação e metadados opcionais, sem armazenar respostas brutas.
- Criar regras conservadoras e exemplos inteiramente fictícios para compras, estornos, pagamentos de fatura, encargos, pendências, parcelas futuras e natureza desconhecida.
- Registrar documentação do contrato com separação entre documentação oficial, observação real, hipótese e limitação; validar a leitura real somente na execução posterior autorizada pelo titular.
- Ampliar o diagnóstico com agregados fixos por código conhecido de `operationType`, cruzamentos com direção/sinal/status, presença de `billId`, distribuição de `paymentType`, consistência de parcelas e relações entre campos de data, sempre separados entre recebidos e únicos e sem conteúdo individual.
- Revisar a hipótese D4 após conferência local sanitizada do titular: `PAGAMENTO` pode representar compra nesta conexão e não deve ser tratado exclusivamente como pagamento de fatura; distinguir natureza confirmada de um caso de qualquer regra automática e manter o gate de compra desligado enquanto não houver predicado discriminatório validado.
- Preservar `pluggy-read`, `/health` e Swagger existentes. Não implementar banco, sincronização persistente, mensageria, login, APIs financeiras, dashboard ou publicação.
- Preservar os itens técnicos entregues e as evidências históricas. Registrar o objetivo original de 5.3 como **substituído por revisão de escopo — não atendido tecnicamente**, mantendo seu critério/resultados sem marcar conclusão técnica. A classificação automática de compras e as métricas dependentes dela ou de competência confirmada ficam adiadas; as regras conservadoras existentes não são ampliadas.
- Encaminhar movimentos descritivos para a [proposta existente do explorador](../../explorador-movimentos-cartao-sem-total/proposal.md), sem especificação concorrente. Encaminhar recorrência assistida para FNR-038/mudança própria: candidato após padrão em três meses consecutivos, confirmação pelo titular e metadados de parcelamento separados; valor igual sozinho não basta e tolerâncias de valor, datas e descrição permanecem pendentes antes de implementar.
- Definir o aceite revisado do incremento técnico: identificar PR #12 e limitações; preservar o critério original de 5.3 como substituído e não atendido; alinhar/aprovar documentos e artefatos afetados; validar proporcionalmente documentos/links/diff e obter aceite explícito do resultado revisado. Não depende de implementar explorador/recorrência, ampliar a regra de compra ou realizar nova coleta. Nenhum encerramento é declarado nesta etapa.

## Capabilities

### New Capabilities

- `pluggy-contract-analysis`: análise local paginada, limitada e sanitizada do contrato e da cobertura disponível na origem.
- `transaction-normalization`: interpretação explícita e conservadora de lançamentos do cartão, com desconhecidos e motivos preservados, independente de HTTP/persistência; não declara regra positiva de compra ou competência validadas.

### Modified Capabilities

Nenhuma. `pluggy-local-read` permanece restrita à prova de uma página; a análise é outro comando, sem alterar seu contrato.

## Impact

- Rastreabilidade histórica: PRD 0.3 e TDD 0.6 aprovados; evidências e escopos anteriores preservados. Alinhamento atual aprovado: [backlog FNR-005, revisão 0.2](../../../../docs/BACKLOG.md), critérios §5; [PRD 0.4](../../../../docs/PRD.md), RF02/RF04–RF07/RF15/RF17, §§8/13 e VP-02/03/04/07; [TDD 0.7](../../../../docs/TDD.md), §§7/10–14/16/19 e DT-09/27/34/38/40/42/43/48/49/54/55. Referências à recorrência delimitam trabalho próprio, não uma terceira capability desta mudança.
- Incremento já integrado: `backend/Finora.Api` (comando/composição), `Finora.Application` (query/handler/contratos), `Finora.Infrastructure/Pluggy` (DTOs/transporte paginado), `Finora.Domain` (regras puras) e testes MSTest existentes. Esta revisão não altera código, pacotes, locks, baseline ou o comportamento entregue.
- Preservar o catálogo `docs/PLUGGY-CONTRACT.md`, evidências de `docs/AMBIENTE.md` e instruções históricas do README, sem reescrever coletas ou apresentar novos resultados. Esta etapa alinha somente os documentos canônicos e artefatos existentes autorizados; seus históricos datados não são uma nova coleta ou aprovação final. Não sincronizar specs consolidadas nem arquivar nesta revisão.
- A proposta, specs e SDD originais foram aprovados e a implementação-base foi aceita parcialmente; D7 e a revisão conservadora D4 foram posteriormente aprovados e entregues no PR #12. `PAGAMENTO` agora permanece desconhecido, separado de `PAGAMENTO_FATURA`; a regra positiva continua desligada e competência não confirmada. A evidência da natureza de um lançamento não é regra automática para outros.
- As autorizações históricas de coleta foram consumidas nas respectivas execuções, não renovadas por este pedido. O conteúdo documental foi aprovado e o encerramento técnico conservador foi aceito explicitamente em 16/11/2026; isso não altera o resultado de 5.3 nem as pendências financeiras. Não realizar consulta real, implementação, instalação, commits, novo PR, merge, publicação ou arquivamento. O PR #12 integrado reporta reviews/checks vazios, sem descrever ausência como aprovação formal.
