# Spec Delta

## MODIFIED Requirements

### Requirement: Documento dos endpoints disponíveis
Em Development, a API SHALL servir um documento OpenAPI válido em `/openapi/v1.json`, descrevendo os endpoints implementados, seus métodos e respostas. O documento SHALL incluir `GET /health` e não apresentar operações financeiras ainda inexistentes. SHALL declarar um esquema de segurança HTTP bearer com formato JWT e exigir esse esquema somente nas operações que, segundo as políticas efetivas da API, requerem autenticação; operações anônimas SHALL não requerê-lo.

#### Scenario: Consulta do documento local
- **WHEN** um cliente solicita `/openapi/v1.json` em Development
- **THEN** recebe HTTP 200 com documento OpenAPI que descreve `GET /health` e sua resposta JSON HTTP 200
- **AND** o documento não contém credenciais nem compras fictícias apresentadas como dados reais

#### Scenario: Esquema Bearer reutilizável
- **WHEN** um cliente consulta o documento de Development
- **THEN** `components.securitySchemes` declara um esquema HTTP `bearer` de formato `JWT` para o cabeçalho Authorization
- **AND** as referências das operações protegidas resolvem para esse esquema, sem valores de token, exemplos de credenciais ou exigência global que inclua operações públicas

#### Scenario: Operações públicas e protegidas
- **WHEN** um cliente compara as operações descritas com as políticas efetivas da API
- **THEN** `POST /api/auth/login` e `GET /health` não exigem Bearer no documento
- **AND** `POST /api/auth/logout` e todas as operações publicadas sujeitas à autenticação explícita ou à política fallback exigem Bearer, sem alteração da política executada pela API

### Requirement: Execução interativa de chamadas
Em Development, a API SHALL disponibilizar Swagger UI em `/swagger`, carregando o documento local e permitindo executar `GET /health` pelo recurso Try it out sem ferramenta externa. A interface SHALL oferecer Authorize para informar manualmente o accessToken e aplicar Bearer às chamadas das operações protegidas documentadas, sem exigir token para login ou health.

#### Scenario: Teste pelo navegador
- **GIVEN** a API está em execução local em Development
- **WHEN** o desenvolvedor abre `/swagger` e executa `GET /health` pelo Try it out
- **THEN** a interface mostra o código HTTP 200 e o corpo JSON `{"status":"ok"}` retornados pela API
- **AND** os recursos da interface são servidos pela aplicação sem depender de CDN externa

#### Scenario: Autorização manual
- **GIVEN** o desenvolvedor abre o Swagger UI em Development
- **WHEN** informa manualmente um accessToken no botão Authorize e executa uma operação protegida
- **THEN** a requisição dessa operação usa `Authorization: Bearer <accessToken>` e continua sujeita à validação normal de JWT/sessão da API
- **AND** o desenvolvedor não precisa informar o prefixo `Bearer` no campo do token

#### Scenario: Rotas públicas após Authorize
- **GIVEN** um valor foi informado no botão Authorize em Development
- **WHEN** o desenvolvedor executa login ou health pela interface
- **THEN** essas operações não exigem Bearer e continuam acessíveis anonimamente, sem alterar seus contratos HTTP
