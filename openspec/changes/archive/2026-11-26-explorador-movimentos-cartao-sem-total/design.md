# Design

## Context

Ver `proposal.md` para motivação e `specs/card-movement-explorer/spec.md` para o comportamento observável. A FNR-005 mantém `PAGAMENTO` como natureza desconhecida, `PAGAMENTO_FATURA` distinto, situações independentes e competência sem confirmação. A última coleta autorizada registrou 319 movimentos em uma página; valores individuais não foram retidos. O antigo resultado 319/319 `missing_or_invalid` para relações com `billForecastDate` é inconclusivo por limitação do parser e não pode ser recalculado retroativamente. O repositório ainda não contém a interface Angular/dashboard.

O incremento técnico foi integrado pelo [PR #12](https://github.com/VictorJer/GestFinan-a/pull/12), merge `adee6998342722e905a1db98ed1423620ce77ee4`, presente na main local. O titular aprovou a direção de substituir o objetivo pendente de FNR-005/5.3 no MVP por movimentos descritivos e recorrência assistida em mudança própria. O conteúdo documental revisado deste explorador foi aprovado como parte do gate FNR-005/7.1 em 16/11/2026; isso não aceita sua implementação ou encerramento. Reviews e checks reportados no PR consultado estavam vazios; a integração não comprova o atendimento técnico de 5.3.

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
- Não implementar detecção ou confirmação de recorrência nesta mudança; sua direção é registrada como dependência de planejamento própria, sem tolerâncias escolhidas implicitamente.

## Decisions

1. **Separar o explorador dos indicadores financeiros — revisão de escopo com histórico preservado.** A nova capability oferece apenas movimentos e estados identificáveis. A direção aprovada substitui o objetivo pendente de FNR-005/5.3 no MVP, sem declarar o critério original tecnicamente atendido. O gate de evidência permanece necessário para os indicadores financeiros futuros, não como bloqueio geral do contrato técnico conservador ou da listagem descritiva. Alternativas rejeitadas: marcar 5.3 concluída como se compra/competência estivessem validadas, ou transformar a FNR-005 em implementação de tela/detector. A primeira falsificaria o resultado; a segunda misturaria escopos e ignoraria a proposta de explorador já existente.

2. **Não agregar valores monetários nesta entrega.** Não calcular, retornar nem mostrar qualquer soma, subtotal, total bruto/líquido, média, agrupamento monetário, série ou gráfico financeiro, independentemente do rótulo ou da camada. Mostrar somente valor por movimento e contagens não monetárias de cobertura/natureza/situação, com universo e completude identificados. Alternativa considerada: exibir um subtotal identificado com aviso; rejeitada nesta fase por risco de destaque visual superar a ressalva.

3. **Usar somente a taxonomia conservadora aprovada.** `PAGAMENTO` continua desconhecido para qualquer direção/sinal; `PAGAMENTO_FATURA` fica separado; estornos só podem ser identificados conforme regra aprovada e não são abatidos sem vínculo confiável. Operações não reconhecidas aparecem como desconhecidas, sem reproduzir valores externos arbitrários.

4. **Separar natureza e situação na API e na apresentação.** Status como `PENDING` não determina natureza nem efetivação. Parcela futura exige a combinação validada de status, metadados e temporalidade; metadados ausentes não criam parcelas. Resultado incompleto da coleta continua visível como incompleto e não recebe rótulo de cobertura integral.

5. **Tratar data como data de origem, não competência.** Apresentar a data retornada e, se houver normalização técnica, sua regra e origem. Filtros/agrupamentos descritivos devem nomear a data usada. Não chamar esse mês de ciclo da fatura, competência, vencimento ou previsão. O diagnóstico corrigido de `billForecastDate` não muda essa decisão: sua comparação é estrutural, não semântica financeira.

6. **Preservar a responsabilidade de cálculo no backend.** A futura API deve fornecer movimentos, natureza/situação, cobertura e ausência/indisponibilidade; o dashboard não pode calcular somas financeiras no Angular. Se a interface vier a exibir contagens, elas devem se referir ao universo concluído ou declarar explicitamente a incompletude.

7. **Revisar os documentos canônicos antes de implementar.** PRD 0.4 (RF04–RF07/RF15, jornada, métricas, regras e definição de pronto), TDD 0.7 (§§10–14/16/19 e referências afetadas) e backlog (encerramento revisado de FNR-005, fatias descritivas e dependências) foram aprovados documentalmente em 16/11/2026 no gate FNR-005/7.1, preservando versões/aprovações anteriores. Essa aprovação não autoriza implementação nem aceita o encerramento deste explorador; seu rastreamento próprio permanece separado.

8. **Separar incremento técnico integrado, objetivo substituído e investigação adiada.** As tarefas já registradas como concluídas da FNR-005 (incluindo análise sanitizada, paginação/limites, fixtures, parser diagnóstico de `billForecastDate` e validações) permanecem evidência histórica do incremento integrado pelo PR #12. Preservar o texto e o resultado original de 5.3, registrando seu objetivo como **substituído por revisão de escopo — não atendido tecnicamente**, sem caixa de conclusão técnica. O encerramento revisado exige: identificação da entrega integrada e suas limitações; alinhamento e aprovação do conteúdo documental final; validações proporcionais e aceite explícito do resultado revisado. Implementar o explorador ou a recorrência não é requisito para encerrar o incremento técnico. As regras existentes permanecem conservadoras; nenhum resultado financeiro é liberado por essa substituição.

   | Fatia do backlog | Tratamento proposto com o contrato conservador | Condição/limite |
   |---|---|---|
   | FNR-007 — modelo | Pode modelar identidade, valor/moeda, texto/data originais, estado e natureza desconhecida, além de opcionais; pode avançar na integridade estrutural. | Não fechar competência nem escolher uma data normalizada como competência financeira. |
   | FNR-012 — importação | Pode importar e atualizar todos os movimentos retornados, com paginação, idempotência e retenção de casos desconhecidos. | Cobertura da consulta precisa ser verdadeira; não converter importação completa em classificação financeira completa. |
   | FNR-013/014 — execução/atualização | Podem tratar execução, completude, falhas, horários e atualização da origem, conforme suas dependências técnicas. | Não declarar sucesso financeiro a partir de sucesso de transporte/importação parcial. |
   | FNR-015 — consulta | Fornece a API protegida de movimentos, busca e filtros com data explicitamente identificada como data da origem, além de valor individual/moeda, contagens e cobertura. | Qualquer total monetário do conjunto filtrado fica adiado; o critério original de soma deve ser preservado como substituído, não como atendido. |
   | FNR-019/021 — base visual/conexão | Podem fornecer estrutura da interface, autenticação/navegação e estado/cobertura da conexão. | Não devem introduzir cartões de gasto ou valores agregados. |
   | FNR-022 — lista de movimentos | É responsável pela lista Angular e organização pessoal dos movimentos, com campos individuais, situação e natureza/motivo conservadores, incluindo `Unknown`, após suas dependências de API e sessão. | Categoria pessoal não confirma natureza de compra; a lista não deve ser reconstruída como entrega duplicada em FNR-023. |
   | FNR-018 — resumos descritivos | Fornece somente contagens não monetárias de movimentos, natureza, situação e cobertura, indicando universo e completude. | Indicadores financeiros originais ficam adiados, não satisfeitos por contagens; manter rastreabilidade para futura investigação. |
   | FNR-023 — visão descritiva | Reúne os resumos de FNR-018 e a navegação/lista já entregue em FNR-022, após sessão e demais dependências revisadas no backlog. | Sem somas, gráficos financeiros, quantidade de compras presumida ou evolução por competência; não duplica a implementação da lista. |
   | FNR-005/5.3 | Preserva o incremento técnico integrado pelo PR #12, o critério original não atendido e as limitações da amostra. | Registrar substituição por revisão de escopo e critérios revisados de encerramento; não reclassificar os 319 forecast buckets nem antecipar aceite final. |
   | Recorrência — item/mudança próprios | Planejar candidato após padrão em três meses consecutivos e confirmação pelo usuário como assinatura, parcela ou compras independentes; metadados de parcelamento da origem são tratados separadamente. | Depende de histórico persistido, consulta protegida e fluxo de confirmação. Tolerâncias de valor, datas e descrição devem ser definidas/aprovadas antes de implementar; valor igual sozinho não basta. Não é requisito de encerramento da FNR-005 ou de implementação deste explorador. |

   Este mapa orienta a revisão documental solicitada, não inicia os itens. As dependências formais e critérios de FNR-015/018/022/023 e do item próprio de recorrência devem constar no backlog revisado antes de implementação; não renumerar IDs existentes nem declarar entregues os objetivos antigos. A recorrência é um eixo independente: sua confirmação não altera automaticamente natureza financeira, status, valor/data originais ou competência. Detalhes e critérios executáveis do detector serão especificados na mudança própria, sem acrescentar outra capability a este explorador.

## Reconciliação posterior da composição visual

O contexto inicial acima descreve a situação de 16/11/2026; desde então, FNR-015 expôs a consulta protegida, FNR-018 o resumo descritivo, FNR-022 a lista única e FNR-023 a visão geral. A decisão aprovada para a tarefa 3.1 é compor essas superfícies, **sem duplicar a lista na visão geral**: `/area/visao` mostra contagens, cobertura, data técnica e filtros aplicados; o link para `/area/movimentos` preserva os filtros aplicados na sessão, onde aparecem valor e moeda individuais, data de origem, natureza e situação. A lista controla a edição dos filtros e a paginação; a visão geral consulta o universo filtrado sem `page/pageSize`. Os GETs são independentes e não prometem snapshot atômico se os dados mudarem entre as leituras.

Essa decisão refina a apresentação, não cria endpoint, modifica a taxonomia conservadora nem libera agregados monetários. A evidência local e suas limitações são confrontadas na matriz do [BACKLOG 0.84](../../../../docs/BACKLOG.md); documentação, tarefas e aceite do explorador permanecem próprios. A verificação em navegador cobre somente GETs/apresentação: o POST automático de atualização na entrada, responsabilidade do fluxo FNR-021, continua bloqueado pelo preflight CORS porque `Idempotency-Key` não está na allowlist API FNR-011; portanto, não se declara validada a integração completa pelo navegador. Não houve teste com leitor de tela, publicação ou serviço/provedor hospedado. As duas falhas PostgreSQL históricas de integridade/rollback seguem sem prova concluída e têm encaminhamento proposto separadamente à FNR-024.

## Risks / Trade-offs

- [A tela descritiva pode ser confundida com um dashboard de gastos] → Remover cartões de soma monetária, identificar a visão como movimentos disponíveis e destacar natureza desconhecida, cobertura e ausência dos indicadores.
- [Contagens parciais podem parecer universais] → Exibir página/estado de conclusão e a base observada; em falha, não usar contagens como resumo completo.
- [A lista pode mostrar datas com semântica não confirmada] → Rotular a data como retornada pela origem e não usar “competência” ou “mês da fatura”.
- [Usuário pode interpretar `Unknown` como erro ou zero] → Apresentar “natureza não identificada” como estado próprio e explicar que valor individual continua visível, mas não compõe gasto total.
- [Amostra real não representa cobertura histórica completa] → Preservar os dados agregados existentes e dizer que cobrem somente a consulta autorizada; nenhuma inferência de meses ausentes.

## Migration Plan

Não há migração de dados ou implantação autorizada nesta etapa documental. O conteúdo revisado desta mudança e de PRD/TDD/backlog foi aprovado no gate FNR-005/7.1 em 16/11/2026; o aceite do encerramento FNR-005 não encerra este explorador. Registrar a substituição do objetivo original de FNR-005/5.3 e os critérios revisados, preservando o histórico; implementação e aceite funcional do explorador permanecem próprios. A futura solução deve ser adicionada sem alterar retroativamente as contagens reais. Não executar consulta real, sync de specs, arquivamento, commits ou PR como efeito desta revisão.

## Open Questions

Para este explorador, os somatórios de gasto e competência permanecem fora do escopo até evidência e decisão próprias. As tolerâncias de valor, datas e descrição do detector de recorrência permanecem explicitamente pendentes e bloqueiam sua futura implementação; não bloqueiam o desenho do explorador porque o detector pertence a outra mudança. Nenhum valor de tolerância ou regra de correspondência é escolhido nesta revisão.
