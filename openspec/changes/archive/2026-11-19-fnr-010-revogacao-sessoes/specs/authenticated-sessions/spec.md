# Spec Delta

## ADDED Requirements

### Requirement: Logout restrito à sessão autenticada
O sistema SHALL oferecer `POST /api/auth/logout` protegido por Bearer, sem exigir corpo nem aceitar seleção de titular ou sessão por dados do cliente. SHALL revogar somente a sessão autenticada, preservar as demais e retornar HTTP 204 sem corpo apenas após confirmação da gravação. Todas as respostas de logout SHALL usar `Cache-Control: no-store`.

#### Scenario: Logout confirmado com duas sessões
- **GIVEN** dois logins válidos criaram sessões distintas do titular
- **WHEN** o titular solicita logout com o token de uma delas
- **THEN** recebe 204 sem corpo após confirmação da revogação dessa sessão
- **AND** uma chamada protegida subsequente com esse token retorna 401, enquanto a outra sessão continua autorizando acesso

#### Scenario: Identificadores fornecidos pelo cliente
- **WHEN** uma chamada autenticada de logout inclui em query ou corpo identificadores de outra sessão ou titular
- **THEN** esses valores não selecionam o alvo nem provocam revogação de outra sessão
- **AND** somente a sessão autenticada pode ser revogada

#### Scenario: Token ausente ou inválido
- **WHEN** logout é solicitado sem Bearer ou com token expirado, adulterado, revogado, sem sessão ou incompatível com ela
- **THEN** retorna 401 sanitizado com challenge Bearer, sem executar a mutação de logout

#### Scenario: Repetição após confirmação
- **WHEN** uma nova chamada de logout reutiliza o token de uma sessão já revogada por logout confirmado
- **THEN** retorna 401, sem desfazer ou alterar a revogação já registrada

#### Scenario: Duas chamadas admitidas antes da revogação
- **WHEN** duas chamadas de logout da mesma sessão passam pela autenticação antes da primeira revogação e ambas completam sem falha nem expiração
- **THEN** ambas podem retornar 204 ao confirmar a sessão revogada, sem sobrescrever a data da primeira revogação
- **AND** uma chamada nova iniciada após essa confirmação recebe 401

### Requirement: Confirmação confiável da revogação remota
O logout SHALL não anunciar confirmação em falha operacional, timeout ou resultado de commit incerto. Falhas de banco, schema ou configuração necessárias à operação SHALL retornar 503 sanitizado, sem challenge de credencial incorreta. Falha anterior ao commit SHALL não publicar revogação parcial; resultado incerto SHALL não ser descrito como rollback garantido.

#### Scenario: Falha antes da confirmação
- **WHEN** o logout sofre falha operacional antes de tentar o commit após iniciar a alteração da sessão
- **THEN** responde 503 sem confirmar encerramento e a alteração não é publicada
- **AND** a sessão anterior conserva seu estado, sujeito à expiração normal

#### Scenario: Falha ao receber confirmação do commit
- **WHEN** o banco confirma fisicamente a revogação mas a resposta do commit se perde
- **THEN** o logout retorna 503 em vez de 204
- **AND** não promete que a sessão continua ativa, não anuncia rollback e não repete automaticamente a operação

#### Scenario: Dependência indisponível
- **WHEN** a autenticação da sessão ou a gravação do logout encontra banco ou schema indisponível
- **THEN** retorna 503 sem executar ação protegida de negócio, revelar configuração ou simular senha incorreta

#### Scenario: Token falsificado antes do banco
- **WHEN** um token de logout pode ser rejeitado criptográfica ou estruturalmente antes de consultar um banco indisponível
- **THEN** retorna 401, preservando a distinção entre token inválido e falha operacional

### Requirement: Ordem entre login e redefinição de senha
Um reset confirmado SHALL invalidar todas as sessões anteriores do titular, incluindo sessão de login concorrente validado com a credencial antiga. Um login efetivado depois do reset SHALL revalidar a credencial atual; SHALL não criar sessão com credencial obsoleta. Novo login válido após o reset SHALL poder criar sessão independente, respeitando o bloqueio de login existente.

#### Scenario: Login confirmado antes do reset
- **WHEN** o login com senha antiga confirma a sessão antes de um reset concorrente ser confirmado
- **THEN** o reset também revoga essa sessão
- **AND** o token eventualmente recebido desse login não autoriza acesso após o reset

#### Scenario: Reset confirmado antes do login
- **WHEN** um login iniciado com senha antiga aguarda enquanto o reset confirma a nova senha
- **THEN** o login reavalia a credencial atual e retorna 401 sem criar sessão com a senha antiga

#### Scenario: Login posterior com senha nova
- **WHEN** o reset terminou e um titular não bloqueado faz login com a nova senha válida
- **THEN** uma nova sessão pode ser confirmada e seu token autoriza acesso, sem reativar sessões revogadas

### Requirement: Efeito observável da revogação entre chamadas
Após logout ou reset confirmado, toda autenticação de sessão afetada iniciada depois dessa confirmação SHALL negar acesso com 401, inclusive em outra instância com mesmo banco e chave. A revogação SHALL persistir após reinicialização. O sistema SHALL não prometer cancelamento retroativo de requisições cuja autenticação já tenha terminado antes da confirmação.

#### Scenario: Outro host e reinicialização
- **WHEN** um token revogado por logout ou reset é usado em outro host ou em host reiniciado com o mesmo banco e chave
- **THEN** uma nova chamada protegida retorna 401 sem executar a ação, sem depender de cache local ou rotação da chave

#### Scenario: Requisição já autenticada
- **WHEN** uma requisição concluiu autenticação antes da confirmação da revogação e ainda está em andamento
- **THEN** a revogação não é apresentada como cancelamento retroativo dessa requisição
- **AND** nova autenticação iniciada após a confirmação recusa o token afetado

## MODIFIED Requirements

### Requirement: Diagnóstico e comandos preservados
A autenticação SHALL preservar `GET /health` anônimo, HTTP 200 e corpo mínimo, sem exigir banco, provedor ou configuração JWT. SHALL não executar migrations/seed no startup nem exigir JWT nos comandos administrativos. SHALL oferecer logout protegido, mas não reset HTTP ou cadastro público. O reset administrativo SHALL confirmar revogação somente junto à redefinição transacional da senha.

#### Scenario: Health com dependências ausentes
- **WHEN** a API inicia sem banco, chave JWT ou Pluggy e recebe `GET /health`
- **THEN** retorna somente `{"status":"ok"}` com HTTP 200, sem consultar dependências

#### Scenario: Comandos administrativos sem JWT
- **WHEN** um comando administrativo válido é executado com banco e schema configurados, mas sem JWT
- **THEN** conserva seu isolamento e os códigos de saída existentes
- **AND** reset bem-sucedido informa revogação confirmada, sem depender de configuração JWT ou listener HTTP

### Requirement: Saídas e evidências sem segredos
O sistema SHALL não registrar senha, JWT, cabeçalho Authorization, hash, e-mail ou connection string em logs e erros de autenticação. Erros SHALL usar mensagens fixas com status e identificador de rastreio, sem exceções brutas. A documentação SHALL distinguir evidências históricas de FNR-008/009 da revogação FNR-010 e preservar os gates remanescentes de RF14 e publicação.

#### Scenario: Dados sensíveis em falhas
- **WHEN** login, validação, logout, reset ou configuração falham com sentinelas sensíveis nos dados de teste
- **THEN** logs, saídas administrativas e respostas de erro não contêm essas sentinelas

#### Scenario: Avaliação da entrega
- **WHEN** FNR-009 é avaliada como evidência histórica isolada
- **THEN** sua prova de leitura de estado revogado não é convertida em evidência de logout/reset transacional
- **AND** a documentação da FNR-010 exige evidências próprias e mantém FNR-011/020 e demais gates antes de declarar RF14 completo ou publicar
