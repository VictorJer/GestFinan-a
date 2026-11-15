# Design

## Context

Motivação em [proposal.md](proposal.md). Proposta, specs e SDD originais foram aprovados e a implementação-base da FNR-005 foi aceita parcialmente. O adendo D7 foi aprovado explicitamente pelo titular no harness em 15/11/2026; a autorização de uma coleta adicional será usada somente após implementar e testar o diagnóstico. Referências canônicas: PRD 0.3 e TDD 0.6; decisões não alteradas continuam herdadas.

A `main` contém FNR-004 integrada pelo PR #10 e encerramento pelo PR #11. O `Program.cs` distingue modo HTTP e `pluggy-read`; a query usa MediatR 12.5.0, o inspetor valida item/cartão e o leitor faz uma única consulta a `/v2/transactions`. Os DTOs de transação retêm somente `id`/`accountId`; há 58 testes aprovados historicamente, não reexecutados neste planejamento. O README e trechos introdutórios dos documentos ainda descrevem estados anteriores; o registro FNR-004 do backlog e o Git são a referência de entrega.

Este desenho é necessário por atravessar camadas, introduzir semântica financeira e envolver paginação autenticada e privacidade. Não pressupõe semântica já comprovada pelos 321 registros: a evidência anterior não contém valores nem datas.

## Goals / Non-Goals

**Goals:** produzir contrato mínimo reutilizável e regras puras, separar transporte de interpretação, medir uma leitura real limitada e documentar exatamente o que foi validado. As duas specs desta mudança definem o comportamento observável.

**Non-Goals:** entidades EF, banco, deduplicação persistente, reconciliação de reconexão, SyncRun, RabbitMQ/MassTransit, webhooks, endpoints financeiros, JWT, interface e agregação do dashboard. Não alterar `pluggy-local-read`, `api-diagnostics` ou `api-exploration`. Não prometer normalização de qualquer instituição nem disponibilidade de campos pagos.

## Decisions

### D1 — Caso de uso local separado

Adicionar `pluggy-analyze` como token único sem argumentos sensíveis, com query/handler na Application e contrato específico de análise implementado na Infrastructure. Reutilizar autenticação, inspeção do item e seleção do cartão; não chamar handlers encadeados nem adicionar behavior. Composição permanece na Api; análise não registra cliente Pluggy no caminho HTTP normal. `pluggy-read` mantém seus códigos e limite de uma página.

Alternativa rejeitada: ampliar silenciosamente `pluggy-read`, pois violaria sua spec consolidada e mudaria custo/tempo da PoC já aceita. Endpoint HTTP rejeitado por não haver autenticação implementada.

### D2 — Consulta paginada limitada, não importação

Referência de execução fixa em UTC; início de consulta é o dia de calendário de referência menos 12 meses (`AddMonths(-12)`), via `dateFrom`. Não enviar `dateTo` nesta análise para não descartar parcelas futuras disponíveis; separar as datas posteriores à referência na interpretação. O contrato externo tem limites inclusivos; isso não altera o futuro contrato HTTP interno de fim exclusivo do TDD §11.

Percorrer `next` até nulo. Limites propostos e testáveis: 100 páginas, 30 segundos por chamada e 120 segundos totais, sem retry automático. Limite atingido é coleta incompleta, não sucesso; ampliar orçamento exige revisão explícita antes de nova execução. Registrar contagens por página, recebidos/IDs únicos, duplicatas e duração total com relógio monotônico. Não manter lista de respostas brutas: processar página a página e reter apenas identidades em memória para contagem/diagnóstico, estatísticas e objetos mínimos necessários. Identidades nunca chegam ao resumo.

`next` é query pronta, não URL a seguir livremente. Exigir forma relativa iniciada em `?`, associada ao caminho fixo `/v2/transactions`; validar conta e filtros contra a requisição original, parâmetros permitidos e ausência de duplicações conflitantes. Não decodificar/recriar o cursor opaco para transporte; após validar o envelope, anexar a query original ao caminho fixo. Rejeitar origem/path arbitrários, fragmentos e cursor repetido. Redirect continua desabilitado. Se a API alterar/omitir filtros na continuação, interromper e registrar divergência contratual em vez de adivinhar a intenção.

Alternativas: buscar apenas uma página não valida paginação; coleta sem teto não limita custo; baixar tudo e persistir anteciparia FNR-012. Repetição de ID é anomalia, não fusão por conteúdo: dados contraditórios para o mesmo ID invalidam uma conclusão consistente, sem escolher silenciosamente uma versão.

### D3 — Transporte mínimo e regras puras

Manter os modelos atuais da PoC ou isolá-los dos novos DTOs para que ampliar campos obrigatórios não quebre FNR-004. Na Infrastructure mapear explicitamente os campos necessários para contrato neutro da Application; o Domain recebe dados puros e devolve natureza, situação, competência, validade e motivo de elegibilidade. Domain continua sem HTTP, JSON, MediatR ou referências aos outros projetos.

Campos mínimos da análise: ID/conta, `description`, `amount` como decimal, `currencyCode`, `date`, `type`, `status`, `operationType` e metadados opcionais de parcela (`installmentNumber`, `totalInstallments`, `purchaseDate`, `billId`, `billForecastDate`/`paymentType` quando disponíveis). Campo ausente difere de zero; enums novos não devem causar compra por fallback. Examinar disponibilidade de limite/fatura nos dados de conta já consultados, sem acrescentar endpoints de faturas neste incremento. Não desserializar `paymentData`, identidade de participantes, merchant completo ou `cardNumber` para o contrato: não são necessários à validação.

Precisão decimal sem arredondamento intermediário; valor original e moeda preservados. Nunca usar `totalAmount` como valor da parcela ou converter `amountInAccountCurrency` para somar moeda estrangeira em BRL. Esta análise não persiste dados, mas documenta limites para `numeric(19,4)` proposto em DT-40; overflow ou precisão incompatível gera diagnóstico, não truncamento.

Alternativa rejeitada: expor resposta externa ou usar modelos EF como contrato, pois acoplaria regras à origem e anteciparia persistência.

### D7 — Diagnóstico agregado expandido, sem reinterpretar os lançamentos

O adendo amplia somente a evidência sanitizada de `pluggy-analyze`. Nenhum bucket diagnóstico altera `TransactionNatureClassifier`, situação, elegibilidade, a flag positiva de compra (que continua desligada) ou a hipótese temporal (que continua sem confirmação financeira). As chaves do resumo são categorias internas fixas; valores não reconhecidos nunca são formatados, logados ou usados como nomes de chaves.

Buckets previstos:

- `operationType` separado para cada código documentado de cartão (`PAGAMENTO`, `PAGAMENTO_FATURA`, `ESTORNO`, `TARIFA`, `CASHBACK`, `OPERACOES_CREDITO_CONTRATADAS_CARTAO`, `OUTROS`), além de ausente e `other`; cruzamento por operação × direção (`DEBIT`/`CREDIT`/outro) × sinal (positivo/negativo/zero) × status (`POSTED`/`PENDING`/outro).
- Status × presença não vazia de `billId` (`present` ou `absent_or_null`); nenhum conteúdo do ID é mantido na contagem.
- `paymentType` em categorias internas correspondentes a `SINGLE`, `INSTALLMENT`, `A_VISTA`, `A_PRAZO`, ausente e `other`. Os dois pares documentais são medidos separadamente para evidenciar a discrepância sem escolher silenciosamente um contrato para esta conexão.
- Metadados de parcela em `absent` (ambos os números ausentes), `partial` (somente um presente), `complete_valid` (ambos positivos e número atual não maior que o total) ou `invalid` (valor não positivo ou número atual maior que o total), cruzados com os buckets de `paymentType`. A presença de `purchaseDate` e a validade de cada data também ficam em buckets fixos; ausência opcional não é falha de contrato.
- Relações pareadas de `date`, `purchaseDate`, `billPostDate` e `billForecastDate`: as três primeiras combinações de granularidade diária (`date`/`purchaseDate`, `date`/`billPostDate`, `purchaseDate`/`billPostDate`) contam `same_day`, `different_day` ou `missing_or_invalid`; combinações com `billForecastDate` (`date`, `purchaseDate` e `billPostDate` contra previsão) contam `same_month`, `before_forecast`, `after_forecast` ou `missing_or_invalid`. `billForecastDate` aceita o período estrito `yyyy-MM` e os formatos de data já aceitos pelo normalizador (`yyyy-MM-dd` e instante com offset explícito); períodos são comparados diretamente por ano/mês, sem fabricar dia ou horário. Instantes usam a conversão diagnóstica já implementada para `America/Sao_Paulo`, e datas de calendário permanecem sem conversão. Validade do forecast distingue `missing` (campo nulo/ausente), `invalid` (texto presente não aceito) e `valid`. São comparações de formato/calendário apenas: não declaram qual campo é compra, lançamento ou competência.

Cada bucket e cruzamento terá dois contadores separados: **received** incrementa a cada ocorrência recebida; **unique** incrementa somente na primeira ocorrência de cada ID. Repetições idênticas ficam em `received`, não duplicam `unique`; contradição para o mesmo ID mantém a coleta incompleta conforme D2/D6. Um resultado parcial mantém `pagination_completed=false` e não é apresentado como amostra completa.

A implementação processa e descarta cada payload como hoje, retendo somente estruturas internas de contagem e a identidade/fingerprint temporária já necessária à deduplicação. A allowlist de saída é atualizada explicitamente e testes sentinela cobrem enums desconhecidos, IDs, valores, descrições, cursores, URLs e textos/valores de data; apenas rótulos internos e inteiros agregados podem aparecer. Não são adicionados endpoints, persistência, retries nem alterações a limites: 100 páginas, 30 s por chamada e 120 s totais.

Execução: após a revisão explícita deste adendo, consumir uma única autorização existente para uma coleta lógica, seguindo páginas até `next=null` dentro do orçamento; nenhuma repetição automática nem segunda execução. Depois, entregar roteiro de conferência exclusivamente local ao titular e aguardar confirmação. Sem essa conferência, 5.3 permanece aberta; nenhuma contagem autoriza ampliar classificação ou declarar competência.

### D4 — Matriz financeira conservadora com gate de evidência (hipótese original em revisão)

As regras são o desenho conservador aprovado; a confirmação local citada abaixo é evidência de um caso, não descoberta de regra geral:

| Evidência | Natureza / tratamento |
|---|---|
| `PAGAMENTO_FATURA`, coerente com tipo/sinal documentados | Pagamento de fatura; nunca gasto nem estorno |
| `PAGAMENTO` | Ambíguo nesta conexão; natureza desconhecida até condição discriminatória validada, sem promoção automática a compra ou pagamento de fatura |
| `ESTORNO`, coerente com tipo/sinal | Estorno; abatimento somente com vínculo confiável, não inferido por descrição/data/valor |
| `TARIFA`, metadado de encargo confiável | Encargo separado; não compra |
| `CASHBACK` ou operação de crédito contratado | Ajuste/outra operação identificada; fora de compras |
| Regra de compra sustentada por contrato, direção DEBIT e sinal coerente no cartão | Compra; efetivação ainda depende da situação |
| Ausência, `OUTROS`, operação nova ou combinação contraditória sem regra validada | Desconhecido com motivo; não compra por sinal isolado |

Não usar expressões de descrição como classificador padrão. A documentação atual enumera operações especiais, mas não garante um código universal de compra: a regra positiva deve ser fechada por evidência documental e amostra local na tarefa de validação. Até lá, o fallback é desconhecido; se a amostra não sustentar distinguir compras, isso bloqueia declarar normalização validada e exige atualizar/revisar os artefatos antes de ampliar a regra. Ausência de vínculo de estorno é limitação explícita para FNR-018, não permissão de abatimento automático.

#### Revisão de D4 aprovada e aplicada de forma conservadora

O titular confirmou localmente a natureza de compra de um lançamento que, no trecho da interface do Dashboard Pluggy, apresentava `operationType=PAGAMENTO`, `type=DEBIT`, `amount` positivo e `status=PENDING`; o caso correspondia a uma parcela de compra no aplicativo. `paymentType` e metadados estruturados de parcela não estavam visíveis nesse trecho. A confirmação é evidência sobre **um caso**, não regra geral nem prova de que todos os `PAGAMENTO` são compras.

Proposta revisada: deixar de tratar `PAGAMENTO` e `PAGAMENTO_FATURA` como equivalentes na regra de natureza. `PAGAMENTO_FATURA` permanece candidato a pagamento de fatura sob coerência dos demais campos; `PAGAMENTO` passa a operação ambígua, mantendo natureza `Unknown` até existir uma condição discriminatória validada para compra ou pagamento. A combinação confirmada acima permanece desconhecida/não elegível no classificador automático enquanto tal condição não existir. A confirmação do titular pode ser registrada como natureza do caso conferido, separada do resultado automático.

Não usar descrição, ausência de metadados, `paymentType` ausente ou texto de parcela (incluindo padrão como `12/12`) como fallback. `PENDING` continua definindo situação pendente, independente de natureza; não efetivar a compra, não inferir competência pelo mês da fatura e manter a regra positiva/gate de compra desligado. Fixture documental fictícia e comparação offline de DTO/endpoints estão em `docs/PLUGGY-CONTRACT.md`.

O titular aprovou a revisão documental de D4 e da spec `transaction-normalization` no harness em 15/11/2026. O classificador trata `PAGAMENTO` como ambíguo e retorna natureza desconhecida para qualquer direção/sinal; `PAGAMENTO_FATURA` permanece uma operação distinta que pode ser pagamento de fatura quando coerente. Isso não classifica o caso confirmado (nem outros `PAGAMENTO`) como compra. Situação `PENDING` continua pendente, a regra positiva de compra continua desligada e competência não é inferida. O Dashboard Pluggy é uma interface externa: o DTO do `pluggy-read` explica apenas a saída desse comando e não explica campos ausentes no Dashboard; a causa no Dashboard fica desconhecida.

Situação é dimensão independente: `POSTED` válido e não contraditório pode ser efetivado; `PENDING` não é efetivado. Parcela futura exige metadado de parcela e evidência temporal coerente (data de lançamento/ciclo futuro documentado); apenas `PENDING` ou `purchaseDate` não bastam. Incoerência entre status e temporalidade permanece desconhecida, sem criação artificial das demais parcelas. Números de parcela devem ser positivos e não exceder o total quando ambos existirem.

### D5 — Datas preservadas, hipótese testável, não fuso implícito

Preservar string/semântica original do campo selecionado e valor temporal tipado. Datas sem horário são calendário, sem conversão. Para instantes explicitamente UTC/com offset, a proposta inicial de competência é o dia em `America/Sao_Paulo`, com teste de disponibilidade do timezone no ambiente suportado; não fixar `-03:00` para todo histórico. `purchaseDate`, `date` e data de fatura não são intercambiáveis. A documentação do produto chama `date` de lançamento UTC, mas a de parcelas alerta que pode ser compra ou lançamento conforme o banco.

Consequência: a primeira análise compara regras em memória e registra apenas indicadores de coerência, não datas individuais. Sem confirmação da semântica da conexão, competência recebe limitação e registros ambíguos não são elegíveis. Divergência que exija outra regra obriga atualizar este desenho/specs afetadas e revisar antes de continuar; não fechar política silenciosamente durante implementação. Alternativa rejeitada: truncar toda string ISO antes do fuso ou converter data de calendário como instante, pois deslocaria limites de mês.

### D6 — Evidência, privacidade e encerramento do contrato

Saída allowlist: estágio, resultado, contagens, duração, paginação terminada, campos presentes/ausentes, contagens por enum interno e motivos fixos de exclusão. Não imprimir enum externo arbitrário, descrições, valores individuais, IDs, cursores ou datas de transação. Cobertura pública apenas por meses/indicador agregado autorizado; se isso não bastar, manter como indeterminada, sem exportar amostra. Não calcular gastos reais no console.

Usar códigos da família existente: 0 análise terminada com registros (não significa validação financeira completa); 1 falha operacional/contrato/limite; 2 configuração/argumentos/seleção inválidos; 3 amostra vazia inconclusiva; 4 conexão bloqueada; 130 cancelamento. Resumo distingue coleta terminada e semântica validada/limitada. Falha não imprime exceção/corpo nem números financeiros parciais como sucesso.

`docs/PLUGGY-CONTRACT.md` será o catálogo técnico de campos e matriz de regras, não outro PRD/TDD: cada linha registra fonte, significado, observação real (sim/não), fixture fictícia, limitação e consequência para FNR-007/012/013/014/018. Casos reais não observados permanecem assim; não exigir criar compras/estornos no banco nem contratar enriquecimento. Documentar duração/volume e recomendação sobre coleta em memória versus staging para FNR-012/013; não reabrir RabbitMQ/MassTransit já escolhidos.

Autorização de planejamento/aplicação não autoriza executar a consulta com credenciais do titular. Pedir autorização específica da prova real após explicar orçamento e saída, sem ler valores secretos por ferramentas. Testes usam handlers HTTP e fixtures sintéticas; jamais copiar payloads pessoais para arquivos ou snapshots. Comentários dos blocos de código e testes serão em português conforme AGENTS.md.

## Risks / Trade-offs

- [Não encontrar estornos/parcelas ou semântica confiável] --> Registrar não observado, manter fallback desconhecido e bloquear critérios dependentes não demonstrados; testes fictícios não substituem evidência real.
- [Uma página real não exercitar continuação] --> Testar múltiplas páginas simuladas e registrar que paginação real múltipla não foi observada, sem afirmar que ocorreu.
- [Consulta de 12 meses não garantir cobertura] --> Distinguir filtro solicitado, meses observados e histórico bancário indeterminado; vazio não equivale a zero gasto.
- [Teto interromper volume maior] --> Terminar incompleto e revisar orçamento antes de repetir; não aumentar limites automaticamente.
- [Campo de parcela/fatura/limite ou enriquecimento pago ausente] --> Modelar como opcional e não disponível; não depender de categoria paga ou adicionar contratação.
- [Logging HTTP ou exceções vazarem finanças] --> Preservar supressão de logs do cliente e ampliar testes sentinela de stdout/stderr/logs em sucesso, falha e cancelamento.
- [Normalizador conservador excluir muitos lançamentos] --> Aceitar exclusão explícita em vez de totais falsos; validação bloqueia etapas dependentes até revisão da regra, sem ampliar silenciosamente escopo.
- [Cruzamentos gerarem células raras ou interpretação indevida] --> Exibir somente contagens em categorias fixas, separar recebidos/únicos, declarar os eixos independentes e impedir uso dos diagnósticos para reclassificar ou calcular competência.

## Migration Plan

Sem migration, deploy ou recursos externos novos. Após aprovação separada dos artefatos, implementar somente FNR-005 na feature branch; validar restore locked/build/testes/privacidade e executar a prova real somente com autorização específica. Atualizar documentos de evidência e revisão do TDD apenas quando necessário, preservando aprovações históricas. Revisão de conteúdo e autorização própria precedem commits/PR. Sync/arquivo somente após validação e aceite; não iniciar FNR-006 ou seguintes automaticamente.

Rollback aplicável: retirar o novo caminho local por uma mudança revisada sem alterar o comportamento HTTP/PoC; não há dados persistidos a reverter. Não usar descarte de trabalho ou reset Git.

## Open Questions

- Quais campos/naturezas e meses a conexão realmente retornará? A consulta posterior responde sem mudar o fallback conservador; ausência será registrada como limitação.
- Qual volume/duração observados recomendarão memória ou staging na futura importação? A recomendação será documentada para FNR-012/013, sem implementar esses mecanismos aqui.

## Fontes consultadas

Consulta documental via Context7 em 15/11/2026; fonte oficial não é prova de comportamento da conexão do titular:

- https://docs.pluggy.ai/en/reference/transaction/transactions-list-by-cursor — `dateFrom`/`dateTo` inclusivos, `next` como query pronta, até 500 registros por página v2; não usar `pageSize` do endpoint antigo.
- https://docs.pluggy.ai/en/docs/reference/basic-concepts.md — cursor opaco e preservação de filtros.
- https://docs.pluggy.ai/en/docs/products/transactions — sinal em cartão, status e `operationType`, metadados opcionais.
- https://docs.pluggy.ai/en/reference/transaction/transactions-retrieve — tipos, metadados e variações de campo.
- https://docs.pluggy.ai/en/docs/products/credit-card-installments — diferença entre valor da parcela/total e variação da semântica de data por instituição.
- https://docs.pluggy.ai/en/changelog — registra `operationType` informado pela instituição ou inferido pela Pluggy, e valores históricos de `paymentType`; a referência atual e o changelog são preservados como fontes distintas no diagnóstico.
