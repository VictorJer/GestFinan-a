# Spec Delta

## Purpose

Permitir ao titular organizar seus próprios movimentos com categoria e observação persistentes, mantendo separadas a organização pessoal e as informações financeiras recebidas da origem.

## ADDED Requirements

### Requirement: Edição protegida e parcial de anotações
A API SHALL permitir que o titular com sessão válida altere a categoria e/ou a observação de um movimento persistido próprio por `PATCH /api/transactions/{id}/annotation`. Campos omitidos SHALL conservar seus valores anteriores; pelo menos um dos dois campos SHALL estar presente. A resposta de sucesso SHALL devolver a categoria e a observação resultantes, sem incluir fatos financeiros.

#### Scenario: Editar ambos os campos
- **WHEN** o titular autenticado envia categoria válida e observação para um movimento próprio
- **THEN** a API responde 200 com os dois valores persistidos e a leitura posterior do movimento reflete a anotação

#### Scenario: Editar somente observação
- **GIVEN** o movimento já possui categoria pessoal
- **WHEN** o titular envia somente uma nova observação
- **THEN** a categoria anterior permanece e apenas a observação muda

#### Scenario: Editar somente categoria
- **GIVEN** o movimento já possui observação
- **WHEN** o titular envia somente uma categoria diferente
- **THEN** a observação anterior permanece e apenas a categoria muda

#### Scenario: Corpo sem campos editáveis
- **WHEN** a requisição omite categoria e observação ou tenta editar identificador, titular ou fatos da origem
- **THEN** recebe 400 sanitizado e nenhuma anotação ou fato financeiro é alterado

### Requirement: Categoria própria ou de sistema e ausência virtual
A edição SHALL aceitar somente categoria persistida de sistema ou categoria pessoal do titular autenticado. `categoryId: null` SHALL limpar a associação, mantendo a observação; o identificador vazio SHALL não representar “Sem categoria”. Categoria inexistente e categoria pessoal alheia SHALL receber resposta indistinguível sem revelar propriedade.

#### Scenario: Selecionar categoria inicial ou pessoal própria
- **WHEN** o titular escolhe um ID persistido de sistema ou pessoal próprio
- **THEN** a categoria aparece na anotação do movimento e em consultas subsequentes

#### Scenario: Voltar para Sem categoria
- **GIVEN** o movimento tem categoria e observação
- **WHEN** o titular envia `categoryId: null`
- **THEN** a categoria passa a nula, a observação permanece e nenhum registro de categoria especial é criado

#### Scenario: Categoria de outro titular ou inexistente
- **WHEN** o titular fornece ID de categoria pessoal alheia ou ID não encontrado
- **THEN** ambos recebem o mesmo 404 sanitizado e a anotação permanece inalterada

#### Scenario: ID de categoria malformado
- **WHEN** o identificador enviado não é um UUID válido e não é nulo
- **THEN** a API responde 400 sanitizado, sem gravar anotação

### Requirement: Observação em texto simples com limpeza independente
A observação SHALL ser texto simples de até 500 caracteres, preservado sem truncamento ou interpretação como HTML. `note: null` ou texto vazio SHALL limpar a observação sem remover a categoria; valores acima do limite ou de tipo inválido SHALL ser rejeitados sem alterar o estado anterior.

#### Scenario: Observação no limite
- **WHEN** o titular envia observação válida de 500 caracteres
- **THEN** a leitura posterior devolve exatamente o texto recebido e a categoria existente continua inalterada

#### Scenario: Limpar somente a observação
- **GIVEN** o movimento tem categoria e observação
- **WHEN** o titular envia `note: null` ou `note: ""` sem campo de categoria
- **THEN** a observação fica nula e a categoria permanece

#### Scenario: Observação inválida
- **WHEN** o titular envia texto com mais de 500 caracteres ou tipo não textual
- **THEN** recebe 400 sanitizado e nenhum campo da anotação é modificado

### Requirement: Isolamento e falhas sem vazamento
A escrita SHALL derivar o titular exclusivamente da sessão e confirmar propriedade do movimento e da categoria antes de gravar. Movimento ausente e alheio SHALL receber o mesmo 404; sessão ausente ou revogada SHALL receber 401 com challenge Bearer. Falhas e limites SHALL seguir a política de erros sanitizados da API, sem incluir texto da observação, dados de origem ou identificadores em logs de falha.

#### Scenario: Movimento alheio ou inexistente
- **WHEN** a sessão tenta alterar um movimento de outro titular ou um ID inexistente
- **THEN** ambos recebem 404 indistinguível e nenhum movimento ou anotação é modificado

#### Scenario: Sessão ausente ou revogada
- **WHEN** uma escrita é enviada sem sessão ativa
- **THEN** recebe 401 com challenge Bearer e não consulta nem grava anotações

#### Scenario: Persistência indisponível ou limite de requisições
- **WHEN** a persistência está tipadamente indisponível ou o limite protegido da API é excedido
- **THEN** recebe respectivamente 503 ou 429 sanitizado, sem confirmação de edição parcial

### Requirement: Organização consistente sem alterar origem
A edição SHALL alterar atomicamente apenas a anotação pessoal. A leitura e o filtro/contagem por categoria existentes SHALL refletir o estado confirmado; uma nova importação ou reconexão conservadora SHALL preservar a anotação ligada à mesma identidade interna. Categoria ou observação SHALL NOT modificar natureza, situação, valor, moeda, descrição, datas, cobertura ou gerar soma monetária e SHALL NOT iniciar sincronização.

#### Scenario: Filtro após trocar categoria
- **GIVEN** um movimento pertence ao filtro de categoria anterior
- **WHEN** o titular troca sua categoria e a gravação é confirmada
- **THEN** a consulta e a contagem deixam de incluí-lo no filtro anterior e passam a incluí-lo no novo filtro, sem total monetário

#### Scenario: Reimportação com fatos de origem alterados
- **GIVEN** o movimento possui categoria e observação pessoais
- **WHEN** nova importação atualiza seus fatos externos na mesma identidade interna
- **THEN** os fatos recebidos seguem sua política de atualização e a anotação permanece vinculada sem reclassificar a natureza

#### Scenario: Falha ou concorrência de edição
- **WHEN** duas edições independentes da mesma anotação ocorrem em paralelo, ou uma edição falha antes da confirmação
- **THEN** cada alteração confirmada preserva os campos omitidos da outra, no máximo uma anotação existe por movimento, e uma falha não publica atualização parcial
