# web-card-movement-list Specification

## Purpose

Permitir ao titular consultar e organizar na interface os próprios movimentos persistidos, com filtros, paginação e cobertura claros, sem confundir dados incompletos com gasto financeiro calculado.

## Requirements

### Requirement: Lista descritiva protegida de movimentos
A interface SHALL listar movimentos da sessão válida com descrição disponível, valor e moeda individuais, data original explicitada, data técnica quando houver, natureza/motivo e situação separados, categoria, observação e metadados de parcelamento disponíveis. SHALL identificar como ausentes os campos não informados sem inventar compra, efetivação ou competência.

#### Scenario: Movimentos conhecidos e ambíguos
- **WHEN** a API retorna movimentos com naturezas identificadas, desconhecidas, status pendente e parcelas informadas
- **THEN** a lista mostra os valores individuais e metadados fornecidos, preserva os desconhecidos e não promove movimento pendente a efetivado nem trata parcela prevista como cobrança observada

#### Scenario: Datas e campos ausentes
- **WHEN** há data original textual mas data técnica indeterminável, ou há campos opcionais e metadados de parcelas ausentes
- **THEN** a tela explicita que o filtro usa data técnica, mantém a data original como fornecida quando apresentável e identifica ausências sem preencher dia, parcela, competência ou valor com zero

### Requirement: Filtros combináveis, contagem e paginação
A interface SHALL permitir busca literal por descrição e filtros combináveis de cartão, categoria, situação e intervalo de dia técnico, com início inclusivo e fim exclusivo; SHALL oferecer navegação paginada e exibir a contagem do conjunto filtrado da API, distinta do número de itens na página. SHALL não inferir presença ou ausência de histórico bancário pela página corrente.

#### Scenario: Interseção e troca de página
- **WHEN** o titular aplica simultaneamente filtros válidos e navega para outra página
- **THEN** a lista envia esses mesmos filtros em cada consulta, exibe a página na ordenação da API e informa `filteredCount` do conjunto completo filtrado, sem soma de valores

#### Scenario: Filtros novos e página anterior
- **WHEN** o titular altera busca, categoria, situação, cartão ou intervalo após estar em página posterior
- **THEN** a consulta volta à primeira página do novo filtro e não reaproveita a contagem da seleção anterior

#### Scenario: Intervalo inválido e busca literal
- **WHEN** o intervalo informado é inválido, o fim não é posterior ao início, ou a busca contém `%` e `_`
- **THEN** a interface impede intervalo inválido com orientação textual; para busca válida, preserva os caracteres literalmente para a API, sem ampliar localmente o filtro

#### Scenario: Página vazia fora do intervalo disponível
- **WHEN** a API retorna itens vazios em uma página solicitada com `filteredCount` positivo
- **THEN** a tela distingue essa página vazia da inexistência de movimentos no conjunto filtrado e oferece navegação de retorno

### Requirement: Cobertura separada de contagem e de gasto
A lista SHALL apresentar o último sucesso e a janela confirmada quando disponíveis, além do estado da execução mais recente em separado, identificando `CompleteForRequestedWindow` somente como cobertura da janela solicitada. SHALL diferenciar ausência de importação, consulta vazia e falha recente, sem converter desconhecido em zero ou em histórico integral.

#### Scenario: Falha recente após sucesso anterior
- **GIVEN** há uma importação confirmada e uma execução posterior falhou
- **WHEN** o titular abre a lista
- **THEN** vê o sucesso anterior, a cobertura da janela conhecida e a falha posterior separadamente, sem declaração de nova importação completa

#### Scenario: Nenhum registro no filtro com cobertura ausente
- **WHEN** a página responde com `filteredCount` zero e sem sucesso ou janela confirmada
- **THEN** a interface informa que não há movimentos persistidos no filtro e que a cobertura é desconhecida, sem anunciar gasto zero ou ausência de movimentos na origem

### Requirement: Criação e seleção de categorias pessoais
A interface SHALL listar opções do catálogo próprias da sessão, permitir criar categoria pessoal pelo nome e aplicá-la a movimento existente sem recarregar a aplicação. SHALL representar “Sem categoria” pela opção virtual de ID nulo e SHALL diferenciar conflito de nome de falha operacional sem apresentar uma criação não confirmada como concluída.

#### Scenario: Categoria pessoal criada e aplicada
- **WHEN** o titular cria categoria com nome válido e a API confirma sua criação
- **THEN** a nova opção aparece no seletor sem reload e pode ser aplicada a um movimento próprio, refletindo a associação apenas após confirmação da edição

#### Scenario: Categoria virtual e nomes conflitantes
- **WHEN** o titular escolhe “Sem categoria” ou tenta criar um nome equivalente a um existente/reservado
- **THEN** o primeiro caso remove somente a associação por ID nulo após confirmação; no segundo, a interface informa conflito sem fabricar ou duplicar opção

### Requirement: Edição parcial de categoria e observação
A interface SHALL permitir editar ou limpar categoria e observação de movimento próprio, preservando o campo não editado; SHALL respeitar observação em texto simples com no máximo 500 caracteres e atualizar lista e filtros/contagens somente conforme estado persistido confirmado. Falha SHALL não transformar edição otimista em confirmação.

#### Scenario: Salvar somente categoria ou observação
- **GIVEN** um movimento possui categoria e observação
- **WHEN** o titular altera apenas um campo e a API confirma a edição
- **THEN** o outro permanece como antes, o campo alterado aparece na lista e uma consulta posterior ao filtro reflete o estado confirmado

#### Scenario: Limpar observação sem alterar categoria
- **WHEN** o titular limpa a observação e a API confirma a alteração
- **THEN** a observação deixa de aparecer e a categoria permanece inalterada

#### Scenario: Validação ou escrita recusada
- **WHEN** a observação ultrapassa 500 caracteres, o nome da categoria é inválido, ou a API responde erro de validação, propriedade ou conflito
- **THEN** o titular recebe orientação sanitizada e a tela não anuncia uma alteração como salva nem exibe dados de outro titular

#### Scenario: Reimportação mantém anotação
- **GIVEN** uma categoria e uma observação foram confirmadas para um movimento
- **WHEN** uma nova importação atualiza fatos de origem e o titular consulta a lista novamente
- **THEN** a interface apresenta os fatos atuais e a anotação preservada retornada pela API, sem recalcular ou substituir a organização pessoal

### Requirement: Leitura segura, estados e recuperação
A lista SHALL exigir sessão válida, rejeitar respostas tardias de outra geração de sessão e distinguir carregamento, ausência, filtro vazio, falha de rede, falta de permissão e indisponibilidade com mensagens em português. Consultar, filtrar, paginar, reler ou editar anotações SHALL não iniciar sincronização, nem criar sessão ou expor corpo bruto de falha.

#### Scenario: Sessão encerrada com consulta pendente
- **WHEN** a sessão termina durante consulta, criação de categoria ou edição de anotação
- **THEN** a interface remove dados privados, não aplica resposta tardia e exige novo login para acesso à lista

#### Scenario: Falha transitória e recuperação
- **WHEN** a consulta falha por rede ou indisponibilidade e mais tarde volta a responder
- **THEN** a tela explica a falha, oferece repetição apenas da leitura e mostra os novos dados sem POST de sincronização

#### Scenario: Negativa e resposta não reconhecida
- **WHEN** a API responde 403, 400 ou uma resposta de sucesso incompatível com o contrato
- **THEN** a interface apresenta respectivamente falta de permissão, orientação sobre filtros ou erro seguro de resposta, sem ecoar payload externo ou classificá-los como credencial inválida

### Requirement: Apresentação acessível sem agregado monetário
A lista SHALL ser operável por teclado em telas pequenas e grandes, identificar controles e estados por texto e foco visível e apresentar somente valores monetários de cada movimento. SHALL NOT calcular ou renderizar total, subtotal, média, série ou gráfico monetário, inclusive sob rótulo parcial ou informativo.

#### Scenario: Uso com teclado e largura reduzida
- **WHEN** o titular percorre filtros, páginas, criação e edição usando teclado em celular ou desktop
- **THEN** cada controle e feedback é alcançável e identificável sem depender de cor ou perder dados essenciais por corte

#### Scenario: Valores mistos e cobertura parcial
- **WHEN** a lista contém valores de sinais/moedas distintos e cobertura parcial ou desconhecida
- **THEN** mostra cada valor com sua moeda e a limitação de cobertura, sem produzir ou apresentar qualquer valor derivado da combinação de movimentos
