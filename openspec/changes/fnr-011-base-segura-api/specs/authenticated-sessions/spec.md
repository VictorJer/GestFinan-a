# Spec Delta

## MODIFIED Requirements

### Requirement: Login exclusivo do titular
O sistema SHALL oferecer `POST /api/auth/login` com e-mail e senha, sem cadastro público, cookies ou refresh token. SHALL comparar e-mail ignorando caixa e espaços externos, preservar a senha exatamente e rejeitar credenciais inválidas avaliadas com HTTP 401 genérico, sem revelar a existência da conta. Quando a política de limitação de requisições bloquear a tentativa antes da avaliação, SHALL responder HTTP 429 sanitizado, sem alterar o lockout persistente.

#### Scenario: Credenciais válidas
- **WHEN** o titular envia credenciais válidas, a política permite avaliar a tentativa e não está bloqueado
- **THEN** recebe HTTP 200 com `accessToken`, `tokenType` igual a `Bearer`, `expiresIn` igual a 1800 e `expiresAtUtc`, após confirmação da sessão
- **AND** a resposta tem `Cache-Control: no-store`, sem cookie ou refresh token

#### Scenario: Entrada inválida e credencial incorreta
- **WHEN** a política permite avaliar a tentativa e o corpo é inválido ou falta e-mail/senha
- **THEN** recebe HTTP 400 sanitizado, sem refletir valores recebidos

#### Scenario: Conta desconhecida ou senha errada
- **WHEN** a política permite avaliar a tentativa e o corpo é válido mas o e-mail é desconhecido ou a senha é incorreta
- **THEN** recebe o mesmo HTTP 401 e corpo genérico, sem sessão ou token

#### Scenario: Normalização restrita ao e-mail
- **WHEN** o e-mail correto difere somente em caixa/espaços externos e a senha mantém seu conteúdo exato
- **THEN** a credencial é avaliada para o mesmo titular, sem remover espaços da senha

#### Scenario: Solicitações excessivas antes da avaliação
- **WHEN** a origem excede o limite de requisições ao login antes de verificar as credenciais
- **THEN** recebe HTTP 429 sanitizado com `Cache-Control: no-store`, sem token, sem avaliar a senha nem incrementar o contador de falhas do usuário
