# Spec Delta

## MODIFIED Requirements

### Requirement: CORS de origem explícita
A API SHALL conceder acesso CORS somente a origens exatas configuradas, com métodos e cabeçalhos necessários às chamadas previstas; SHALL conceder `Idempotency-Key` apenas no preflight de `POST` às duas operações autenticadas de solicitação de sincronização da conexão atual. SHALL não conceder origem curinga, ecoar origem arbitrária ou usar CORS em lugar de autenticação. Sem origens configuradas, SHALL não conceder acesso CORS.

#### Scenario: Preflight de origem autorizada
- **WHEN** um navegador envia preflight de origem configurada para método e cabeçalhos permitidos
- **THEN** a API devolve cabeçalhos CORS compatíveis com a solicitação, sem exigir Bearer no preflight

#### Scenario: Origem não autorizada ou lista vazia
- **WHEN** um navegador solicita preflight ou resposta normal de origem não configurada
- **THEN** a resposta não concede acesso CORS a essa origem nem expõe credenciais ou dados a ela

#### Scenario: Cliente sem Origin
- **WHEN** uma chamada sem cabeçalho Origin acessa rota protegida
- **THEN** CORS não é tratado como autenticação nem como impedimento independente para um cliente HTTP autorizado

#### Scenario: Preflight dos pedidos de sincronização
- **WHEN** uma origem configurada solicita preflight com método `POST` e cabeçalhos `Authorization` e `Idempotency-Key` para `/api/connections/current/sync` ou `/api/connections/current/sync/on-entry`
- **THEN** a API concede origem, método e os dois cabeçalhos solicitados sem exigir Bearer no preflight; a chamada efetiva continua sujeita à autenticação, autorização e validação da chave

#### Scenario: Chave não é autorizada em outra operação
- **WHEN** uma origem configurada solicita `Idempotency-Key` no preflight de outra rota ou de método diferente de `POST`
- **THEN** a resposta não concede esse cabeçalho para essa operação, preservando os cabeçalhos e métodos previamente permitidos no respectivo contrato

#### Scenario: Resposta efetiva da sincronização
- **WHEN** uma origem configurada envia o `POST` de sincronização com `Origin`, Bearer e chave de idempotência válidos
- **THEN** a resposta HTTP, inclusive erros tratados da operação, contém a permissão CORS de origem aplicável sem tornar a operação anônima nem criar nova intenção numa repetição com a mesma chave
