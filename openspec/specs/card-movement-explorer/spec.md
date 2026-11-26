# card-movement-explorer Specification

## Purpose

Permite ao titular inspecionar movimentos do cartão com natureza e situação separadas, cobertura visível e ambiguidades preservadas, sem transformar registros incompletos ou não classificados em gasto total.

## Requirements

### Requirement: Apresentação descritiva dos movimentos do cartão
A aplicação SHALL apresentar os movimentos efetivamente disponibilizados para a consulta, com valor e moeda por movimento, data de origem, descrição disponível, situação e natureza identificável. SHALL informar cobertura e completude, sem tratar uma consulta parcial como conjunto completo.

#### Scenario: Consulta completa com movimentos conhecidos e ambíguos
- **WHEN** a consulta conclui a paginação e contém movimentos de naturezas conhecidas e desconhecidas
- **THEN** a interface apresenta cada movimento no escopo consultado, mostra a cobertura e preserva os ambíguos como não classificados, sem descartá-los silenciosamente

#### Scenario: Consulta incompleta
- **WHEN** a paginação falha, é cancelada ou atinge um limite
- **THEN** o resultado é marcado incompleto e nenhuma contagem ou visão parcial é apresentada como universo completo

### Requirement: Composição da visão geral com a lista única
A visão geral SHALL mostrar resumos descritivos, cobertura e filtros aplicados e permitir abrir a lista única preservando esses filtros. Os campos e valores individuais SHALL aparecer na lista, sem criar outra lista na visão geral.

#### Scenario: Da visão geral à lista única
- **WHEN** o titular consulta os resumos com filtros aplicados e abre a lista de movimentos
- **THEN** a visão geral apresenta contagens e cobertura do universo declarado sem reproduzir os movimentos individuais, e a lista única mantém os filtros aplicados e apresenta valores, moeda, data de origem, natureza e situação por movimento; consultas separadas não prometem um snapshot atômico

### Requirement: Natureza explícita e independente da situação
A aplicação SHALL apresentar natureza somente quando sustentada pelas regras aprovadas e SHALL manter natureza desconhecida distinta de situação pendente, efetivada ou futura. Códigos ambíguos não SHALL ser promovidos por sinal, descrição ou equivalência não validada.

#### Scenario: Operação ambígua PAGAMENTO
- **WHEN** um movimento apresenta `operationType=PAGAMENTO` sem condição discriminatória validada
- **THEN** sua natureza permanece desconhecida, inclusive quando direção, sinal ou situação coincidam com um caso individual confirmado

#### Scenario: Pagamento de fatura distinto
- **WHEN** um movimento apresenta `PAGAMENTO_FATURA` e os demais sinais são coerentes com a regra aprovada
- **THEN** é apresentado como pagamento de fatura, separado de compra, estorno, encargo e desconhecido

#### Scenario: Situação pendente
- **WHEN** um movimento tem status `PENDING`, qualquer que seja sua natureza
- **THEN** é apresentado como pendente e não como movimento efetivado; a natureza não é inferida do status

### Requirement: Ausência e incerteza não são valores financeiros
A aplicação SHALL distinguir informação ausente, natureza desconhecida e resultado monetário indisponível de valores zero. SHALL não omitir ambiguidades para sugerir cobertura integral.

#### Scenario: Ausência não é zero
- **WHEN** movimentos, valores ou metadados estão ausentes
- **THEN** a interface informa ausência ou indisponibilidade sem substituir a informação por zero ou dado fictício

### Requirement: Nenhum agregado monetário nesta entrega
A aplicação SHALL NOT calcular, retornar ou apresentar qualquer agregado monetário derivado de múltiplos movimentos nesta entrega, independentemente do rótulo, canal ou completude. Somente valores individuais por movimento podem ser exibidos.

#### Scenario: Nenhuma soma de gasto validada
- **WHEN** a classificação de compras ou a cobertura semântica não permite calcular o gasto total
- **THEN** nenhum valor monetário agregado é calculado, retornado ou exibido, e a interface informa que o indicador de gasto não está disponível

#### Scenario: Proibição independe do rótulo
- **WHEN** uma camada da aplicação tenta resumir valores de múltiplos movimentos como total, subtotal, soma bruta/líquida, média, série, gráfico ou agrupamento por período, situação, natureza ou categoria
- **THEN** o agregado monetário não é calculado nem incluído em resposta ou apresentação, mesmo que seja rotulado como parcial, informativo ou não total

### Requirement: Datas não declaram competência financeira
A aplicação SHALL identificar a data exibida como dado de origem ou data normalizada segundo regra explicitada. SHALL não usar `billForecastDate`, mês de fatura ou data de vencimento como competência financeira sem confirmação semântica.

#### Scenario: Data de movimento sem competência confirmada
- **WHEN** o titular filtra ou consulta movimentos usando uma data de origem disponível
- **THEN** o resultado descreve o período da data usada e não o apresenta como ciclo de fatura ou competência financeira

#### Scenario: Indicador por competência não validado
- **WHEN** a semântica do campo temporal para competência permanece não confirmada
- **THEN** a interface não apresenta evolução mensal de gastos, gasto por fatura ou previsão como métrica financeira
