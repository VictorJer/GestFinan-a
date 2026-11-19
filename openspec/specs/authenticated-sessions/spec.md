# authenticated-sessions Specification

## Purpose

Autenticar o titular por credenciais e condicionar cada acesso protegido à validade de um token assinado e de sua sessão persistida, com falhas distinguíveis e sem exposição de segredos.

## Requirements

### Requirement: Login exclusivo do titular
O sistema SHALL oferecer `POST /api/auth/login` com e-mail e senha, sem cadastro público, cookies ou refresh token. SHALL comparar e-mail ignorando caixa e espaços externos, preservar a senha exatamente e rejeitar credenciais inválidas com HTTP 401 genérico, sem revelar a existência da conta.

#### Scenario: Credenciais válidas
- **WHEN** o titular envia credenciais válidas e não está bloqueado
- **THEN** recebe HTTP 200 com `accessToken`, `tokenType` igual a `Bearer`, `expiresIn` igual a 1800 e `expiresAtUtc`, após confirmação da sessão
- **AND** a resposta tem `Cache-Control: no-store`, sem cookie ou refresh token

#### Scenario: Entrada inválida e credencial incorreta
- **WHEN** o corpo é inválido ou falta e-mail/senha
- **THEN** recebe HTTP 400 sanitizado, sem refletir valores recebidos

#### Scenario: Conta desconhecida ou senha errada
- **WHEN** o corpo é válido mas o e-mail é desconhecido ou a senha é incorreta
- **THEN** recebe o mesmo HTTP 401 e corpo genérico, sem sessão ou token

#### Scenario: Normalização restrita ao e-mail
- **WHEN** o e-mail correto difere somente em caixa/espaços externos e a senha mantém seu conteúdo exato
- **THEN** a credencial é avaliada para o mesmo titular, sem remover espaços da senha

### Requirement: Bloqueio temporário de novas autenticações
O sistema SHALL bloquear novos logins da conta por cinco minutos após cinco falhas de senha contabilizadas. Sucesso SHALL zerar o contador. O bloqueio SHALL persistir entre reinicializações, não revogar sessões existentes e não ser prolongado por tentativas durante sua vigência. Conta bloqueada SHALL receber o mesmo 401 genérico de credencial inválida.

#### Scenario: Limiar de bloqueio
- **WHEN** ocorrem cinco falhas para a conta e depois é enviada a senha correta antes de cinco minutos
- **THEN** nenhum token é entregue, inclusive para a senha correta, e as tentativas durante o bloqueio não prorrogam seu término

#### Scenario: Expiração e sucesso
- **WHEN** o bloqueio termina e o titular envia a senha correta
- **THEN** o login pode completar e o contador de falhas volta a zero

#### Scenario: Contagem concorrente
- **WHEN** cinco tentativas inválidas simultâneas são processadas com persistência disponível
- **THEN** nenhuma atualização de contador é perdida e a conta termina bloqueada

#### Scenario: Sessão anterior ao bloqueio
- **WHEN** a conta fica bloqueada e apresenta token de sessão anterior ainda válida
- **THEN** o bloqueio de login, isoladamente, não invalida essa sessão

### Requirement: Sessão confirmada antes da entrega
Cada login bem-sucedido SHALL criar sessão com identificador aleatório único, titular, criação e expiração UTC e revogação opcional. O sistema SHALL entregar o token somente após commit confirmado e SHALL não persistir o JWT completo. Falha ou cancelamento antes do commit SHALL impedir publicação parcial de sessão.

#### Scenario: Dois logins válidos
- **WHEN** dois logins válidos completam
- **THEN** cada token referencia uma sessão distinta persistida para o mesmo titular, sem substituir a outra

#### Scenario: Falha anterior ao commit
- **WHEN** a gravação da sessão falha antes do commit
- **THEN** nenhuma nova sessão permanece confirmada e nenhum token é entregue

#### Scenario: Commit de resultado incerto
- **WHEN** a conexão falha durante a confirmação do commit
- **THEN** nenhum token é entregue nem sucesso anunciado, sem afirmar que o banco necessariamente desfez a sessão

### Requirement: Token de acesso limitado e verificável
O token SHALL durar 30 minutos e conter somente identidade do titular, identificador da sessão e metadados técnicos de emissão, destino e validade. A API SHALL exigir assinatura válida e algoritmo permitido, emissor/destinatário esperados, identidade e sessão válidas e prazo não expirado. SHALL não aceitar tokens sem assinatura, adulterados ou com claims obrigatórias ausentes ou ambíguas.

#### Scenario: Token malformado ou falsificado
- **WHEN** falta assinatura ou uma assinatura, algoritmo, emissor, destinatário, claim obrigatória ou formato de identidade é inválido
- **THEN** a requisição protegida retorna 401, sem executar a ação protegida

#### Scenario: Expiração exata
- **WHEN** o instante atual é igual ou posterior à expiração da sessão/token
- **THEN** o acesso retorna 401, sem janela adicional de autorização

#### Scenario: Conteúdo mínimo
- **WHEN** são inspecionados token e registro de sessão emitidos
- **THEN** não contêm senha, e-mail, hash, security stamp, dados financeiros ou credenciais externas; o registro não contém o token completo

### Requirement: Verificação persistente em cada acesso
Após validar o token, o sistema SHALL consultar a sessão pelo identificador e titular em cada chamada protegida e exigir usuário existente, sessão não revogada e prazo válido. SHALL não usar cache que aceite estado antigo. A identidade autenticada SHALL vir do token validado e da sessão correspondente, nunca do corpo ou filtro enviado pelo cliente.

#### Scenario: Sessão válida
- **WHEN** uma requisição apresenta token válido de sessão ativa e titular correspondente
- **THEN** a autenticação fornece essa identidade à ação protegida após consulta ao banco

#### Scenario: Sessão ausente ou incompatível
- **WHEN** a sessão está ausente, revogada, expirada ou pertence a outro titular
- **THEN** retorna 401 e não executa a ação protegida

#### Scenario: Mudança entre chamadas
- **WHEN** uma sessão é marcada revogada no banco entre duas chamadas com o mesmo token
- **THEN** a chamada seguinte retorna 401 sem reutilizar autorização anterior

### Requirement: Indisponibilidade distinta de credencial inválida
O sistema SHALL falhar fechado em indisponibilidade de banco, schema ou configuração de autenticação. Quando uma operação de autenticação depende do componente indisponível, SHALL retornar 503 sanitizado em vez de sucesso ou senha incorreta. Tokens inválidos detectáveis antes de acessar banco SHALL continuar recebendo 401.

#### Scenario: Banco indisponível no login ou na consulta de sessão
- **WHEN** credenciais ou token estruturalmente válido exigem acesso ao banco e este falha
- **THEN** retorna 503 sem token, dados protegidos, mensagem de senha incorreta ou detalhe da conexão

#### Scenario: Configuração ausente
- **WHEN** a configuração de autenticação está ausente ou inválida e o login é solicitado
- **THEN** retorna 503 sem usar chave temporária, segredo padrão ou outro banco

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
