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
A introdução de autenticação SHALL preservar `GET /health` anônimo, HTTP 200 e corpo mínimo, sem exigir banco, provedor ou configuração JWT. SHALL não executar migrations/seed no startup nem exigir JWT nos comandos administrativos. Esta entrega SHALL não oferecer logout, reset HTTP ou cadastro público.

#### Scenario: Health com dependências ausentes
- **WHEN** a API inicia sem banco, chave JWT ou Pluggy e recebe `GET /health`
- **THEN** retorna somente `{"status":"ok"}` com HTTP 200, sem consultar dependências

#### Scenario: Comandos administrativos sem JWT
- **WHEN** um comando administrativo válido é executado com banco configurado, mas sem JWT
- **THEN** conserva seu contrato e o aviso de revogação pendente no reset

### Requirement: Saídas e evidências sem segredos
O sistema SHALL não registrar senha, JWT, cabeçalho Authorization, hash, e-mail ou connection string em logs e erros de autenticação. Erros SHALL usar mensagens fixas com status e identificador de rastreio, sem exceções brutas. A documentação SHALL manter FNR-010 como requisito antes de publicar autenticação e não declarar RF14 integralmente atendido.

#### Scenario: Dados sensíveis em falhas
- **WHEN** login, validação ou configuração falham com sentinelas sensíveis nos dados de teste
- **THEN** logs e respostas de erro não contêm essas sentinelas

#### Scenario: Avaliação da entrega
- **WHEN** FNR-009 é validada isoladamente
- **THEN** a evidência distingue a leitura do estado revogado da implementação de logout/reset transacional, ainda pertencente à FNR-010
