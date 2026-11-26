# Design

## Context

Ver [proposal.md](proposal.md). `FinoraCors.AddFinoraCors` registra hoje uma única política com origens exatas e `GET`/`POST`/`PATCH`, mas apenas `Authorization`/`Content-Type`. Em `Program.cs`, `UseRouting` precede `UseCors(FinoraCors.PolicyName)`, que precede a fronteira de erros, autenticação e autorização; isso permite responder OPTIONS sem Bearer. `ConnectionSyncController` exige `Idempotency-Key` nos dois POSTs de sincronização; `SyncIntentCoordinator` já preserva a chave em replay explícito. Os testes `CorsContractTests` usam host HTTP sintético, enquanto `PostgreSqlSyncRunAcceptanceApiTests` exercitam aceitação e replay com PostgreSQL descartável. O TDD 0.36 e o backlog 0.85 aguardam revisão própria.

## Goals / Non-Goals

**Goals:**
- Selecionar a permissão adicional por caminho exato e método efetivo `POST` (ou `Access-Control-Request-Method: POST` no OPTIONS), mantendo a allowlist de origens já configurada e a política original para o restante.
- Deixar CORS como permissão de leitura do navegador, sem transformá-lo em autorização de negócio ou alterar o processamento idempotente.
- Diferenciar provas HTTP de middleware, provas HTTP com aceitação persistida e validação real de navegador.

**Non-Goals:**
- Modificar Angular, regras de atualização, autenticação, migrações, provisionamento de domínios ou permitir outros cabeçalhos/operações.
- Declarar o fluxo FNR-021 integrado ou publicar a aplicação com base em testes sintéticos.

## Decisions

### 1. Duas políticas CORS com despacho restrito no mesmo middleware

Manter uma política base (origens exatas configuradas; métodos `GET`/`POST`/`PATCH`; cabeçalhos `Authorization`/`Content-Type`) e uma política específica com as mesmas origens/métodos, adicionando apenas `Idempotency-Key`. Selecionar a específica somente para os caminhos exatos `/api/connections/current/sync` e `/api/connections/current/sync/on-entry` quando o método da requisição for `POST` ou, em OPTIONS de preflight válido, o método solicitado for `POST`. Usar um provedor/seleção de política no ponto único `UseCors` após roteamento, antes da autenticação; deixar a política base como fallback para todo o restante e manter a configuração vazia fechada. Comparação de caminhos deve respeitar limites de segmento e não aceitar prefixos ou sufixos arbitrários. Não criar política CORS anônima de forma implícita.

Alternativas rejeitadas: acrescentar `Idempotency-Key` à política global concederia o cabeçalho a rotas que não o usam; confiar apenas em atributo de controller junto à política global pode combinar políticas de forma difícil de auditar e depender da resolução do OPTIONS. O despacho único permite testar preflight e resposta real na mesma composição. Durante a implementação, confirmar no host que OPTIONS é atendido antes de `AuthenticationBoundary` e que a política aplicada corresponde ao caminho/método observados; caso o mecanismo do framework não preserve isso, revisar este design antes de substituí-lo.

### 2. Conservar o contrato de idempotência e a autenticação no POST

`UseCors` continua precedendo a fronteira HTTP e o middleware de autenticação, para envolver respostas reais (inclusive 401 e falhas sanitizadas) e terminar preflights sem exigir sessão. A política seleciona permissões do navegador, mas não dispensa Bearer nem valida a chave. O controller e a aceitação persistente continuam exigindo chave única e replay da mesma intenção; a UI não precisa gerar nova chave após resposta indeterminada. Não adicionar `Access-Control-Allow-Credentials`, origem curinga, `AllowAnyHeader` ou `AllowAnyMethod`.

Alternativa rejeitada: corrigir somente a UI para remover a chave ou enviar sem preflight quebraria o contrato de idempotência e não corrigiria a divergência da API.

### 3. Verificar transporte e negócio separadamente

Em `CorsContractTests`, variar caminho, método, `Origin` e lista de cabeçalhos solicitados; inspecionar as concessões (ou sua ausência), não só o status 204: CORS pode devolver 204 também quando não autoriza. Testar POST efetivo com `Origin` e sem Bearer para confirmar 401 com CORS e sem execução; testar resposta de POST autenticado (sucesso 200/202 e erro aplicável) com origem permitida e replay da mesma chave usando fixtures sintéticas e PostgreSQL descartável quando necessário. Manter regressões de 403/429/500, rotas GET/health e ausência de origens; reutilizar as provas de replay existentes no frontend e backend.

Alternativa rejeitada: considerar OPTIONS 204 ou testes `HttpTestingController` como evidência de navegador; nenhum dos dois sozinho prova que o navegador aceita o POST e exibe o resultado.

## Risks / Trade-offs

- [Despacho seleciona política por prefixo/método incorreto] → usar caminhos exatos, `Access-Control-Request-Method` apenas para OPTIONS de preflight e testes negativos de rota similar, método diferente e cabeçalho desconhecido.
- [O navegador bloqueia mesmo com testes TestServer verdes] → registrar evidência sintética como tal e validar em navegador com origem configurada e serviços descartáveis em etapa autorizada separada, antes de declarar integração FNR-021.
- [Preflight funciona, mas resposta efetiva não tem `Access-Control-Allow-Origin`] → manter um único middleware CORS externo e verificar respostas 200/202, 401 e erros sanitizados.
- [Documentação 0.36/0.85 ainda não aceita] → qualquer atualização factual futura abre revisão própria; preservar histórico e aprovações anteriores, sem declarar aprovação retroativa.

## Migration Plan

Não há alteração de schema nem migration. Na implementação, executar os testes focais e regressões, revisar o contrato CORS em TDD/backlog e apresentar evidências para aceite. A configuração de origens permanece a mesma; a habilitação em ambiente hospedado depende de configuração da origem legítima e dos gates normais de CI/PR/deploy. Reversão seria retornar à seleção anterior de política mediante nova revisão, reconhecendo que isso voltaria a bloquear a atualização pelo navegador; não ajustar CORS no ambiente de produção fora do fluxo aprovado.
