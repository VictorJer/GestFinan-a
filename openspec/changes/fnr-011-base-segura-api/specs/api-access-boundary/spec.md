# Spec Delta

## Purpose

Proteger o acesso aos dados da API e oferecer contratos HTTP de falha previsíveis, sanitizados e compatíveis com a autenticação existente.

## ADDED Requirements

### Requirement: Fronteira autenticada para dados
A API SHALL exigir sessão válida para todo endpoint HTTP de dados financeiros e SHALL obter a identidade do titular exclusivamente da sessão autenticada. Apenas rotas explicitamente anônimas SHALL permanecer sem autenticação; ausência de credenciais em rota protegida SHALL retornar 401 e não executar a ação.

#### Scenario: Rota protegida sem sessão
- **WHEN** uma requisição sem Bearer acessa uma rota de dados protegida
- **THEN** recebe 401 sanitizado com challenge Bearer, sem executar a operação nem divulgar dados

#### Scenario: Identidade enviada pelo cliente
- **WHEN** uma requisição autenticada fornece outro titular no corpo, na rota ou em filtros
- **THEN** essa entrada não substitui a identidade derivada da sessão nem amplia o acesso aos dados

#### Scenario: Rotas anônimas existentes
- **WHEN** a API recebe login ou `GET /health` sem sessão válida
- **THEN** cada rota mantém seu próprio contrato anônimo, sem consulta de sessão para health

### Requirement: Propriedade e não divulgação de recursos
A API SHALL verificar a propriedade antes de retornar ou alterar um recurso financeiro individual e SHALL não permitir que filtros selecionem dados de outro titular. Recurso inexistente e recurso de outro titular SHALL produzir a mesma resposta 404; 403 SHALL indicar falta de permissão conhecida sem confirmar a existência de um recurso alheio.

#### Scenario: Recurso de outro titular
- **WHEN** uma sessão válida consulta ou tenta alterar o identificador de recurso pertencente a outro titular
- **THEN** recebe o mesmo 404 sanitizado de um identificador inexistente, sem alterar ou revelar o recurso

#### Scenario: Lista com filtro de outro titular
- **WHEN** uma sessão válida fornece um identificador de titular diferente em um filtro de listagem
- **THEN** a consulta permanece restrita à sessão autenticada e não devolve registros alheios

#### Scenario: Permissão insuficiente sem revelar recurso
- **WHEN** uma operação explicitamente autorizada ao titular exige permissão adicional conhecida e a sessão não a possui
- **THEN** recebe 403 sanitizado, sem sugerir que um recurso alheio exista

### Requirement: CORS de origem explícita
A API SHALL conceder acesso CORS somente a origens exatas configuradas, com métodos e cabeçalhos necessários às chamadas previstas; SHALL não conceder origem curinga, ecoar origem arbitrária ou usar CORS em lugar de autenticação. Sem origens configuradas, SHALL não conceder acesso CORS.

#### Scenario: Preflight de origem autorizada
- **WHEN** um navegador envia preflight de origem configurada para método e cabeçalhos permitidos
- **THEN** a API devolve cabeçalhos CORS compatíveis com a solicitação, sem exigir Bearer no preflight

#### Scenario: Origem não autorizada ou lista vazia
- **WHEN** um navegador solicita preflight ou resposta normal de origem não configurada
- **THEN** a resposta não concede acesso CORS a essa origem nem expõe credenciais ou dados a ela

#### Scenario: Cliente sem Origin
- **WHEN** uma chamada sem cabeçalho Origin acessa rota protegida
- **THEN** CORS não é tratado como autenticação nem como impedimento independente para um cliente HTTP autorizado

### Requirement: Erros HTTP sanitizados e rastreáveis
A API SHALL usar `application/problem+json` nos erros tratados, com status, título/código estáveis e traceId não sensível. SHALL distinguir entrada inválida (400), sessão inválida (401), permissão insuficiente (403), recurso inacessível (404), conflito (409), excesso de solicitações (429), falha inesperada (500) e dependência indisponível (503), sem refletir valores recebidos ou exceções brutas.

#### Scenario: Entrada inválida e conflito
- **WHEN** um endpoint da API rejeita entrada inválida ou detecta conflito de negócio
- **THEN** responde respectivamente 400 ou 409 em Problem Details, sem ecoar campos sensíveis

#### Scenario: Falha inesperada ou dependência indisponível
- **WHEN** uma chamada encontra exceção inesperada ou indisponibilidade conhecida do serviço necessário
- **THEN** responde respectivamente 500 ou 503 sanitizado, com traceId, sem detalhes de banco, provedor ou stack trace

#### Scenario: Autenticação preexistente
- **WHEN** login/logout retornam erros previstos ou uma sessão inválida recebe challenge
- **THEN** preservam os códigos específicos, `Cache-Control: no-store` e `WWW-Authenticate: Bearer` onde já exigido, sem publicar mensagem de validação do framework

### Requirement: Limitação de solicitações sem substituir lockout
A API SHALL limitar chamadas ao login e a rotas protegidas por políticas explícitas configuráveis. Ao exceder o limite aplicável, SHALL retornar 429 sanitizado antes de executar a operação limitada. SHALL não aplicar essa limitação a `GET /health` nem aos comandos administrativos; limitação por instância SHALL não ser apresentada como proteção global entre hosts.

#### Scenario: Excesso no login
- **WHEN** uma origem excede o limite de login da instância
- **THEN** a chamada seguinte recebe 429 sem avaliar senha, criar sessão ou alterar o contador de falhas de credenciais

#### Scenario: Excesso em rota protegida
- **WHEN** uma sessão válida excede o limite configurado de requisições protegidas
- **THEN** a requisição excedente recebe 429 sem executar a ação protegida

#### Scenario: Health e comando administrativo
- **WHEN** chamadas a `GET /health` ou a comandos administrativos ocorrem durante tráfego limitado
- **THEN** preservam seus contratos existentes sem receber 429 dessa política

### Requirement: Compatibilidade dos contratos HTTP vigentes
A entrega SHALL conservar as rotas e os formatos de sucesso de login, logout e health, bem como a exposição de OpenAPI/Swagger apenas em Development. A documentação SHALL distinguir o caminho versionado do documento OpenAPI de versionamento das rotas; alterações incompatíveis de rotas/DTOs SHALL exigir especificação de versão própria antes de serem publicadas.

#### Scenario: Rotas existentes
- **WHEN** clientes usam `/api/auth/login`, `/api/auth/logout` ou `/health` após esta entrega
- **THEN** as rotas continuam presentes com os sucessos existentes, salvo a nova possibilidade de 429 no login excedente

#### Scenario: Documentação fora de Development
- **WHEN** o ambiente é Production ou Staging
- **THEN** OpenAPI e Swagger continuam indisponíveis e `GET /health` preserva o corpo mínimo
