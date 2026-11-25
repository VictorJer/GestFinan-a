# web-card-connection Specification

## Purpose

Permitir ao titular acompanhar seus cartões persistidos e o estado verdadeiro da conexão no navegador, solicitar atualizações elegíveis e distinguir aceitação, importação concluída e falhas sem expor credenciais ou dados fictícios como reais.

## Requirements

### Requirement: Cartões próprios disponíveis na área autenticada
A área protegida SHALL apresentar os cartões retornados pela API para a sessão atual usando somente nome, final mascarado e moeda disponíveis, sem número completo, identificação externa, limite presumido ou gasto calculado. Lista vazia SHALL ser distinguida de falha de consulta e de ausência comprovada de cartões na origem.

#### Scenario: Cartões consultáveis
- **GIVEN** o titular possui uma sessão válida e a API retorna cartões próprios
- **WHEN** abre a visão de cartão
- **THEN** vê somente os campos de apresentação disponíveis para cada cartão, sem dados financeiros calculados ou credenciais

#### Scenario: Sem cartões persistidos
- **WHEN** a consulta protegida retorna uma lista vazia
- **THEN** a tela informa que não há cartões disponíveis nos dados persistidos e não afirma que a origem não possui cartões

#### Scenario: Dados de apresentação incompletos
- **WHEN** a origem não informa nome ou final de um cartão
- **THEN** a tela mostra indisponibilidade desses campos sem inventar final, bandeira ou limite

### Requirement: Estado da conexão com horários e cobertura honestos
A visão SHALL apresentar separadamente o último sucesso completo no Finora, a última tentativa, o horário da origem apenas quando informado, a execução recente e a cobertura conhecida, sem representar dado ausente como zero, importação completa do banco ou sucesso novo após uma falha.

#### Scenario: Sucesso anterior e falha recente
- **GIVEN** existe último sucesso completo e uma execução posterior falhou
- **WHEN** o titular consulta o estado da conexão
- **THEN** vê o horário antigo de sucesso preservado, a falha recente e a cobertura disponível, sem anúncio de atualização concluída

#### Scenario: Origem ou cobertura desconhecida
- **WHEN** o estado da API não informa horário da origem ou cobertura confirmada
- **THEN** a tela rotula esses dados como não disponíveis, sem substituí-los pelo horário de acesso ou declarar histórico integral

#### Scenario: Conexão inexistente ou indisponível
- **WHEN** a consulta de estado recebe ausência de conexão própria ou falha operacional
- **THEN** a tela informa a condição sem mostrar conexão fictícia, não apaga histórico já exibido por uma falha transitória e oferece ação de nova leitura quando cabível

### Requirement: Uma avaliação automática por entrada autenticada
Uma entrada autenticada SHALL enviar uma intenção explícita de avaliação ao servidor uma vez por sessão de login, sem depender de relógio local para decidir antiguidade. Reabrir a visão, consultar estado ou navegar SHALL não criar outra intenção automática; uma nova sessão autenticada pode iniciar nova avaliação.

#### Scenario: Login e entrada na área protegida
- **WHEN** o titular conclui login e entra na área autenticada
- **THEN** a aplicação solicita a avaliação de entrada uma vez e exibe a decisão do servidor, sujeita à política de execução ativa e intervalo mínimo

#### Scenario: Navegação repetida
- **GIVEN** a avaliação já foi enviada na sessão atual
- **WHEN** o titular sai da visão e volta, consulta o estado ou navega entre páginas
- **THEN** nenhuma nova intenção automática é enviada por essas leituras ou navegações

#### Scenario: Atualização dispensada ou reautorização exigida
- **WHEN** a avaliação retorna `not_required` ou `reauthorization_required`
- **THEN** a tela informa respectivamente que não há atualização automática necessária ou que é preciso renovar acesso no provedor, sem afirmar nova importação ou repetir automaticamente a solicitação

### Requirement: Pedido manual único e tratamento de bloqueios
O titular SHALL poder pedir uma atualização manual com intenção diferente da entrada, sujeito à decisão do servidor. A tela SHALL evitar pedidos concorrentes pelo botão, explicar execução ativa ou intervalo mínimo quando conhecidos e não tratar bloqueio, falta de permissão ou falha de rede como sucesso.

#### Scenario: Pedido aceito
- **WHEN** o titular aciona o botão manual e o servidor responde `202` com identificador de execução
- **THEN** a tela informa solicitação aceita, inicia o acompanhamento dessa execução e não anuncia dados atualizados

#### Scenario: Botão acionado durante pedido em andamento
- **WHEN** o titular tenta acionar novamente o botão enquanto uma intenção manual aguarda decisão ou uma execução ativa já está sendo acompanhada
- **THEN** a interface não envia uma segunda intenção concorrente

#### Scenario: Intervalo mínimo ou execução ativa
- **WHEN** o servidor recusa nova intenção por intervalo mínimo ou execução ativa
- **THEN** a tela informa o bloqueio e, se fornecidos, o próximo instante permitido ou a execução existente, sem criar nova intenção por conta própria

### Requirement: Aceitação idempotente e resultado incerto
Cada intenção SHALL manter uma chave idempotente estável nas repetições da mesma intenção e SHALL usar outra chave para uma intenção nova ou de outro modo. Após timeout ou confirmação indeterminada, a interface SHALL não afirmar rejeição ou sucesso e SHALL oferecer tentativa de reconciliação da mesma intenção sem gerar uma duplicata silenciosa.

#### Scenario: Resposta perdida após aceite
- **GIVEN** o titular enviou uma intenção e a resposta não chegou
- **WHEN** ele solicita repetição da mesma intenção na sessão ainda válida
- **THEN** a aplicação reenvia a mesma chave e modo, sem criar nova intenção com outra chave

#### Scenario: Aceite indeterminado
- **WHEN** o servidor sinaliza `ACCEPTANCE_RESULT_INDETERMINATE` ou a resposta se perde durante o pedido
- **THEN** a tela informa que o resultado não foi confirmado e não habilita um novo pedido independente antes de resolver ou encerrar a tentativa pendente

#### Scenario: Repetição resolvida
- **WHEN** o replay idempotente retorna uma execução previamente aceita
- **THEN** a tela acompanha essa execução sem declarar que outra importação foi iniciada

### Requirement: Acompanhamento limitado e dados preservados
Uma execução aceita SHALL ser consultada por identificador próprio até estado terminal, expiração da sessão ou limite de acompanhamento; a tela SHALL permitir atualizar o estado sem reenviar POST. Somente conclusão confirmada SHALL atualizar o horário de sucesso; falha, espera e perda de contato SHALL preservar os dados anteriores e oferecer orientação honesta.

#### Scenario: Execução ainda ativa
- **WHEN** a consulta protegida indica execução em andamento ou aguardando retomada
- **THEN** a tela informa o estado sem confundir aceitação com importação completa e não dispara nova sincronização

#### Scenario: Execução concluída
- **WHEN** a execução termina com sucesso e a nova leitura de estado confirma o horário de sucesso
- **THEN** a tela apresenta a conclusão e o estado atualizado, sem inferir cobertura além do informado

#### Scenario: Falha ou limite de acompanhamento
- **WHEN** a execução falha, a consulta fica indisponível ou o limite de acompanhamento é alcançado
- **THEN** a tela preserva os dados já exibidos, informa que não há confirmação de novo sucesso e permite leitura manual do estado sem novo POST

#### Scenario: Sessão encerrada durante acompanhamento
- **WHEN** o titular sai, a sessão expira ou é revogada enquanto há consultas pendentes
- **THEN** a tela deixa de mostrar dados protegidos e respostas atrasadas não restauram cartões nem iniciam novas consultas

### Requirement: Estados compreensíveis, acessíveis e privados
A visão SHALL identificar carregamento, ausência, erro e recuperação em português, com controles operáveis por teclado e atualização textual acessível. SHALL não expor token, chave idempotente, identificador externo, resposta bruta, senha bancária nem dados simulados como reais.

#### Scenario: Falha e retorno de leitura
- **WHEN** a API de cartões ou de estado falha e posteriormente volta a responder
- **THEN** o titular encontra mensagem sanitizada, ação de repetir leitura e estados atualizados sem que a leitura provoque importação

#### Scenario: Teclado e informações sensíveis
- **WHEN** o titular navega e pede atualização com teclado mesmo diante de falha HTTP com corpo externo
- **THEN** botão, mensagens e foco permanecem utilizáveis e não exibem conteúdo bruto, credenciais ou chave de idempotência
