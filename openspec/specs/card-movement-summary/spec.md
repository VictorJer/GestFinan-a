# card-movement-summary Specification

## Purpose

Permitir ao titular autenticado compreender quantos movimentos persistidos existem no conjunto filtrado, como se distribuem por natureza e situação e qual é a cobertura conhecida, sem apresentar contagens como gastos financeiros.

## Requirements

### Requirement: Resumo protegido do universo filtrado
A API SHALL oferecer `GET /api/dashboard` para a sessão ativa do titular com `cardId?`, `categoryId?`, `situation?`, `from?`, `toExclusive?` e `search?`, aplicando a mesma semântica de filtros da consulta de movimentos. SHALL retornar `filteredCount` do conjunto persistido filtrado inteiro, sem paginação, e `dateBasis: "TransactionDate"` para identificar o dia técnico, não competência financeira.

#### Scenario: Filtros combinados e página independente
- **WHEN** o titular filtra cartão próprio, categoria, situação, intervalo de data e descrição, enquanto a lista de movimentos está dividida em páginas
- **THEN** `filteredCount` do resumo corresponde à contagem do filtro inteiro de `GET /api/transactions` com os mesmos filtros, não ao número de itens de uma página

#### Scenario: Datas de origem sem dia técnico
- **WHEN** há movimento com dia técnico indeterminável e o titular consulta sem filtro temporal e depois com intervalo temporal
- **THEN** o movimento integra somente o conjunto sem intervalo, e o resumo identifica o intervalo como dia técnico, sem afirmar ciclo de fatura ou competência

#### Scenario: Categoria alheia ou inexistente
- **WHEN** o filtro contém ID de categoria pessoal alheia ou ID inexistente
- **THEN** ambos produzem o mesmo conjunto persistido filtrado vazio, sem revelar a categoria ou incluir movimentos de outro titular

### Requirement: Contagens independentes por natureza e situação
O resumo SHALL fornecer contagens não monetárias por natureza (`Purchase`, `Refund`, `BillPayment`, `Fee`, `Adjustment`, `Unknown`) e por situação (`Effective`, `Pending`, `FutureInstallment`, `Unknown`) para o mesmo conjunto filtrado. Cada movimento SHALL contribuir uma vez em cada eixo; `Unknown` SHALL permanecer visível quando houver ambiguidade.

#### Scenario: Natureza ambígua e situação pendente
- **WHEN** o conjunto contém `PAGAMENTO` com natureza desconhecida e situação pendente, junto de pagamento de fatura identificado
- **THEN** o primeiro aparece em `Unknown` no eixo natureza e `Pending` no eixo situação, e o segundo em `BillPayment`, sem reclassificar nenhum como compra

#### Scenario: Parcelas futuras e encargos
- **WHEN** há parcelas futuras identificadas, tarifas e ajustes no conjunto filtrado
- **THEN** suas situações e naturezas armazenadas são contadas nos eixos correspondentes, separadas de natureza desconhecida e sem declarar compras efetivadas

#### Scenario: Conciliação dos eixos
- **WHEN** a consulta retorna um conjunto persistido com naturezas e situações diversas, incluindo desconhecidas
- **THEN** a soma das contagens de cada eixo, separadamente, é igual a `filteredCount` para o mesmo filtro e o mesmo estado dos dados

#### Scenario: Grupo sem ocorrência
- **WHEN** uma natureza ou situação conhecida não possui movimentos no conjunto filtrado
- **THEN** seu grupo aparece com contagem zero para esse conjunto persistido, sem converter cobertura desconhecida em histórico bancário completo

### Requirement: Cobertura separada de contagens
A resposta SHALL incluir cobertura com último sucesso confirmado, janela correlata quando conhecida e execução mais recente em campos separados das contagens, usando a semântica da consulta existente. `filteredCount` igual a zero SHALL descrever somente ausência no conjunto persistido filtrado, sem afirmar histórico bancário integral, classificação completa ou gasto zero.

#### Scenario: Sucesso anterior e tentativa parcial posterior
- **GIVEN** existe janela confirmada anterior e uma execução mais recente falhou ou ficou parcial
- **WHEN** o titular solicita o resumo
- **THEN** a resposta preserva o sucesso anterior e informa a execução recente separadamente, sem declarar a nova janela completa

#### Scenario: Sem importação confirmada e filtro vazio
- **WHEN** nenhum movimento corresponde ao filtro e não existe importação confirmada
- **THEN** o total do conjunto persistido filtrado é zero, a cobertura permanece desconhecida/ausente e a resposta não afirma gasto financeiro zero

#### Scenario: Janela concluída não comprova banco inteiro
- **WHEN** a última janela solicitada está marcada `CompleteForRequestedWindow`
- **THEN** somente essa janela é descrita como concluída, não a cobertura integral do banco ou a classificação financeira dos movimentos

### Requirement: Leitura autorizada e sem efeitos externos
A API SHALL derivar titularidade da sessão, isolar movimentos e cobertura pela conexão própria e responder com erros sanitizados. Consulta inválida SHALL receber 400; sessão ausente ou revogada, 401 com challenge Bearer; conexão ou cartão inexistente/inacessível, 404 indistinguível; conexão ambígua, 409. Limites e falhas SHALL seguir os mesmos contratos protegidos da API. A leitura SHALL NOT iniciar sincronização.

#### Scenario: Outro titular e cartão inacessível
- **WHEN** uma sessão válida consulta o resumo com `cardId` alheio ou inexistente
- **THEN** ambos retornam 404 indistinguível sem publicar contagem/cobertura alheia

#### Scenario: Falha de entrada e sessão revogada
- **WHEN** uma chamada usa intervalo inválido ou sessão expirada/revogada
- **THEN** retorna respectivamente 400 sanitizado ou 401 com challenge Bearer sem produzir resumo

#### Scenario: Leitura sem provedor disponível
- **WHEN** provedor ou broker está indisponível, mas o banco contém movimentos já persistidos do titular
- **THEN** o resumo continua legível e nenhuma execução de sincronização, consulta externa ou escrita é iniciada

### Requirement: Nenhum indicador monetário derivado
A resposta de resumo SHALL conter somente contagens e metadados de cobertura, sem valores individuais nem soma, subtotal, média, série ou agrupamento monetário de movimentos. SHALL NOT apresentar quantidade presumida de compras, gasto, renda, saldo, patrimônio ou confirmação de recorrência como indicador financeiro, mesmo com rótulo parcial.

#### Scenario: Sinais e moedas mistos
- **WHEN** o conjunto inclui valores positivos e negativos em moedas distintas e categorias pessoais
- **THEN** a resposta contém somente contagens e cobertura permitidas, sem campo de valor monetário ou conversão e sem usar categoria para inferir compra

#### Scenario: Filtro após edição de categoria
- **GIVEN** uma anotação pessoal teve sua categoria alterada por edição confirmada
- **WHEN** o titular consulta novamente resumo e lista com o filtro antigo e o novo
- **THEN** as contagens seguem o conjunto persistido atual correspondente a cada filtro, sem somar valores ou alterar natureza e situação originais
