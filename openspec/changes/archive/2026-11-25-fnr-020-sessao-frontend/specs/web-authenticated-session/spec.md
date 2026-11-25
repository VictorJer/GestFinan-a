# Spec Delta

## Purpose

Permitir ao titular entrar e sair do Finora pelo navegador com uma sessão temporária protegida, sem persistir credenciais ou confundir acesso local com revogação confirmada no servidor.

## ADDED Requirements

### Requirement: Login do titular no navegador
A aplicação SHALL oferecer formulário em português de e-mail e senha que usa o login da API configurada e inicia uma sessão local apenas após resposta válida. SHALL preservar a senha enviada sem transformação, não mostrar se a conta existe e não oferecer cadastro público ou recuperação por e-mail.

#### Scenario: Login válido
- **WHEN** o titular envia e-mail e senha válidos com a API configurada e recebe o token Bearer e a expiração do servidor
- **THEN** a sessão local é iniciada e o titular pode abrir a área protegida, sem exibir o token na página

#### Scenario: Credencial inválida ou conta bloqueada
- **WHEN** a API responde 401 à tentativa de login
- **THEN** a aplicação informa falha genérica de autenticação, sem distinguir conta ausente, senha incorreta ou bloqueio e sem criar sessão

#### Scenario: Entrada inválida ou limite de tentativas
- **WHEN** a entrada é inválida ou excede limites, ou a API responde 400, 413 ou 429
- **THEN** a aplicação informa respectivamente correção da entrada, excesso de tamanho ou limite de tentativas, sem ecoar a senha nem criar sessão

#### Scenario: Indisponibilidade e configuração ausente
- **WHEN** falta URL da API ou o login falha por rede, timeout, 500 ou 503
- **THEN** a aplicação informa indisponibilidade sem rotular a falha como senha incorreta, sem criar sessão nem enviar a senha a outro destino

### Requirement: Sessão local volátil e expiração
A aplicação SHALL manter token e expiração somente em memória, sem cookies, armazenamento persistente ou renovação automática. SHALL encerrar a sessão local quando expirar; recarregar a página SHALL exigir novo login.

#### Scenario: Recarga da página
- **WHEN** o titular recarrega o navegador após login
- **THEN** token e acesso local anteriores não são restaurados e novo login é necessário

#### Scenario: Expiração sem requisição
- **WHEN** o instante alcança a expiração informada pelo servidor mesmo sem nova requisição
- **THEN** o acesso local e o token em memória são removidos e a área protegida passa a exigir login

#### Scenario: Resposta de login inválida
- **WHEN** a API devolve sucesso sem token Bearer utilizável ou com expiração já vencida ou inválida
- **THEN** a aplicação não inicia sessão e informa falha segura, sem registrar o corpo sensível

### Requirement: Navegação protegida e acesso honesto
A aplicação SHALL permitir abrir sua área autenticada informativa somente com sessão local válida, orientar novo login quando necessário e não carregar dados financeiros por entrar nessa área. A API continua sendo a autoridade sobre o acesso real.

#### Scenario: Acesso direto sem sessão
- **WHEN** uma pessoa abre diretamente a URL da área protegida sem sessão local válida
- **THEN** é encaminhada ao login e não vê conteúdo protegido

#### Scenario: Acesso autenticado
- **WHEN** o titular navega para a área protegida durante sessão local ainda válida
- **THEN** vê somente a área informativa, sem cartões, movimentos, importação ou dados de demonstração apresentados como reais

### Requirement: Bearer restrito e reação a respostas protegidas
A aplicação SHALL enviar o Bearer somente em chamadas protegidas destinadas à base configurada da API Finora, nunca para outra origem, URL parecida, login público ou página pública. Um 401 de chamada protegida SHALL encerrar a sessão local; 403 SHALL informar falta de permissão sem provocar login repetido.

#### Scenario: Requisição para a API protegida
- **WHEN** a sessão local está válida e a aplicação envia uma chamada protegida para a base configurada
- **THEN** somente essa chamada recebe `Authorization: Bearer` com o token da sessão atual

#### Scenario: Destino externo ou público
- **WHEN** a aplicação envia uma chamada para outra origem, URL de aparência semelhante ou endpoint público de login
- **THEN** não anexa token de sessão nem reclassifica eventual 401 desse destino como expiração local

#### Scenario: Sessão revogada no servidor
- **WHEN** uma chamada protegida da sessão atual recebe 401
- **THEN** o token local é eliminado e o titular é orientado a entrar novamente; a aplicação não anuncia revogação remota por conta própria

#### Scenario: Permissão insuficiente ou falha operacional
- **WHEN** uma chamada protegida recebe 403 ou falha por rede ou indisponibilidade
- **THEN** a aplicação distingue falta de permissão ou indisponibilidade sem tratar esses resultados como credencial inválida nem repetir automaticamente o login

### Requirement: Logout com confirmação diferenciada
A aplicação SHALL solicitar logout da sessão atual à API configurada e SHALL limpar o token em memória após a tentativa. Somente resposta 204 SHALL ser apresentada como revogação confirmada; falha, timeout, 401 ou confirmação incerta SHALL informar que o estado remoto não foi confirmado.

#### Scenario: Saída confirmada
- **WHEN** o titular solicita logout e a API responde 204
- **THEN** a sessão local é encerrada, a área protegida deixa de estar acessível e a aplicação informa saída confirmada

#### Scenario: Saída sem confirmação remota
- **WHEN** o logout falha por rede, timeout ou resposta distinta de 204
- **THEN** a sessão local é encerrada, o acesso local protegido deixa de funcionar e a aplicação informa que não foi possível confirmar o encerramento no servidor

### Requirement: Privacidade e uso acessível da sessão
A aplicação SHALL não registrar nem exibir token, senha ou cabeçalho Authorization em mensagens, console ou navegação; formulários, erros e ações de login/logout SHALL ser identificáveis por texto, operáveis por teclado e compreensíveis em português.

#### Scenario: Erro com informações sensíveis
- **WHEN** um login ou logout falha com valores sensíveis enviados na requisição ou presentes na resposta
- **THEN** mensagem visível, rota e registros controlados pela aplicação não contêm senha, token, cabeçalho Authorization ou texto bruto do servidor

#### Scenario: Navegação por teclado
- **WHEN** o titular usa teclado para entrar, identificar erros e sair
- **THEN** encontra campos rotulados, foco visível, estado textual acessível e ações acionáveis sem ponteiro
