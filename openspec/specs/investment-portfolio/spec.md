# investment-portfolio Specification

## Purpose

Permitir que o titular mantenha uma carteira manual de investimentos persistente, com valores e resumos próprios, sem inferir resultados a partir dos movimentos do cartão.

## Requirements

### Requirement: Registro manual e persistente da carteira
O sistema SHALL permitir ao titular autenticado criar, listar, editar e excluir seus investimentos com nome, classe, valor aplicado e valor atual. Os registros SHALL persistir após recarga e reinício, sem depender de conexão bancária, cotação externa ou transações de cartão.

#### Scenario: Cadastro e releitura
- **WHEN** o titular cadastra um investimento válido e recarrega a aplicação
- **THEN** encontra o mesmo nome, classe, valor aplicado e valor atual na lista própria

#### Scenario: Edição e exclusão
- **GIVEN** existe um investimento do titular
- **WHEN** ele altera seus dados e depois o exclui
- **THEN** a edição aparece em nova leitura e, após a exclusão, o registro deixa de aparecer na carteira e no resumo

#### Scenario: Carteira vazia
- **WHEN** o titular não possui investimentos
- **THEN** recebe lista vazia e estado de carteira vazia, sem ativos ou valores de demonstração apresentados como reais

### Requirement: Validação explícita de campos e classes
O sistema SHALL exigir nome não vazio, classe de uma das três opções confirmadas (renda fixa, renda variável ou fundos imobiliários) e valores monetários não negativos e representáveis, sem coerção silenciosa, arredondamento ou preenchimento fictício de dados inválidos.

#### Scenario: Entrada válida
- **WHEN** o titular informa nome e classe permitida com valor aplicado e valor atual não negativos e válidos
- **THEN** o registro é aceito e a leitura preserva os valores informados

#### Scenario: Entrada incompleta, classe desconhecida ou valor inválido
- **WHEN** um campo obrigatório está vazio, a classe é desconhecida ou um valor é negativo, não numérico ou fora da precisão aceita
- **THEN** a gravação é recusada com erro de validação sem criar ou alterar investimento

### Requirement: Contrato HTTP monetário decimal canônico
O contrato HTTP SHALL transportar valores monetários como strings decimais canônicas invariáveis, com ponto e exatamente duas casas, sem agrupamento, expoente ou zeros à esquerda. Valores individuais SHALL permanecer no intervalo não negativo de `numeric(19,2)`; respostas SHALL preservar essa representação para posições, totais, diferença e valores por classe.

#### Scenario: Round-trip monetário em toda a faixa individual
- **WHEN** o titular envia e consulta posição com valor até `99999999999999999.99`
- **THEN** requisição, resposta do item e resumo preservam exatamente a string com duas casas, inclusive os centavos, sem conversão por número binário no cliente

#### Scenario: Representação HTTP não canônica
- **WHEN** a API recebe valor sem exatamente duas casas, com expoente, separador localizado, zeros à esquerda, tipo JSON numérico ou fora de `numeric(19,2)`
- **THEN** recusa a entrada sem persistir ou arredondar; valores monetários de resposta permanecem strings decimais com ponto e duas casas, e a diferença pode ser negativa

### Requirement: Carteira isolada por titular e sessão
O sistema SHALL obter o titular da sessão ativa e restringir listagem, alteração e exclusão aos seus registros. Recursos inexistentes e recursos de outro titular SHALL ter resposta indistinguível; uma sessão inválida SHALL não divulgar dados da carteira.

#### Scenario: Recurso alheio ou inexistente
- **WHEN** o titular tenta editar ou excluir um investimento de outro titular ou um identificador inexistente
- **THEN** recebe a mesma resposta 404 sem alterar o registro nem divulgar seu conteúdo

#### Scenario: Acesso sem sessão válida
- **WHEN** alguém consulta ou altera a carteira sem sessão válida
- **THEN** a API retorna 401 sem leitura nem escrita dos investimentos protegidos

### Requirement: Resumo e distribuição reconciliados
O sistema SHALL calcular total aplicado, total atual e diferença simples (atual menos aplicado) exclusivamente a partir dos investimentos do titular. SHALL apresentar distribuição por classe com base nos valores atuais dos mesmos registros e identificar essa diferença como não ajustada a datas ou fluxos financeiros.

#### Scenario: Classes e totais
- **GIVEN** o titular tem registros em duas classes, incluindo valor atual zero
- **WHEN** consulta o resumo após incluir, editar ou excluir um deles
- **THEN** os totais e os valores atuais por classe conciliam com a lista própria e a diferença corresponde a total atual menos total aplicado

#### Scenario: Carteira vazia e total atual zero
- **WHEN** não há registros ou a soma dos valores atuais é zero
- **THEN** os totais e valores por classe aplicáveis são zero e nenhuma porcentagem por classe é apresentada como se tivesse denominador positivo

### Requirement: Apresentação fiel e separada de outras finanças
A página protegida SHALL apresentar lista e resumo da carteira em português, com estados identificáveis de carregamento, ausência de registros, falha e ação em andamento. SHALL não derivar investimentos de movimentos do cartão nem apresentar a diferença simples como rentabilidade ajustada, recomendação ou projeção.

#### Scenario: Operação bem-sucedida e erro
- **WHEN** o titular salva ou exclui um registro, ou uma requisição falha
- **THEN** a página atualiza os dados confirmados após sucesso ou preserva um estado coerente com orientação de recuperação após falha, sem fingir sucesso

#### Scenario: Encerramento de sessão durante leitura
- **WHEN** a sessão expira ou o titular sai enquanto uma leitura da carteira está em andamento
- **THEN** a resposta tardia não exibe dados privados na nova sessão ou na página pública
