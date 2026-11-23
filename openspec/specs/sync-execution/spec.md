# sync-execution Specification

## Purpose

Descreve a aceitação, o acompanhamento e a execução recuperável de sincronizações financeiras, preservando exclusão mútua, idempotência e estado verificável diante de duplicatas, falhas e reinícios.

## Requirements

### Requirement: Aceitação persistente e resposta honesta
Uma solicitação autorizada SHALL persistir atomicamente uma única `SyncRun`, sua janela e a obrigação de entrega antes de responder como aceita. Cada intenção SHALL usar uma chave de idempotência estável, vinculada ao titular e à conexão, para resolver repetição e resultado de commit incerto. A resposta `202` SHALL referenciar a execução e não SHALL significar entrega ou conclusão da importação.

#### Scenario: Solicitação aceita
- **GIVEN** o titular autenticado solicita sincronização de uma conexão que lhe pertence
- **WHEN** a criação da execução e sua obrigação durável são confirmadas
- **THEN** a API responde `202` com a referência da `SyncRun` persistida, ainda que a entrega ou importação não tenha ocorrido

#### Scenario: Falha antes da confirmação da aceitação
- **GIVEN** uma solicitação autenticada e uma falha ao persistir a execução ou sua obrigação de entrega
- **WHEN** a transação de aceitação é revertida
- **THEN** a API não responde `202` e não deixa uma execução parcialmente aceita

#### Scenario: Rollback confirmado da aceitação
- **GIVEN** nenhuma `SyncRun` ou obrigação foi persistida para a chave de idempotência
- **WHEN** o banco confirma que a transação de aceitação sofreu rollback
- **THEN** a resposta informa que o pedido não foi aceito e a repetição com a mesma chave pode criar uma única `SyncRun`

#### Scenario: Resultado do commit de aceitação desconhecido
- **GIVEN** a conexão é interrompida durante a confirmação e o cliente não sabe se o commit foi concluído
- **WHEN** a API não consegue provar commit nem rollback
- **THEN** o resultado é informado como indeterminado, sem afirmar que não existe execução, e uma repetição com a mesma chave converge para a `SyncRun` e obrigação já gravadas ou cria exatamente um par se o rollback for confirmado

#### Scenario: Chave de idempotência reutilizada com outro pedido
- **GIVEN** uma chave já está vinculada a uma execução aceita para titular e conexão
- **WHEN** a chave é repetida com titular, conexão ou janela incompatível
- **THEN** a API rejeita a reutilização sem alterar ou expor a execução previamente vinculada

### Requirement: Solicitações concorrentes convergem
Solicitações concorrentes para a mesma conexão SHALL convergir para no máximo uma execução ativa, com decisão atômica e referência consultável; a mesma chave de idempotência SHALL sempre identificar a mesma execução, inclusive após seu término, e não SHALL produzir uma execução terminal paralela.

#### Scenario: Duas solicitações simultâneas
- **GIVEN** não há sincronização ativa para uma conexão
- **WHEN** duas solicitações concorrentes tentam iniciar sua sincronização
- **THEN** somente uma `SyncRun` ativa é criada e ambas as respostas convergem para o resultado de aceitação definido para essa execução, sem iniciar duas importações

#### Scenario: Repetição após resposta perdida
- **GIVEN** a aceitação da `SyncRun` foi confirmada, mas o cliente não recebeu a resposta
- **WHEN** o cliente repete a solicitação com a mesma chave de idempotência
- **THEN** a API retorna a mesma `SyncRun`, ativa ou terminal, e não cria outra execução nem obrigação financeira

#### Scenario: Intenção distinta enquanto há execução ativa
- **GIVEN** já existe uma `SyncRun` ativa para a conexão e chega uma intenção com outra chave de idempotência
- **WHEN** a API decide atomicamente a aceitação concorrente
- **THEN** não cria uma segunda `SyncRun` e informa o conflito com a execução ativa sem declarar a nova intenção como aceita

### Requirement: Tentativas reutilizam a execução e a janela aceitas
Cada tentativa SHALL carregar a mesma `SyncRunId`, `RequestedFrom` e `RequestedToExclusive` aceitas e persistidas. O importador não SHALL recalcular a janela nem criar registro terminal paralelo para sucesso, falha ou cancelamento de uma tentativa.

#### Scenario: Retomada usa janela original
- **GIVEN** a `SyncRun` aceita tem janela persistida e uma tentativa anterior terminou sem concluir
- **WHEN** uma nova tentativa retoma essa execução
- **THEN** a coleta usa a janela original e atualiza a mesma `SyncRun`, sem criar outro registro de execução

#### Scenario: Promoção atualiza a execução aceita
- **GIVEN** a coleta completa pertence à `SyncRun` aceita e sua tentativa ainda possui fencing vigente
- **WHEN** a promoção confirma movimentos e sucesso
- **THEN** movimentos, horários da conexão e sucesso são confirmados na mesma transação, associados à `SyncRunId` aceita

#### Scenario: Orçamento da tentativa abrange coleta e promoção
- **GIVEN** a tentativa adquiriu posse persistente antes da primeira chamada à origem
- **WHEN** ela coleta páginas, valida o snapshot e promove o resultado
- **THEN** seus até 10 minutos são contados desde a aquisição confirmada até o commit conhecido da promoção ou, se esse não ocorrer, até o deadline que interrompe o trabalho de negócio; incluem chamadas HTTP e banco, não reiniciam por página ou na promoção, e não permitem sucesso financeiro após expiração. Os 11 minutos da entrega começam no recebimento pelo consumer: se a aquisição consumir a margem necessária para recuperação e ack, o trabalho efetivo é encurtado ou diferido duravelmente sem começar coleta; nunca se somam 10 minutos a uma espera anterior sem considerar o limite interno da entrega

### Requirement: Consulta de execução protegida e sanitizada
A API SHALL permitir que somente o titular consulte sua `SyncRun` e SHALL retornar estado, horários, contadores conhecidos, cobertura e código de falha interno permitido, sem credenciais, payload bruto ou mensagens externas.

#### Scenario: Titular consulta a execução
- **GIVEN** uma `SyncRun` pertence ao titular autenticado
- **WHEN** esse titular consulta seu identificador
- **THEN** a resposta apresenta somente estado e metadados operacionais sanitizados disponíveis

#### Scenario: Execução inexistente ou de outro titular
- **GIVEN** um identificador de `SyncRun` inexistente ou pertencente a outro titular
- **WHEN** o titular autenticado consulta esse identificador
- **THEN** a resposta não revela a existência nem os dados da execução alheia

### Requirement: Posse única com fencing
No máximo um consumidor SHALL possuir a execução aceita de uma conexão por vez. Cada tentativa SHALL atualizar a mesma `SyncRun` e manter posse expirável; a promoção financeira SHALL validar fencing vigente na mesma transação que confirma o resultado, impedindo gravação ou sucesso por tentativa antiga.

#### Scenario: Consumidor perde a posse
- **GIVEN** um consumidor iniciou a coleta e sua posse expirou ou foi substituída
- **WHEN** esse consumidor tenta persistir o resultado final
- **THEN** a gravação é recusada e somente o proprietário vigente pode confirmar o resultado da `SyncRun`

#### Scenario: Mensagem duplicada durante posse ativa
- **GIVEN** uma execução está sendo processada por um consumidor com posse válida
- **WHEN** outra entrega da mesma `SyncRun` chega
- **THEN** a segunda entrega não inicia uma importação concorrente nem descarta silenciosamente a obrigação de recuperação

#### Scenario: Renovação da posse durante coleta ou promoção
- **GIVEN** um consumidor mantém lease por conexão de 120 segundos, renovável a cada 30 segundos durante coleta e promoção
- **WHEN** a renovação independente falha, expira, perde o fencing ou ultrapassa o deadline da tentativa
- **THEN** a tentativa interrompe novas páginas, não promove resultado e deixa a execução recuperável; movimentos e sucesso só são confirmados se a transação de promoção verificar posse, geração e deadline vigentes

### Requirement: Execução idempotente e sucesso somente após promoção
O consumidor SHALL passar a `SyncRunId` aceita ao importador FNR-012, confirmar sucesso somente após coleta completa e promoção consistente, e tratar reentregas da mesma execução como no-op sem novo efeito financeiro ou criação de outra `SyncRun` terminal.

#### Scenario: Reentrega depois de sucesso confirmado
- **GIVEN** uma `SyncRun` terminou com sucesso e a confirmação da mensagem foi perdida
- **WHEN** a mensagem da mesma execução é entregue novamente
- **THEN** o consumidor confirma a duplicata sem repetir efeitos financeiros nem alterar o resultado concluído

#### Scenario: Falha antes da promoção completa
- **GIVEN** existe um histórico anteriormente confirmado
- **WHEN** a coleta ou validação falha antes da promoção integral
- **THEN** o histórico anterior permanece válido, a `SyncRun` não é marcada como sucesso e o último sucesso da conexão não avança

### Requirement: Falhas limitadas e observáveis
Falhas SHALL ser classificadas sem registrar texto externo; falhas transitórias SHALL atualizar tentativa/agenda na mesma `SyncRun` com orçamento persistente e limitado, enquanto autorização inválida e contrato inválido SHALL encerrar essa mesma execução sem retry automático. Cancelamento/shutdown antes da conclusão SHALL deixar a execução recuperável e não criar `SyncRun` terminal paralela. Esgotamento SHALL deixar resultado terminal observável e não descartar obrigação sem confirmação durável.

#### Scenario: Falha transitória repetida através de reinício
- **GIVEN** uma falha transitória ocorreu e a execução ainda possui orçamento de tentativas
- **WHEN** a API reinicia antes da próxima tentativa
- **THEN** a tentativa pode ser retomada com o orçamento já persistido, sem reiniciar indefinidamente a contagem

#### Scenario: Espera por backoff ou Retry-After não retém a entrega
- **GIVEN** uma das três tentativas totais falhou transitoriamente e existe backoff de 1 ou 5 minutos ou Retry-After válido de até 15 minutos
- **WHEN** a próxima tentativa ainda não é elegível
- **THEN** a mesma `SyncRun` registra orçamento, instante elegível e obrigação recuperável antes de confirmar a entrega anterior; não há espera unacked durante o intervalo, nem nova tentativa antes do prazo exigido pela origem

#### Scenario: Retry-After excede o teto aprovado
- **GIVEN** a origem exige Retry-After superior a 15 minutos
- **WHEN** a execução avalia a repetição automática
- **THEN** não trunca o prazo nem repete automaticamente; registra impedimento observável e exige intervenção ou replay explícito conforme política aprovada, sem iniciar uma execução paralela

#### Scenario: Limite de ack controlado pela aplicação com fila classic
- **GIVEN** a fila aprovada é classic no RabbitMQ 4.3, que não aplica timeout de ack do broker a esse tipo de fila
- **WHEN** a tentativa chega ao teto de 10 minutos
- **THEN** a aplicação interrompe trabalho financeiro e tenta concluir cancelamento, persistência e confirmação da entrega até seu limite interno de 11 minutos; se não puder comprovar handoff durável, não afirma ack nem conclusão, deixando redelivery/reconciliação recuperar a obrigação

#### Scenario: Falha terminal ou orçamento esgotado
- **GIVEN** a origem exige nova autorização, a resposta é inválida ou o orçamento limitado de tentativas se esgotou
- **WHEN** a execução é encerrada sem sucesso
- **THEN** a mesma `SyncRun` aceita registra código interno sanitizado e estado terminal, preserva os dados anteriores e não entra em retry automático contínuo nem cria outro registro terminal

#### Scenario: Falha de tentativa atualiza a mesma execução
- **GIVEN** uma tentativa falha antes de promover o snapshot completo
- **WHEN** a falha é transitória, terminal ou excede o orçamento conforme sua classificação
- **THEN** contadores, tentativa, próxima ação e eventual estado final são registrados na `SyncRun` aceita, sem criar `SyncRun` separada para a falha

#### Scenario: Falha transitória ou cancelamento antes da promoção
- **GIVEN** uma tentativa da `SyncRun` aceita falha transitoriamente ou recebe cancelamento/shutdown antes da promoção
- **WHEN** a tentativa termina sem confirmar resultado financeiro
- **THEN** a mesma `SyncRun` registra tentativa e próxima ação recuperável, ou estado terminal somente quando a política aprovada assim determinar, sem avançar último sucesso nem criar uma execução terminal paralela

### Requirement: Retomada recuperável após reinício e shutdown
Após reinício, execuções pendentes ou com posse expirada SHALL permanecer detectáveis e recuperáveis sem depender de tarefa apenas em memória. No shutdown, novos consumos SHALL parar e o trabalho interrompido SHALL continuar recuperável sem sucesso ou falha de negócio artificial.

#### Scenario: API reinicia com obrigação não entregue
- **GIVEN** a aceitação foi confirmada e a mensagem ainda aguarda entrega durável
- **WHEN** a API reinicia e volta a executar
- **THEN** a obrigação permanece detectável e pode ser entregue sem criar outra `SyncRun`

#### Scenario: API encerra durante coleta
- **GIVEN** um consumidor está coletando dados e a API inicia shutdown
- **WHEN** o consumidor recebe cancelamento antes da promoção
- **THEN** não confirma sucesso artificial, preserva o conjunto anterior e deixa a mesma `SyncRun` aceita recuperável conforme a política persistida, sem criar registro terminal separado

### Requirement: Limites de transação e isolamento do transporte
A execução SHALL manter chamadas HTTP externas fora de transações PostgreSQL longas, SHALL confirmar dados e sucesso em transação curta condicionada à posse vigente e SHALL separar indisponibilidade do broker de autenticação e leituras que não dependem de consumo.

#### Scenario: Chamadas externas durante a execução
- **GIVEN** uma execução precisa buscar páginas da origem financeira
- **WHEN** o consumidor realiza as chamadas HTTP externas
- **THEN** não mantém uma transação PostgreSQL aberta durante a comunicação com a origem

#### Scenario: Broker indisponível
- **GIVEN** o broker está indisponível, mas a API e o PostgreSQL estão acessíveis
- **WHEN** um titular consulta autenticação ou dados já persistidos
- **THEN** esses fluxos não são bloqueados pela inicialização ou disponibilidade do consumidor de sincronização
