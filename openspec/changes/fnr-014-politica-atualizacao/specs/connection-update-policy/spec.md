# Spec Delta

## Purpose

Define quando uma conexão própria pode receber uma atualização solicitada pelo titular e como seu estado de importação é informado sem confundir dados antigos, falha e atualização na origem.

## ADDED Requirements

### Requirement: Entrada autenticada avalia antiguidade no servidor
A elegibilidade de uma solicitação automática SHALL usar o último sucesso completo da conexão e o instante do servidor: sem sucesso prévio ou com sucesso anterior a 24 horas, a conexão é elegível; com sucesso de até 24 horas, não é. O limiar SHALL ser configurável sem depender de relógio do cliente.

#### Scenario: Sem sucesso anterior
- **WHEN** o titular autenticado solicita a avaliação de entrada de sua conexão sem importação completa anterior
- **THEN** a política informa que os dados precisam de atualização, sujeita a execução ativa e intervalo mínimo

#### Scenario: Limiar exato
- **GIVEN** o limiar configurado é de 24 horas
- **WHEN** o último sucesso completo ocorreu há exatamente 24 horas
- **THEN** a política não o considera antigo; após o limiar, considera-o antigo

#### Scenario: Execução falha não renova o sucesso
- **GIVEN** o último sucesso já ultrapassou o limiar
- **WHEN** uma execução posterior termina em falha ou ainda não concluiu a promoção
- **THEN** a conexão permanece antiga e o instante do último sucesso não avança

### Requirement: Intentos compartilham intervalo mínimo por conexão
Uma nova intenção automática ou manual SHALL respeitar o mesmo intervalo configurável de cinco minutos por conexão desde a última intenção nova aceita, inclusive quando essa execução falhou. O bloqueio SHALL informar o próximo instante permitido e SHALL não criar nova execução. Repetição da mesma intenção por chave idempotente SHALL não reiniciar o intervalo.

#### Scenario: Pedido manual após falha
- **GIVEN** uma nova intenção foi aceita e a execução falhou
- **WHEN** o titular solicita atualização manual da mesma conexão antes de transcorridos cinco minutos
- **THEN** o pedido é recusado com o próximo instante permitido e não cria outra execução

#### Scenario: Entrada e botão compartilham o limite
- **GIVEN** a entrada autenticada aceitou uma execução e os dados seguem antigos
- **WHEN** chega uma intenção manual diferente antes do intervalo configurado
- **THEN** a nova intenção é bloqueada pela mesma política, sem consulta à origem

#### Scenario: Limiar do intervalo
- **WHEN** chega uma nova intenção no instante exato em que termina o intervalo configurado, sem execução ativa
- **THEN** ela pode ser avaliada para aceitação, respeitando as outras condições aplicáveis

#### Scenario: Repetição após resposta perdida
- **GIVEN** uma intenção foi aceita e sua resposta não chegou ao cliente
- **WHEN** o cliente repete a mesma chave e intenção durante o intervalo mínimo
- **THEN** recebe a mesma referência da execução, sem novo prazo e sem nova importação

### Requirement: Estado da conexão é explícito e protegido
O titular autenticado SHALL poder consultar o último sucesso completo no Finora, a última tentativa conhecida, a atualização na origem somente se disponível, o estado da execução pertinente e a próxima oportunidade de solicitação. A resposta SHALL distinguir indisponível de zero, dados antigos de falha, e solicitação aceita de importação concluída.

#### Scenario: Sucesso anterior seguido de falha
- **GIVEN** existe histórico confirmado e uma execução mais recente falhou
- **WHEN** o titular consulta o estado da conexão
- **THEN** vê o sucesso antigo preservado e a falha sanitizada da execução recente, sem indicação de novo sucesso

#### Scenario: Fonte não informou atualização
- **WHEN** não existe horário confiável de atualização na origem
- **THEN** a resposta deixa esse campo ausente e não o substitui pelo horário da consulta ou da importação

#### Scenario: Reautorização necessária
- **GIVEN** uma execução terminou com código interno de autorização necessária
- **WHEN** o titular consulta o estado ou avalia nova entrada automática
- **THEN** recebe orientação sanitizada para renovar o acesso no provedor e nenhuma repetição automática contínua é criada; após intervenção, o pedido manual ainda obedece ao intervalo

#### Scenario: Propriedade e ausência
- **WHEN** o usuário não está autenticado ou não possui conexão única consultável
- **THEN** a resposta não expõe dados, identificadores externos, credenciais ou histórico de outro titular

### Requirement: Leitura não inicia sincronização
Consultar o estado, navegar e filtrar SHALL somente ler dados persistidos; nenhuma dessas operações SHALL criar nova execução ou disparar leitura externa. Uma atualização automática SHALL exigir intenção explícita associada à entrada autenticada e não SHALL depender de tarefa agendada com a aplicação parada.

#### Scenario: Navegação e filtro
- **WHEN** o titular consulta estado e depois altera filtros ou páginas de movimentos
- **THEN** nenhuma nova sincronização é criada por essas leituras

#### Scenario: API em repouso
- **WHEN** não há entrada autenticada nem ação manual
- **THEN** a política não agenda sincronizações financeiras em segundo plano
