# Proposal

## Why

A FNR-009/010 entregou login, sessão e revogação, mas a padronização de acesso e respostas HTTP ainda está restrita à autenticação. Antes de expor importação e dados financeiros, a FNR-011 precisa estabelecer uma fronteira comum que não transforme falhas ou recursos de outro titular em vazamento de informações.

## What Changes

- Definir uma política de autenticação obrigatória para novos endpoints de dados, identidade derivada da sessão e verificação de propriedade em cada recurso, sem criar endpoint financeiro fictício para demonstrá-la.
- Restringir CORS a origens explicitamente configuradas, com preflight coerente e sem usar CORS como autorização; manter diagnóstico e documentação de desenvolvimento conforme os contratos existentes.
- Padronizar erros HTTP da API com Problem Details sanitizado, código estável, status e traceId; distinguir 400, 401, 403, 404, 409, 429, 500 e 503 sem refletir entrada, exceções ou dados externos. Preservar os contratos específicos de login/logout e o desafio Bearer.
- Introduzir limitação de tentativas para o login como proteção complementar ao lockout persistente existente; excesso de requisições recebe 429 sem avaliar credenciais nem criar sessão. Estabelecer política configurável também para rotas protegidas, sem afetar `/health` e os comandos CLI.
- Registrar estratégia de evolução do contrato HTTP sem mudar as rotas atuais: manter `/api/auth/*` e `/health`; mudanças incompatíveis futuras exigem versão explícita e aprovação no SDD do endpoint afetado. Não alegar que `/openapi/v1.json` versiona as rotas.
- **Sem mudanças incompatíveis planejadas** nos sucessos e falhas já estabelecidos, exceto a nova possibilidade de 429 sob excesso de tentativas. Reconciliar o registro documental do PR #23 na etapa de execução, sem alterar o histórico de aprovações.

## Capabilities

### New Capabilities

- `api-access-boundary`: proteger rotas de dados e padronizar CORS, propriedade, respostas de falha e limitação de requisições da API.

### Modified Capabilities

- `authenticated-sessions`: permitir HTTP 429 antes da avaliação de credenciais quando o limite de solicitações de login for atingido, sem alterar lockout, duração, revogação ou o 401 genérico para credenciais efetivamente avaliadas.

## Impact

- Código futuro: composição/middleware e políticas em `backend/Finora.Api`, com adaptações pontuais na autenticação HTTP; testes HTTP em `backend/tests/Finora.Api.Tests`. A persistência de sessões e os comandos administrativos não são alterados.
- Contratos existentes: login/logout e health permanecem nas mesmas rotas; OpenAPI/Swagger continuam exclusivos de Development. Endpoints financeiros, frontend, publicação, migrações e acesso ao provedor ficam fora desta mudança.
- Rastreabilidade: FNR-011; PRD RF09/RF14; TDD §§2, 5 e 11. FNR-013/015/020 são entregas posteriores. Revisões de TDD/backlog e aprovação dos artefatos novos são gates próprios antes de implementar; aprovação histórica de FNR-010 não se transfere.
