# Design

## Context

Ver `proposal.md` para motivação e `specs/card-movement-explorer/spec.md` para o comportamento observável. A FNR-005 mantém `PAGAMENTO` como natureza desconhecida, `PAGAMENTO_FATURA` distinto, situações independentes e competência sem confirmação. A última coleta autorizada registrou 319 movimentos em uma página; valores individuais não foram retidos. O antigo resultado 319/319 `missing_or_invalid` para relações com `billForecastDate` é inconclusivo por limitação do parser e não pode ser recalculado retroativamente. O repositório ainda não contém a interface Angular/dashboard.

## Goals / Non-Goals

**Goals:**
- Oferecer uma leitura descritiva da amostra disponível e seus limites, sem depender de classificar todos os movimentos como compra.
- Mostrar natureza conhecida, situação, valor individual, moeda, data de origem e estado de cobertura sem misturar esses eixos.
- Deixar explícito que nenhum total de gasto está validado.

**Non-Goals:**
- Não resolver a regra automática de compra nem a semântica de competência da FNR-005/5.3.
- Não calcular soma bruta/líquida, quantidade total de compras, ticket médio, distribuição monetária por categoria ou evolução mensal de gastos.
- Não tratar unknown, ausência, consulta vazia ou moeda não elegível como zero; não usar dados fictícios como fallback.
- Não fazer nova coleta, reclassificar os 319 registros históricos, alterar o classificador da FNR-005 ou implementar nesta proposta.

## Decisions

1. **Separar o explorador dos indicadores financeiros — proposta de escopo distinta.** A FNR-005 aprovada conserva o gate de evidência para normalização financeira; a nova capability oferece apenas movimentos e estados identificáveis. Alternativa considerada: reescrever o objetivo e fechar 5.3 como se a limitação já fosse critério atendido. Rejeitada porque mudaria o escopo aprovado e poderia ser interpretada como validação de compra/competência que não ocorreu.

2. **Não agregar valores monetários nesta entrega.** Não calcular, retornar nem mostrar qualquer soma, subtotal, total bruto/líquido, média, agrupamento monetário, série ou gráfico financeiro, independentemente do rótulo ou da camada. Mostrar somente valor por movimento e contagens não monetárias de cobertura/natureza/situação, com universo e completude identificados. Alternativa considerada: exibir um subtotal identificado com aviso; rejeitada nesta fase por risco de destaque visual superar a ressalva.

3. **Usar somente a taxonomia conservadora aprovada.** `PAGAMENTO` continua desconhecido para qualquer direção/sinal; `PAGAMENTO_FATURA` fica separado; estornos só podem ser identificados conforme regra aprovada e não são abatidos sem vínculo confiável. Operações não reconhecidas aparecem como desconhecidas, sem reproduzir valores externos arbitrários.

4. **Separar natureza e situação na API e na apresentação.** Status como `PENDING` não determina natureza nem efetivação. Parcela futura exige a combinação validada de status, metadados e temporalidade; metadados ausentes não criam parcelas. Resultado incompleto da coleta continua visível como incompleto e não recebe rótulo de cobertura integral.

5. **Tratar data como data de origem, não competência.** Apresentar a data retornada e, se houver normalização técnica, sua regra e origem. Filtros/agrupamentos descritivos devem nomear a data usada. Não chamar esse mês de ciclo da fatura, competência, vencimento ou previsão. O diagnóstico corrigido de `billForecastDate` não muda essa decisão: sua comparação é estrutural, não semântica financeira.

6. **Preservar a responsabilidade de cálculo no backend.** A futura API deve fornecer movimentos, natureza/situação, cobertura e ausência/indisponibilidade; o dashboard não pode calcular somas financeiras no Angular. Se a interface vier a exibir contagens, elas devem se referir ao universo concluído ou declarar explicitamente a incompletude.

7. **Revisar os documentos canônicos antes de implementar.** Se esta proposta for aceita, atualizar PRD (RF04/RF07, regras e definição de pronto), TDD (§§10–13 e §19) e backlog (FNR-018/FNR-023 e dependências), preservando versões/aprovações anteriores e marcando as novas revisões como pendentes de aprovação. FNR-005 e sua 5.3 não são fechadas por este trabalho.

8. **Separar incremento técnico validado de gate financeiro ainda bloqueado.** As tarefas 5.1–5.2, 5.4–5.15 e 6.1–6.3 da FNR-005, já registradas como concluídas (incluindo análise sanitizada, paginação/limites, fixtures, parser diagnóstico de `billForecastDate` e validações), permanecem evidência histórica do incremento; não substituem a tarefa 5.3, que fica aberta até haver regra discriminatória de compra e semântica financeira/temporal suficiente. Nenhum status de aceite é alterado por esta proposta.

   | Fatia do backlog | Tratamento proposto com o contrato conservador | Condição/limite |
   |---|---|---|
   | FNR-007 — modelo | Pode modelar identidade, valor/moeda, texto/data originais, estado e natureza desconhecida, além de opcionais; pode avançar na integridade estrutural. | Não fechar competência nem escolher uma data normalizada como competência financeira. |
   | FNR-012 — importação | Pode importar e atualizar todos os movimentos retornados, com paginação, idempotência e retenção de casos desconhecidos. | Cobertura da consulta precisa ser verdadeira; não converter importação completa em classificação financeira completa. |
   | FNR-013/014 — execução/atualização | Podem tratar execução, completude, falhas, horários e atualização da origem, conforme suas dependências técnicas. | Não declarar sucesso financeiro a partir de sucesso de transporte/importação parcial. |
   | FNR-015 — consulta | Pode avançar na lista, busca e filtros com data explicitamente identificada como data da origem, além de valor individual/moeda. | Qualquer total monetário do conjunto filtrado permanece bloqueado; a fatia não conclui o critério original de soma. |
   | FNR-019/021 — base visual/conexão | Podem fornecer estrutura da interface, autenticação/navegação e estado/cobertura da conexão. | Não devem introduzir cartões de gasto ou valores agregados. |
   | FNR-022 — lista de movimentos | Pode apresentar movimentos e campos individuais, situação e natureza/motivo conservadores, incluindo `Unknown`. | Edição/categorização apresentada como compra não pode ser inferida da natureza desconhecida. |
   | FNR-018 — indicadores | Pode ser decomposta para contagens não monetárias de movimentos, natureza, situação e cobertura, se o universo e a completude forem mostrados. | Cálculos de gastos, compras, categorias monetárias e séries financeiras ficam bloqueados. |
   | FNR-023 — dashboard | Pode apresentar a visão descritiva de movimentos após revisão explícita das dependências e da divisão de FNR-018. | Gráficos/somas de gasto, quantidade total de compras e evolução por competência continuam bloqueados. |
   | FNR-005/5.3 | Conserva o registro do incremento técnico validado e as limitações da amostra. | 5.3 continua aberta; não reclassificar os 319 forecast buckets, nem tratar a proposta como aceite da FNR-005. |

   Este mapa é uma proposta de dependências, não alteração do backlog aprovado. Dependências formais de FNR-018/023, os limites de conclusão das fatias e qualquer revisão de RF07 precisam de decisão/aprovação antes de editar documentos canônicos ou iniciar esses itens.

## Risks / Trade-offs

- [A tela descritiva pode ser confundida com um dashboard de gastos] → Remover cartões de soma monetária, identificar a visão como movimentos disponíveis e destacar natureza desconhecida, cobertura e ausência dos indicadores.
- [Contagens parciais podem parecer universais] → Exibir página/estado de conclusão e a base observada; em falha, não usar contagens como resumo completo.
- [A lista pode mostrar datas com semântica não confirmada] → Rotular a data como retornada pela origem e não usar “competência” ou “mês da fatura”.
- [Usuário pode interpretar `Unknown` como erro ou zero] → Apresentar “natureza não identificada” como estado próprio e explicar que valor individual continua visível, mas não compõe gasto total.
- [Amostra real não representa cobertura histórica completa] → Preservar os dados agregados existentes e dizer que cobrem somente a consulta autorizada; nenhuma inferência de meses ausentes.

## Migration Plan

Não há migração de dados ou implantação autorizada nesta etapa documental. Antes de implementação, obter aprovação desta mudança e das revisões pendentes de PRD/TDD/backlog; manter FNR-005/5.3 aberta. A futura solução deve ser adicionada sem alterar retroativamente as contagens reais já registradas.

## Open Questions

Nenhuma pergunta aberta altera a proposta: os somatórios de gasto e competência permanecem fora do escopo até evidência e decisão próprias.
