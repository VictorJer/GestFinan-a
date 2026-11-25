# web-card-movement-overview Specification

## Purpose

Permitir ao titular interpretar contagens e cobertura dos próprios movimentos persistidos em uma visão protegida, conectada à lista existente, sem apresentar indicadores financeiros não validados.

## Requirements

### Requirement: Visão protegida do conjunto filtrado
A visão SHALL consultar o resumo protegido com os mesmos filtros efetivamente aplicados à lista de movimentos, sem paginação, e SHALL identificar `TransactionDate` como dia técnico do filtro, não competência ou ciclo de fatura. Acesso direto sem sessão SHALL exigir login sem exibir dados privados.

#### Scenario: Acesso direto autenticado
- **GIVEN** o titular tem sessão válida e nenhum filtro foi aplicado na lista nesta sessão
- **WHEN** abre diretamente a visão
- **THEN** recebe as contagens do conjunto persistido sem filtros e a identificação explícita da data técnica, sem iniciar importação

#### Scenario: Retorno após aplicar filtros na lista
- **GIVEN** o titular aplicou cartão, categoria, situação, intervalo e busca literal na lista
- **WHEN** navega para a visão
- **THEN** o resumo usa exatamente esses filtros, com início inclusivo e fim exclusivo, sem enviar página ou tamanho de página

#### Scenario: Acesso direto sem sessão
- **WHEN** uma pessoa abre o destino da visão sem sessão válida
- **THEN** é encaminhada ao login e nenhuma contagem ou cobertura privada é apresentada

### Requirement: Contagens descritivas e ambiguidades explícitas
A visão SHALL apresentar `filteredCount` do universo persistido filtrado inteiro e as contagens independentes por natureza e situação recebidas da API, inclusive grupos desconhecidos e grupos sem ocorrências. SHALL não confundir uma página da lista, uma categoria pessoal ou um status com quantidade de compras confirmadas.

#### Scenario: Página menor que o universo
- **GIVEN** a lista mostra uma página com 25 itens e o conjunto filtrado contém mais movimentos
- **WHEN** o titular abre a visão para os mesmos filtros
- **THEN** vê a contagem do conjunto filtrado, não o número de itens da página, com os dois eixos rotulados separadamente

#### Scenario: Natureza desconhecida e situação pendente
- **WHEN** o resumo contém movimentos `Unknown` na natureza e `Pending` na situação
- **THEN** ambos aparecem com seus respectivos números sem converter desconhecidos ou pendentes em compras efetivadas

#### Scenario: Conjunto filtrado vazio
- **WHEN** o resumo retorna `filteredCount` zero e grupos com contagem zero
- **THEN** a visão informa ausência de movimentos persistidos no filtro, sem afirmar ausência de histórico na origem ou gasto zero

### Requirement: Cobertura e execução separadas do universo
A visão SHALL apresentar último sucesso confirmado, janela conhecida e execução mais recente como dimensões independentes das contagens. SHALL identificar conclusão da janela solicitada somente como cobertura dessa janela; falha/ausência de atualização não SHALL transformar cobertura desconhecida em completa.

#### Scenario: Falha recente após sucesso confirmado
- **GIVEN** existe janela anteriormente confirmada e a execução posterior falhou
- **WHEN** o titular abre a visão
- **THEN** vê o sucesso anterior, sua janela e a falha recente separadamente, sem anunciar nova cobertura completa

#### Scenario: Importação ainda não confirmada
- **WHEN** a API informa cobertura sem último sucesso nem janela confirmada
- **THEN** a visão informa cobertura desconhecida, inclusive se a contagem persistida filtrada for zero

### Requirement: Reutilização da lista e consistência dos filtros
A visão SHALL oferecer navegação para a única lista de movimentos existente e SHALL preservar os filtros aplicados da sessão ao mudar entre visão e lista. SHALL não criar segunda lista, paginação paralela ou edição de anotação/categoria na visão. Alterações confirmadas na lista SHALL poder ser refletidas por nova leitura do resumo.

#### Scenario: Navegação para movimentos
- **GIVEN** o resumo foi lido com filtros aplicados à lista na sessão atual
- **WHEN** o titular segue o link de movimentos
- **THEN** a lista apresenta esses mesmos filtros e permite paginação/organização nos controles já existentes

#### Scenario: Categoria alterada na lista
- **GIVEN** o titular alterou uma categoria e a API confirmou a edição
- **WHEN** consulta novamente a visão com o filtro aplicável
- **THEN** vê o resumo atual retornado pela API, sem recalcular contagens a partir da página anterior

### Requirement: Estados, privacidade e recuperação
A visão SHALL diferenciar carregamento, ausência de movimentos no filtro, falha de rede, indisponibilidade, falta de permissão e resposta incompatível, com orientação segura em português. Logout, expiração ou troca de sessão SHALL ocultar os dados privados e impedir que respostas tardias os reponham. Repetir a leitura SHALL não iniciar sincronização.

#### Scenario: Falha de leitura e repetição
- **WHEN** a consulta falha por rede ou indisponibilidade
- **THEN** a visão informa a falha sem expor corpo remoto e oferece repetição apenas da leitura do resumo

#### Scenario: Sessão encerrada durante consulta
- **WHEN** a sessão termina com uma leitura pendente ou uma resposta da sessão anterior chega depois
- **THEN** a visão remove contagens e cobertura privadas e não aplica a resposta tardia à nova sessão

#### Scenario: Proibição de leitura enganosa
- **WHEN** a resposta do resumo é incompatível com o contrato de contagens e cobertura
- **THEN** a visão apresenta erro seguro em vez de fabricar zeros, classificação ou completude

### Requirement: Apresentação acessível e sem métricas monetárias
A visão SHALL apresentar resumo textual/HTML acessível, com rótulos legíveis por teclado em celular e desktop. SHALL NOT calcular ou exibir soma, subtotal, média, série ou gráfico monetário, nem inferir gasto, renda, saldo, competência ou recorrência a partir das contagens.

#### Scenario: Uso por teclado em tela reduzida
- **WHEN** o titular percorre a visão e navega para a lista usando teclado em largura de celular
- **THEN** pode identificar contagens, cobertura, limitações e o link de navegação sem depender só de cor ou perder dados essenciais

#### Scenario: Sinais e moedas mistos na lista
- **GIVEN** a lista contém movimentos com valores individuais em moedas/sinais distintos
- **WHEN** o titular abre a visão correspondente
- **THEN** encontra apenas contagens e cobertura, sem valores monetários derivados nem gráfico de gasto, mesmo rotulado como parcial
