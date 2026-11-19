# Proposal

## Why

A FNR-009 já confirma sessões e consulta seu estado em cada chamada, mas ainda não há logout e o reset administrativo não revoga sessões existentes. A **FNR-010**, dependente da FNR-009 integrada, fecha essa lacuna de RF14 e DT-30 antes de qualquer publicação autenticada, incluindo login concorrente ao reset.

## What Changes

- Oferecer `POST /api/auth/logout` protegido por Bearer, sem identificador fornecido pelo cliente: revogar somente a sessão autenticada e responder 204 apenas após confirmação transacional. Token inválido/revogado/expirado continua recebendo 401; falha operacional recebe 503 sanitizado.
- Incorporar a revogação de todas as sessões ainda não revogadas do titular à mesma transação que redefine a senha via `user-reset-password`, preservando identidade, vínculos, política de senha e isolamento do comando.
- Ordenar login, reset e logout pelo protocolo transacional existente; um login com credencial antiga não poderá deixar sessão válida após reset confirmado. Não acrescentar claims, refresh tokens ou versão de segurança persistida sem necessidade.
- **BREAKING (saída administrativa):** substituir `session_revocation=not_implemented` por `session_revocation=confirmed` exclusivamente no reset confirmado; preservar `status=password_reset` e os códigos de saída existentes.
- Atualizar explicitamente as specs que hoje proíbem logout/exigem o aviso de revogação pendente, sem reescrever evidências históricas da FNR-008/009.
- Validar fluxos HTTP reais de login/acesso/logout e de login/acesso/reset administrativo/rejeição dos tokens; incluir concorrência, rollback, commit incerto, indisponibilidade e privacidade em PostgreSQL descartável.
- Fora de escopo: FNR-011 (segurança geral/CORS/propriedade de recursos financeiros), FNR-020 (Angular/limpeza local e mensagens de falha remota), cadastro/reset HTTP, desbloqueio de conta, logout global HTTP, retenção/limpeza de sessões, migrations sem alteração de schema, infraestrutura e deploy. A obrigação de a interface não confirmar logout remoto falho é herdada do TDD §5, mas sua implementação pertence à FNR-020.

## Capabilities

### New Capabilities

Nenhuma. A revogação pertence às fronteiras de sessão e recuperação administrativa já consolidadas.

### Modified Capabilities

- `authenticated-sessions`: acrescentar logout da sessão atual, confirmação/falhas e concorrência; substituir a proibição histórica de logout e atualizar os limites de evidência/publicação.
- `administrative-user-access`: tornar reset e revogação global atômicos, ordenar o reset com login e substituir o aviso de revogação não implementada por confirmação verdadeira.

## Impact

- **Rastreabilidade:** [BACKLOG FNR-010, §§4/5](../../../../docs/BACKLOG.md), [PRD RF14/§13](../../../../docs/PRD.md) e [TDD §5, DT-23–30/38–39/48–49](../../../../docs/TDD.md). Reutiliza as decisões aprovadas da FNR-008/009; PRD não precisa de alteração. Detalhes novos deste SDD dependem de revisão humana e consolidação proporcional no TDD antes de código dependente.
- **Base observada:** `AuthenticationController` está anônimo no nível da classe; a proteção de logout exigirá corrigir a posição desse atributo. O registro HTTP MediatR permite apenas `LoginHandler`; será necessário incluir o handler de logout. `AuthSessions` já contém revogação UTC e índice por usuário. Login/reset já usam `IdentityMutationLock` e transação `ReadCommitted`.
- **Áreas futuras:** `backend/Finora.Api/{Controllers,Authentication,AdministrativeUsers}`, `backend/Finora.Application/Authentication`, `backend/Finora.Infrastructure/Identity` e `backend/tests/Finora.Api.Tests`. Reutilizar fixtures HTTP/PostgreSQL e adaptar testes administrativos que hoje aplicam somente a migration anterior a `AuthSessions`.
- **Dependências:** sem novos pacotes ou schema propostos; preservar baseline/locks. Banco sem `AuthSessions` deverá recusar reset como falha operacional, nunca redefinir senha sem revogar. Nenhum acesso a banco pessoal, Neon ou Pluggy integra este planejamento.
- **Documentação posterior:** README, AMBIENTE, TDD e BACKLOG devem separar o comportamento novo das evidências históricas. Concluir FNR-010 não conclui RF14 nem autoriza publicação: FNR-011/020 e demais gates permanecem aplicáveis.
- **Estado:** artefatos propostos, não aprovados; zero tarefas de implementação executadas por esta solicitação. Sem commit, push, PR, merge, sync, arquivamento ou publicação autorizados.
