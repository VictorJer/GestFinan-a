# Catálogo do contrato Pluggy — FNR-005

**Estado:** documentação técnica em implementação. Coletas autorizadas observaram disponibilidade dos campos e distribuições agregadas de uma única página; isso não comprova histórico completo nem valida regras gerais. O titular confirmou localmente a natureza de compra de um único caso, sem compartilhar seus dados individuais; essa confirmação não é uma regra automática. Fixtures citadas são exclusivamente sintéticas.

## Objetivo e rastreabilidade

Este catálogo registra o contrato mínimo para validar a leitura e orientar as etapas futuras, sem persistir respostas brutas ou declarar histórico completo. Rastreia o PRD RF02 (janela de até 12 meses e preservação de parcelas futuras), RF07 (somente gastos elegíveis), RNF-01/02/05/09/10 (segredos, minimização, precisão, falhas e logs) e o TDD DT-09/27/34/38/40/42/43. O mapeamento é explícito em C#; segredos permanecem fora do repositório; Domain não depende de transporte; e qualquer decisão ainda não sustentada permanece limitação.

## Fontes oficiais consultadas

Consulta documental registrada em 15/11/2026. A documentação oficial descreve a API, mas não demonstra quais campos ou semânticas a conexão desta conta retorna.

- [List transactions by cursor](https://docs.pluggy.ai/en/reference/transaction/transactions-list-by-cursor): `dateFrom`/`dateTo` inclusivos, `next` como query de continuação e limite de até 500 resultados por página na API v2.
- [Basic concepts](https://docs.pluggy.ai/en/docs/reference/basic-concepts.md): cursor opaco e preservação dos filtros entre páginas.
- [Transactions](https://docs.pluggy.ai/en/docs/products/transactions): campos de transação, sinais, status, operação e metadados opcionais.
- [Retrieve transaction](https://docs.pluggy.ai/en/reference/transaction/transactions-retrieve): atributos e variações do contrato de transação.
- [Credit card installments](https://docs.pluggy.ai/en/docs/products/credit-card-installments): diferenças entre valor da parcela e total e variações institucionais da semântica de data.

## Catálogo de campos

| Campo | Uso no Finora | Documentado | Observado nesta conexão | Simulado | Limitação / consequência |
|---|---|---|---|---|---|
| `id` | Identidade externa opaca; não deduplicar por conteúdo. | Sim — API de transações | Sim — 319/319 presentes | Sim — fixtures sintéticas | Campo-base. Uma página não valida identidade ao longo de sincronizações; IDs não são exibidos em resumo. Contraditórios para o mesmo ID impedem escolher versão silenciosamente. |
| `accountId` | Verificar que o lançamento pertence ao cartão selecionado. | Sim — API de transações | Sim — 319/319 presentes | Sim | Campo-base. Conta divergente invalida a página. |
| `description` | Preservar a descrição original, sem usá-la como classificador. | Sim — API de transações | Sim — 319/319 presentes | Sim | Campo-base do contrato interno; não imprimir em logs/saída. |
| `amount` | Valor decimal original, sem arredondamento ou substituição por zero. | Sim — API de transações | Sim — 319/319 presentes | Sim — decimais exatos e overflow | Campo-base; observação de presença não valida escala/faixa de todos os casos. Valores fora de `decimal` são rejeitados. Não usar `totalAmount` como valor de parcela. |
| `currencyCode` | Preservar moeda; somente BRL poderá ser elegível para total em reais. | Sim — API de transações | Sim — 319/319 presentes | Sim — BRL e moeda estrangeira | Campo-base; a saída não distribuiu moedas. Não converter moedas nem usar `amountInAccountCurrency` como conversão implícita. |
| `date` | Preservar texto original e derivar data de transação conforme regra explícita. | Sim, com variação descrita por produto | Sim — 319/319 presentes; 319 classificados no bucket de instante | Sim — calendário e instantes UTC/offset | Formato observado, mas semântica por instituição ainda não confirmada. Não confundir com compra ou vencimento de fatura. |
| `type` | Evidência contratual adicional para classificar natureza/direção. | Sim — API de transações | Sim — 319/319 presentes; agregados: 245 `DEBIT`, 74 `CREDIT` | Sim — inclusive valor externo novo | Cruzamento observado: 245 `DEBIT`/positivo e 74 `CREDIT`/negativo. Coerência de sinal não identifica compra; enum desconhecido não pode virar compra por fallback. |
| `status` | Dimensão de situação independente da natureza. | Sim — API de transações | Sim — 319/319 presentes; 257 `POSTED`, 62 `PENDING` | Sim — `POSTED`, `PENDING` e desconhecido | Distribuição não confirma semântica institucional além dos rótulos observados. Status novo ou contraditório não é efetivado automaticamente. |
| `operationType` | Evidência explícita para compra, pagamento, estorno, tarifa ou ajuste. | Sim — operações documentadas pelo provedor | Sim — 319/319 presentes; grupos fixos: pagamento 202, ajuste 110, tarifa 7 | Sim — valores sintéticos | Nenhum grupo de estorno, operação ausente ou outra operação foi reportado. A coleta não sustentou regra automática geral de compra; o titular confirmou separadamente um caso `PAGAMENTO` como compra, sem predicado para generalizar. |
| `creditCardMetadata.installmentNumber`, `totalInstallments` | Metadados opcionais, sem geração artificial de parcelas. | Sim — metadados de cartão aninhados | Sim — 78/319 presentes para cada campo | Sim — presentes, ausentes e incoerentes | Ausência é distinta de zero; observação de presença não valida numeração nem cobertura de parcelas. |
| `creditCardMetadata.purchaseDate` | Data de compra opcional, distinta da data de lançamento. | Sim — documentação de parcelas | Sim — 78/319 presentes | Sim | Semântica depende da instituição; não substituir `date`. |
| `creditCardMetadata.billId`, `billPostDate`, `billForecastDate`, `paymentType` | Contexto opcional de fatura e parcelamento, sem efetivar por associação isolada. | Sim — metadados de cartão, sujeito à disponibilidade | `billId`: 257/319; demais: 319/319 | Sim | Presença não valida vínculo ou semântica. `billPostDate` e `billId` dependem da instituição/conector; não consultar endpoint extra nem inventar valor quando ausentes. |
| `next` | Continuação cursor-based, transportada sem reconstruir cursor opaco. | Sim — endpoint cursor-based | Sim — nulo após uma página | Sim — cursor final e paginação múltipla | Paginação múltipla não foi observada; aceitar somente query relativa validada para host/path/filtros fixos, nunca seguir URL arbitrária. |

Os campos não listados, incluindo `paymentData`, identidade completa de participantes, dados completos de merchant e `cardNumber`, não fazem parte do contrato permitido nesta validação.

## Filtros e limites da coleta proposta

- Referência temporal fixa em UTC no início da execução; `dateFrom` é a data de calendário da referência menos 12 meses (`AddMonths(-12)`).
- Não enviar `dateTo`, para não eliminar parcelas futuras retornadas. Filtros externos são inclusivos, conforme a documentação consultada; isso não define o futuro intervalo HTTP interno de fim exclusivo.
- Seguir `next` até `null`, preservando conta/filtros, com no máximo 100 páginas, 30 segundos por chamada e 120 segundos totais. Sem retry automático.
- Falha, cancelamento ou limite resulta em coleta incompleta. `next = null` e página vazia não comprovam doze meses de histórico bancário.
- O resumo contém apenas contagens, duração monotônica, término da paginação, quantidade de meses distintos com registros e disponibilidade agregada de campos. Meses vazios não são interpolados e essa quantidade não representa cobertura contínua. Não inclui IDs, cursores, valores individuais, descrições, datas de transação ou payload bruto.

## Matriz de interpretação — fixtures e observação real limitada

### Agregados da coleta real

Uma execução autorizada em 15/11/2026 recebeu 319 registros em uma página, com 319 IDs únicos, zero duplicatas, `next=null` e duração de 597 ms. A solicitação usou `dateFrom=2025-11-15`, sem `dateTo`; foram observados 24 meses distintos e 39 registros posteriores à referência temporal proposta. A contagem de meses não demonstra cobertura contínua ou histórica.

| Dimensão interna fixa | Contagens observadas | O que permite concluir / limite |
|---|---|---|
| Direção e sinal | `DEBIT` 245, `CREDIT` 74; positivo 245, negativo 74, zero 0; `DEBIT`/positivo 245, `CREDIT`/negativo 74 | Todos os pares observados são coerentes com o cruzamento proposto, mas direção/sinal não identificam compras. |
| Status | `POSTED` 257, `PENDING` 62, outro 0 | Ambos os rótulos existem na amostra; não prova que todos os estados da conexão foram cobertos. |
| Operação | pagamento 202, ajuste 110, tarifa 7; estorno, ausente e outra operação 0 | Não houve observação de estorno nem operação positiva de compra identificável. Valores externos foram agrupados e não são reproduzidos. |
| Natureza produzida pelo classificador conservador | pagamento 37, ajuste 37, tarifa 7, desconhecida 238; compra e estorno 0 | O classificador não reconheceu compra. A diferença entre os grupos de operação e naturezas classificadas demonstra que as regras propostas não explicam toda a amostra; não ampliar regras por sinal ou fixture. |
| Formato de `date` | instante 319; calendário, ambígua, inválida e timezone indisponível 0 | O formato processado foi instante com offset/UTC; isso não determina se a instituição representa compra ou lançamento. |

Categorias zero são omitidas pelo comando; as contagens acima refletem somente os buckets internos fixos permitidos. Não foram guardados valores, enums externos, descrições, IDs ou datas individuais. A coleta **não sustenta uma regra automática geral de compra nem valida competência temporal**. A confirmação local posterior estabelece a natureza de um caso, não libera o gate de compra nem fecha a tarefa 5.3.

| Fixture sintética | Regra proposta | Estado da evidência | Motivo / limitação |
|---|---|---|---|
| Compra BRL com evidência de compra, direção/sinal coerentes e `POSTED` | Elegível para gasto efetivado após validação da regra positiva. | Simulada; o classificador não reconheceu compra na coleta. O titular confirmou separadamente um caso `PAGAMENTO` como compra. | A confirmação de um caso não sustenta uma regra geral nem valida o classificador positivo. |
| Crédito identificado como pagamento de fatura | Pagamento, nunca compra ou estorno. | Simulada; real não observada | Não classificar só pelo sinal. |
| Operação identificada como estorno | Preservar natureza; abater apenas com vínculo confiável. | Simulada; real não observada | Sem vínculo, abatimento bloqueado. |
| Tarifa/encargo identificável | Natureza separada, fora das compras. | Simulada; real não observada | Não inferir pela descrição. |
| Cashback ou ajuste identificado | Ajuste/ outra operação, fora das compras. | Simulada; real não observada | Operação nova ou contraditória fica desconhecida. |
| `PENDING` sem metadados suficientes de parcela futura | Situação pendente, não efetivada. | Simulada; real não observada | Natureza permanece independente; pendência não significa parcela futura. |
| `PENDING`, `paymentType=INSTALLMENT`, numeração válida e `billForecastDate` futura | Situação futura separada, fora dos gastos efetivados. | Simulada; real não observada | Data de compra/fatura isolada, parcela sem total ou status `POSTED` contraditório não basta. |
| Compra em moeda diferente de BRL | Preservar valor/moeda, fora do total em reais. | Simulada; real não observada | Sem conversão cambial. |
| Tipo, status ou operação desconhecida/contraditória | Inválida ou desconhecida, sem elegibilidade automática. | Simulada; real não observada | Não usar fallback permissivo. |

Motivos fixos no Domain: `MissingOperationType` (operação ausente), `UnknownOperationType` (código novo, `OUTROS` ou operação conhecida sem natureza distinguível/validada, incluindo `PAGAMENTO` nesta conexão) e `ContradictoryTypeOrSign` (direção/tipo incompatível com a regra atual). Uma natureza especial reconhecida também fica desconhecida quando o sinal contradiz a regra. A descrição nunca é usada para classificar. Estes mapeamentos são propostas testadas com fixtures. Na coleta, o classificador anterior produziu 238 naturezas desconhecidas contra 202 operações no grupo de pagamento e 110 no grupo de ajuste, sem reconhecer compra. A confirmação local posterior estabelece que pelo menos um lançamento com `PAGAMENTO` era compra, mas não fornece um predicado para identificar automaticamente esse caso ou generalizar para outros; o gate de compra permanece fechado e a tarefa 5.3 bloqueada.

Para a situação, `POSTED` é efetivado pela situação explícita, não por data passada/fatura; `PENDING` permanece pendente salvo evidência coerente de parcela futura (modalidade `INSTALLMENT`, numeração válida e previsão posterior ao mês de referência). Status novo, numeração incoerente ou previsão futura contraditória produzem situação desconhecida. Nenhuma parcela é criada artificialmente.

A elegibilidade é calculada por transação e retorna um motivo fixo: campos-base ausentes/data inválida, `UnlinkedRefund`, `NotPurchase`, `NotEffective`, `FutureInstallment`, `UnknownStatus`, `UnsupportedCurrency` ou natureza desconhecida. Não há soma, conversão de moeda ou abatimento de estorno. O parâmetro de confirmação de regra positiva começa desligado; o teste com fixture sintética que o habilita não representa validação da conexão nem libera compras reais.

Para `date`, preserva-se sempre o texto original. `yyyy-MM-dd` é tratado como dia de calendário sem conversão; timestamps que declaram `Z` ou offset são convertidos para `America/Sao_Paulo` para derivar o dia. Horário sem offset, formato inválido ou fuso indisponível não recebe competência presumida. A disponibilidade do timezone é testada no ambiente local. Embora a documentação descreva `date` como UTC, a instituição pode atribuir significados diferentes aos campos de compra/lançamento; esta regra continua proposta até confronto com a amostra real autorizada.

## Cobertura, identidade e etapas seguintes

Recebidos e IDs únicos foram contados separadamente na amostra (319/319; zero duplicatas); isso não valida identidade entre execuções. Ocorrências repetidas são anomalias, não fusão por descrição/data/valor. Uma repetição contraditória para o mesmo ID impede conclusão consistente. A janela requisitada é distinta do histórico efetivamente observado; meses sem registros e cobertura bancária indisponível permanecem indeterminados. A coleta terminou em uma página, sem exercitar paginação real múltipla.

Para esta consulta limitada, 319 registros/597 ms em uma página não justificam staging: manter leitura transitória em memória, processada página a página, é proporcional ao escopo diagnóstico. Isso não dimensiona importação de histórico completo nem execução persistente; FNR-012/013 devem reavaliar staging com volume/carga representativos antes de decidir. FNR-007/012/013/014/018 não devem assumir histórico completo, abatimento de estorno sem vínculo ou política de retry baseada em limite real não confirmado. A observação não alterou decisões do TDD.

### Distribuições agregadas permitidas

Para confrontar o contrato sem divulgar enums externos arbitrários, o comando agrupa a amostra em buckets internos fixos: direção (`DEBIT`, `CREDIT`, outro), sinal (positivo, negativo, zero) e cruzamento direção/sinal; status (`POSTED`, `PENDING`, outro); operação (pagamento de fatura, estorno, tarifa, ajuste, ausente, outra); natureza classificada coerentemente (pagamento, estorno, tarifa, ajuste, compra, desconhecida); e formato de data (calendário, instante com offset, ambígua, inválida ou timezone indisponível). Apenas contagens por esses rótulos fixos são elegíveis para saída. O bucket `nature_unknown` não revela se um código externo desconhecido é um valor específico, e nenhuma dessas contagens isoladas prova uma compra positiva.

As distribuições acima foram obtidas na consulta adicional explicitamente autorizada em 15/11/2026. A coleta continua limitada a uma página e a seus agregados fixos; não inferir ou preencher a matriz com as fixtures sintéticas.

### Adendo D7 — buckets adicionais observados

Após aprovação do desenho, uma única execução lógica adicional em 15/11/2026 percorreu uma página até `next=null`: 319 recebidos/únicos, zero duplicatas, 527 ms. A tabela abaixo registra somente contagens agregadas allowlisted. Buckets não exibidos tiveram zero ocorrências; nenhum enum externo inesperado é reproduzido.

| Eixo | Buckets observados (recebidos/únicos) | Limite da observação |
|---|---|---|
| `operationType` conhecido | `PAGAMENTO` 165/165; `PAGAMENTO_FATURA` 37/37; `TARIFA` 7/7; `OPERACOES_CREDITO_CONTRATADAS_CARTAO` 110/110 | Não houve ocorrência reportada de código ausente, desconhecido, `ESTORNO`, `CASHBACK` ou `OUTROS`; operação não define compra. |
| Operação × direção × sinal × status | `PAGAMENTO`/`DEBIT`/positivo: `POSTED` 115/115, `PENDING` 50/50; `PAGAMENTO_FATURA`/`CREDIT`/negativo: `POSTED` 35/35, `PENDING` 2/2; `TARIFA`/`DEBIT`/positivo/`POSTED` 7/7; `OPERACOES_CREDITO_CONTRATADAS_CARTAO`/`DEBIT`/positivo: `POSTED` 66/66, `PENDING` 7/7; mesma operação/`CREDIT`/negativo: `POSTED` 34/34, `PENDING` 3/3 | Cruzamento descritivo. Não muda a natureza/classificação nem confirma compra ou efetivação bancária. |
| Status × presença de `billId` | `POSTED`/presente 257/257; `PENDING`/ausente ou nulo 62/62 | A presença é apenas categórica; identificador não retido. |
| `paymentType` | `SINGLE` 241/241; `INSTALLMENT` 78/78 | Os enums alternativos não tiveram ocorrência reportada. Isso não decide equivalência entre os pares documentados. |
| Metadados de parcela | ambos ausentes 241/241; completos válidos 78/78; parcial/inválido sem ocorrência reportada | Validade é estrutural: números positivos e número atual ≤ total. Não valida o significado dos campos. |
| `date` × `purchaseDate` | mesma data 9/9; diferente 69/69; ausente/inválida 241/241 | Comparação diagnóstica de calendário, sem inferência de competência. |
| `date` × `billPostDate` | mesma data 153/153; diferente 166/166 | Comparação diagnóstica de calendário. |
| `purchaseDate` × `billPostDate` | mesma data 1/1; diferente 77/77; ausente/inválida 241/241 | Ausência de `purchaseDate` mantida como categoria própria. |
| Pares com `billForecastDate` | ausente/inválido 319/319 para cada um dos três pares | O campo estava presente na resposta em 319/319, mas o parser diagnóstico então utilizado não aceitava `yyyy-MM`. Portanto, esses buckets são **inconclusivos por limitação do parser**: não demonstram ausência ou invalidade dos períodos e não permitem reclassificação retroativa, pois os valores individuais não foram retidos. |

Como não houve duplicatas, os contadores recebidos e únicos coincidem nesta coleta; a separação permanece implementada e coberta por fixtures sintéticas. A saída omite buckets zero e não registra transações individuais, valores, datas, IDs, descrições ou conteúdo bruto. O diagnóstico expandido é observacional: compra continua desligada, natureza desconhecida não é promovida e competência não está confirmada. A tarefa 5.3 permanece bloqueada até evidência suficiente e conferência local do titular conforme roteiro sanitizado em `docs/AMBIENTE.md`.

No parser corrigido, `billForecastDate` distingue `missing` (campo ausente/nulo), `invalid` (valor presente não aceito) e `valid`. O período `yyyy-MM` é validado estritamente e comparado como ano/mês, sem criar dia ou horário; continuam aceitos `yyyy-MM-dd` e instantes com offset explícito conforme o contrato temporal aprovado. Datas de calendário não sofrem conversão e instantes são convertidos por `America/Sao_Paulo`. Isso permanece uma comparação diagnóstica, sem confirmar competência financeira. Os agregados históricos de 319 registros acima permanecem inconclusivos e não são recalculados.

### Conferência local do titular — distinção entre evidência e regra automática

O titular informou que conferiu localmente um lançamento que corresponde a uma parcela de compra: compra, valor, número da parcela e mês da fatura correspondiam ao aplicativo Nubank. A síntese não inclui nem registra esses valores, número, datas, ID ou descrição. Para o trecho da **interface do Dashboard Pluggy** examinado, o titular reportou `operationType=PAGAMENTO`, `type=DEBIT`, sinal positivo e `status=PENDING`; `paymentType` e metadados estruturados de parcelamento não estavam visíveis nesse trecho.

Esta confirmação sustenta **a natureza de compra daquele caso conferido pelo titular**, mas não comprova que todo `PAGAMENTO` seja compra, não valida automaticamente o classificador nem estabelece regra geral de competência. Ela contradiz a hipótese de D4 de que `PAGAMENTO` possa ser tratado exclusivamente como pagamento de fatura. `PENDING` continua sendo situação pendente; não torna o caso efetivado para gastos.

#### Comparação offline: Dashboard Pluggy e comandos locais

| Caminho | Endpoint e paginação | DTO/caminho JSON retido | O que se pode concluir |
|---|---|---|---|
| Interface do Dashboard Pluggy | Interface externa; endpoints/payload do trecho examinado não estão instrumentados nem documentados neste repositório | Nenhum DTO do Dashboard está disponível no código local para comparação | O titular informou que `paymentType` e metadados estruturados não apareciam no trecho visualizado. A causa da ausência no JSON ou na apresentação do Dashboard é desconhecida; o DTO do comando local não explica nem prova o comportamento do Dashboard. |
| Comando local `pluggy-read` | `GET /v2/transactions?accountId=...`; lê somente a primeira página, informa se `next` existe e não segue o cursor | `PluggyTransactionsPage.results[]` desserializa cada item como `PluggyTransactionResponse(id, accountId)` | Propriedades não mapeadas, incluindo `creditCardMetadata` e `paymentData`, são descartadas pelo DTO estreito. A saída do comando não pode mostrar `paymentType` mesmo que o JSON recebido contenha o campo. |
| Comando local `pluggy-analyze` | Mesmo endpoint base `/v2/transactions`, com `accountId` e `dateFrom`, seguindo `next` validado | `PluggyAnalysisTransactionResponse.creditCardMetadata.paymentType` e demais propriedades allowlisted são lidas do objeto aninhado | A coleta agregou `paymentType` em 319/319 registros; os agregados não identificam se o caso conferido pelo titular estava entre eles nem se seu payload continha esses campos. |

Portanto, o DTO mínimo explica somente por que o **comando local** `pluggy-read` descarta esses campos. Não há evidência local para atribuir a ausência visual ou no JSON do **Dashboard Pluggy** a esse DTO, a um endpoint específico, ao conector ou à resposta de uma transação. Essa causa permanece desconhecida; nenhuma consulta será feita para tentar resolvê-la.

#### Fixture documental exclusivamente fictícia

| Campo da fixture | Valor ilustrativo inventado | Interpretação do teste documental |
|---|---|---|
| `operationType` | `PAGAMENTO` | Operação ambígua; não classificar isoladamente como pagamento de fatura. |
| `type` / sinal de `amount` | `DEBIT` / positivo | Compatível com a evidência do caso, mas insuficiente para regra universal. |
| `status` | `PENDING` | Situação continua pendente e fora de gastos efetivados. |
| `paymentType`, `installmentNumber`, `totalInstallments` | ausentes | Não inferir parcelamento pelo rótulo, descrição ou qualquer padrão textual como `12/12`. |
| Natureza conferida fora do classificador | compra (asserção fictícia da fixture) | Demonstra que conhecimento confirmado sobre um caso é distinto do resultado automático atual, que permanece `Unknown` até regra discriminatória validada. |

Todos os valores desta fixture são sintéticos; não reproduzem valor, parcela ou mês da conferência real. Não devem ser usados como fallback de produção.

#### Revisão de D4 aprovada e aplicada de forma conservadora

1. Separar `PAGAMENTO_FATURA`, que pode continuar candidato a pagamento de fatura somente quando os demais sinais forem coerentes, de `PAGAMENTO`, que nesta conexão é ambíguo e não pode ser interpretado exclusivamente como pagamento de fatura.
2. O caso `PAGAMENTO` + `DEBIT` + valor positivo + `PENDING` demonstra uma compra parcelada confirmada pelo titular, mas não fornece sozinho um predicado seguro para classificar automaticamente todos os lançamentos desse padrão. Até haver evidência discriminatória, o classificador deve manter essa combinação como desconhecida/não elegível; a confirmação local permanece anotação de natureza do caso, fora do algoritmo.
3. Não usar descrição, `paymentType` ausente, metadados ausentes ou padrão textual de parcela como fallback. Em particular, nenhum texto como `12/12` autoriza compra.
4. Não mudar a situação: `PENDING` continua pendente. Não derivar competência do mês da fatura nem alterar a regra positiva de compra/gate.

O titular aprovou a revisão documental de D4 e da spec em 15/11/2026. O classificador agora deixa `PAGAMENTO` em natureza desconhecida para qualquer direção/sinal, inclusive `CREDIT`/negativo, e mantém `PAGAMENTO_FATURA` como código separado de pagamento de fatura. A fixture sintética cobre o caso conferido (`PAGAMENTO`/`DEBIT`/positivo/`PENDING`) e verifica que permanece desconhecido e não efetivado; não cria regra positiva de compra. A alteração não liga o gate de compra nem confirma competência. A tarefa 5.3 permanece aberta.
