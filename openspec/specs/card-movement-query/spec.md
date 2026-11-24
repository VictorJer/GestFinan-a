# card-movement-query Specification

## Purpose

Permitir ao titular autenticado consultar seus cartões e movimentos persistidos com filtros, contagem e cobertura explícita, sem divulgar dados alheios nem produzir indicadores monetários agregados.

## Requirements

### Requirement: Cartões próprios consultáveis
A API SHALL oferecer `GET /api/cards` para retornar somente cartões persistidos da conexão própria única do titular, identificados por ID interno e campos de apresentação disponíveis, sem credenciais, número completo ou identificador externo da origem.

#### Scenario: Cartões de uma conexão própria
- **WHEN** o titular autenticado possui uma conexão e cartões persistidos
- **THEN** a API retorna somente cartões dessa conexão, com nome e final do cartão quando disponíveis e moeda informada, sem dados de outro titular

#### Scenario: Conexão existente sem cartões
- **WHEN** o titular possui conexão, mas não há cartões persistidos
- **THEN** a lista é vazia, sem afirmar que a origem não possui cartões

### Requirement: Movimentos próprios com detalhes conservadores
A API SHALL oferecer `GET /api/transactions` com os movimentos persistidos do titular e seus valores e moedas individuais, data original e dia técnico quando disponível, natureza e situação independentes com motivos internos permitidos, metadados de parcelas disponíveis e categoria/anotação pessoal já existente. SHALL não inferir compra ou competência.

#### Scenario: Conhecidos e ambíguos lado a lado
- **WHEN** o conjunto contém movimentos de naturezas conhecidas, `PAGAMENTO` ambíguo e situação `PENDING`
- **THEN** todos são retornados com valor individual, natureza e situação separados, mantendo `PAGAMENTO` desconhecido com motivo e `PENDING` sem declaração de efetivação

#### Scenario: Data ambígua e metadados opcionais
- **WHEN** um movimento tem data original disponível, mas dia técnico indeterminável ou metadados de parcela ausentes
- **THEN** a resposta preserva a origem, deixa o dia técnico ou metadados opcionais ausentes e não inventa dia, competência, parcela ou valor substituto

### Requirement: Filtros combináveis e busca literal
A consulta SHALL combinar filtros opcionais de cartão, intervalo de dia técnico da transação, categoria e situação com busca textual literal por descrição. O início do intervalo SHALL ser inclusivo e o fim exclusivo. Data indeterminável SHALL permanecer visível sem filtro de data e não SHALL corresponder a um intervalo definido.

#### Scenario: Combinação com data e categoria
- **WHEN** o titular filtra simultaneamente por cartão, intervalo, categoria, situação e descrição
- **THEN** somente movimentos próprios que satisfaçam todos os filtros são incluídos, usando dia técnico explicitado, não competência ou ciclo de fatura

#### Scenario: Busca sem operadores especiais
- **WHEN** a descrição buscada contém caracteres usados como curingas por sistemas de consulta
- **THEN** eles são tratados como texto literal, não como expansão do filtro para outros movimentos

#### Scenario: Intervalo inválido
- **WHEN** algum limite do intervalo é inválido ou, quando ambos são fornecidos, o fim não é posterior ao início
- **THEN** a API responde 400 sanitizado sem iniciar outra operação financeira

### Requirement: Paginação estável e contagem do filtro inteiro
A resposta de movimentos SHALL conter página, tamanho, itens e contagem de todos os movimentos persistidos que correspondem ao filtro, não apenas os da página. A ordenação SHALL ser decrescente por dia técnico com desempate por ID interno; datas indetermináveis SHALL vir depois das datas conhecidas, também ordenadas por ID. Tamanho de página SHALL ter limite explícito.

#### Scenario: Duas páginas com empates e data ausente
- **WHEN** o filtro retorna mais movimentos que o tamanho de página, incluindo empates de data e datas sem dia técnico
- **THEN** páginas sucessivas mantêm ordem determinística, não duplicam movimentos e exibem a mesma contagem do filtro inteiro na ausência de escrita concorrente

#### Scenario: Página além do fim e limite inválido
- **WHEN** é pedida uma página além do fim com parâmetros válidos
- **THEN** a API retorna itens vazios e a contagem real do filtro, sem converter ausência de itens da página em ausência de histórico

#### Scenario: Tamanho de página acima do permitido
- **WHEN** a requisição pede tamanho de página fora dos limites
- **THEN** a API responde 400 sanitizado, sem devolver um conjunto ilimitado

### Requirement: Cobertura e completude sem presunção
A consulta SHALL informar, separadamente da contagem filtrada, a última importação confirmada para a conexão e a cobertura de sua janela solicitada quando conhecida, além do estado sanitizado da execução posterior quando houver. SHALL distinguir completude da janela importada de cobertura bancária integral e de completude financeira/classificatória.

#### Scenario: Histórico antigo após falha posterior
- **GIVEN** existe importação completa confirmada para sua janela e uma execução mais recente falhou ou está pendente
- **WHEN** o titular consulta movimentos
- **THEN** os dados anteriores continuam consultáveis, o sucesso antigo e a execução recente têm estados distintos e a nova janela não é anunciada como completa

#### Scenario: Nenhuma importação confirmada ou resultado vazio
- **WHEN** não há importação confirmada ou a consulta filtrada não encontra movimentos
- **THEN** a resposta distingue contagem zero no conjunto persistido filtrado de cobertura desconhecida ou ausente, sem alegar gasto financeiro zero ou histórico bancário integral

### Requirement: Leitura protegida, sanitizada e sem sincronização implícita
As duas rotas SHALL exigir sessão ativa e restringir as consultas à identidade autenticada; identificador de cartão alheio e inexistente SHALL receber o mesmo 404. Erros SHALL seguir Problem Details sanitizado. Consultar, filtrar ou paginar SHALL não criar execução nem consultar provedor ou broker.

#### Scenario: Sessão ausente ou revogada
- **WHEN** uma chamada é feita sem sessão válida
- **THEN** recebe 401 com challenge Bearer, sem executar consulta nem expor dados

#### Scenario: Cartão alheio e identificador inexistente
- **WHEN** o titular informa filtro de cartão pertencente a outro titular ou inexistente
- **THEN** ambos retornam 404 indistinguível, sem revelar titular ou movimentos alheios

#### Scenario: Categoria de outro titular
- **WHEN** um identificador de categoria de outro titular é usado como filtro
- **THEN** nenhum movimento alheio é retornado e a resposta não distingue esse identificador de uma categoria inexistente

#### Scenario: Navegar sem efeitos financeiros
- **WHEN** o titular consulta cartões, filtra ou muda a página de movimentos com broker ou provedor indisponível
- **THEN** a leitura de dados já persistidos permanece possível e não cria `SyncRun` nem dispara consulta externa

### Requirement: Ausência de agregado monetário em toda resposta
A API SHALL retornar somente valores monetários individuais de movimentos nesta capability. SHALL NOT calcular ou retornar soma, total, subtotal, média, série, agrupamento ou indicador de gasto derivado de vários movimentos, mesmo com rótulo parcial ou informativo.

#### Scenario: Moedas e sinais mistos
- **WHEN** movimentos positivos e negativos em moedas distintas integram o mesmo filtro
- **THEN** cada valor e moeda individual permanece fiel ao armazenamento, a resposta pode trazer contagem não monetária e nenhum agregado monetário é retornado
