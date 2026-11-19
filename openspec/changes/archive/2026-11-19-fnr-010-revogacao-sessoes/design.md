# Design

## Context

Motivação e escopo: [proposal.md](proposal.md). Este SDD está **proposto, aguardando revisão humana**, e não autoriza implementação. Referências canônicas: [PRD RF14/§13](../../../../docs/PRD.md), [TDD §5, DT-23–30/38–39/48–49](../../../../docs/TDD.md) e [BACKLOG FNR-010](../../../../docs/BACKLOG.md). A FNR-009 está integrada; o backlog conserva registros históricos anteriores ao encaminhamento do encerramento, que não serão reescritos neste planejamento.

### Evidência da inspeção somente leitura

| Área observada | Consequência para o desenho |
|---|---|
| `backend/Finora.Api/Controllers/AuthenticationController.cs`: `[AllowAnonymous]` na classe, somente login | Não basta acrescentar `[Authorize]` em uma action; mover o anonimato para login e proteger logout explicitamente. |
| `AuthenticationServiceCollectionExtensions.cs`: `TypeEvaluator` permite só `LoginHandler`; `AuthenticationHttp.IsProtected` exige metadados de autorização sem anonimato | Incluir logout na composição HTTP e testar os metadados efetivos, não somente a existência da rota. |
| `IdentityLoginService.cs`: `ReadCommitted`, `IdentityMutationLock`, limpeza de tracking e leitura depois do lock | Reutilizar esse protocolo para reset/logout; login já está preparado para não usar senha obsoleta. |
| `AdministrativeUserAccessService.ResetPasswordAsync`: Identity e commit já estão na mesma transação, sem revogação | Inserir revogação antes da tentativa de commit, sem criar transação ou contexto independentes. |
| `AuthSession`/`AuthSessionConfiguration`: revogação nullable UTC, FK Restrict, índice UserId e constraint revogação ≥ criação | Sem novo schema ou migration. Preservar registros e datas de revogação existentes. |
| `AuthenticationSessionReader`: consulta sem tracking/cache por chamada, verifica expiração novamente após SQL | Manter validação de FNR-009 e usar a revogação persistida, sem blacklist em memória. |
| `AdministrativeUserCommand.MapWorkflowResult`: reset emite `not_implemented` | Mudança intencional de contrato de stdout, mantendo códigos e estados. |
| `PostgreSqlAdministrativeUserResetTests`: aplica só `CoreMigrationId`; `PostgreSqlLoginTests`: ordem login→reset ainda não exige revogação | Atualizar fixtures de reset e a prova de concorrência, preservando testes históricos de migrations financeiras. |
| `AuthenticationAvailabilityTests`/`ExplorationTests`: logout ausente/404 e OpenAPI só health/login | Substituir essa expectativa pela rota protegida; cadastro/reset HTTP/refresh continuam ausentes. |

## Goals / Non-Goals

**Goals:**
- Aproveitar uma única fronteira transacional no PostgreSQL para credencial/sessão, com identidade da chamada derivada somente da autenticação real.
- Tornar falhas anteriores ao commit comprovadamente atômicas e perdas de confirmação explicitamente incertas, sem respostas de sucesso antecipadas.
- Manter Application sem dependência de ASP.NET/EF/Identity e os comandos independentes de JWT, servidor HTTP e provedor financeiro.

**Non-Goals:**
- Não criar modelo financeiro, nova versão de sessão, stamp no JWT, cache, cookies, refresh ou limpeza de registros.
- Não serializar todas as leituras protegidas com as mutações nem cancelar retroativamente requisições autenticadas antes da revogação.
- Não desbloquear o titular ou zerar deliberadamente contadores/lockout no reset; manter o comportamento Identity aprovado. Não generalizar erros/CORS da API além da borda de autenticação.
- Não editar PRD/TDD/BACKLOG nem código nesta fase; a consolidação canônica necessária será um gate documental da futura aplicação.

## Decisions

### D1 — Caso de uso de logout e composição segura

Api acrescentará `POST /api/auth/logout` sem DTO de corpo. Removerá `[AllowAnonymous]` da classe e o aplicará somente a login; logout terá `[Authorize]`. Não fazer binding de UserId/jti de query, rota ou corpo. A action construirá `AuthenticationSessionReference` a partir das claims já validadas (`sub`, `jti`, `iat`, `exp`), com parsing defensivo; ausência/inconsistência produz 401, nunca identidade default.

Application receberá comando/handler de logout e contrato específico `IAuthenticationSessionRevoker`, com resultado tipado (`Confirmed`, `InvalidSession`, `Unavailable`), sem JWT bruto, EF, Identity ou `HttpContext`. Request/resultado terão representação textual redigida. O handler delegará o contrato, propagando cancelamento.

O registro HTTP incluirá explicitamente `LogoutHandler` na allowlist ao lado de `LoginHandler`; Infrastructure registrará o adaptador scoped no mesmo contexto Identity. Configuração inválida mantém o ramo de autenticação indisponível: challenge 503 antes da action, sem precisar resolver um revogador conectado. A composição administrativa permanece sem providers JWT; seu registro MediatR não deve exigir resolver handlers HTTP sem dependências nesse host. Restringir a seleção aos handlers administrativos se necessário à validação de DI, sem levar serviços HTTP para a CLI.

**Alternativas:** Controller separado evitaria o atributo na classe, mas manter as duas operações na fronteira existente reduz duplicação. `[Authorize]` na action mantendo o anonimato da classe é inseguro e foi rejeitado. Chamar EF no Controller romperia DT-38/48.

### D2 — Logout transacional, revalidação e resultado HTTP

O adaptador executará `ReadCommitted` e o mesmo `IdentityMutationLock.Key = 0x46494E4F5241L`, seguido de leitura sem tracking de usuário/sessão por referência completa. Não confiar em entidades anteriores à espera. Verificar existência, titular, tempos de emissão/expiração e prazo usando `TimeProvider`, inclusive depois da espera/SQL. Referência inconsistente, usuário ausente ou expiração durante a espera produz `InvalidSession`/401 sem commit de alteração.

Para sessão não revogada, fazer atualização condicional limitada à sessão e titular, sem deletar registro nem tocar em outra sessão. Confirmar 204 somente após commit. Se outra mutação revogou a mesma sessão **depois** que esta chamada passou pela autenticação, a releitura pode confirmar o estado já revogado: não sobrescrever o timestamp; permitir 204 após confirmação da transação. Isso acomoda duas chamadas admitidas antes da primeira revogação sem tornar logout anônimo. Chamada nova com token já revogado continua barrada por JwtBearer/reader com 401; não há promessa de repetição sempre retornar 204.

Revalidar expiração após a escrita e antes de iniciar commit; se venceu, desfazer a alteração e responder 401. A confirmação de logout não cria token, não gira chave e não revoga um novo login independente. Logout e reset concorrentes compartilham o lock, de modo que um não reativa nem apaga a revogação do outro.

`AuthenticationBoundary` aplicará `Cache-Control: no-store` ao logout, incluindo falhas anteriores à action, sem aplicar a leitura de corpo de login ao logout. Reutilizar Problem Details sanitizado/traceId: 401 com challenge Bearer; 503 operacional sem challenge; 500 inesperado sem exceção bruta. Sucesso 204 não tem corpo/cookie. Query/corpo enviados não são lidos como seletores e não alteram o alvo.

**Alternativas:** 200 com payload/contagem não acrescenta informação necessária. Logout anônimo ou aceitar token revogado para obter 204 alteraria o modelo de validação por chamada. Apagar sessão perderia o registro mínimo e não é necessário para revogar.

### D3 — Reset e revogação global na mesma transação

Estender `AdministrativeUserAccessService.ResetPasswordAsync` depois do `ResetPasswordAsync` Identity bem-sucedido e antes de `commitAttempted = true`. O contexto/transação scoped existentes devem ser os mesmos usados pelo store e pela revogação. Revalidar titular após lock sem dados Identity obsoletos; limpar tracking antigo somente no escopo dedicado à operação, nunca descartar trabalho financeiro pendente de outro fluxo.

Atualizar em lote `AuthSessions` do titular com `RevokedAtUtc == null`, **incluindo expiradas**. Zero linhas é sucesso válido. Não regravar timestamps existentes nem alterar dados financeiros, identidade, lockout ou contadores por regra adicional. Registrar `TimeProvider` padrão também na composição administrativa, substituível em testes e sem depender da configuração JWT.

Reutilizar um helper interno de persistência de revogação, com contexto/transação recebidos, para evitar variação de predicados/timestamps entre logout/reset. Usar `ExecuteUpdateAsync` em projeção filtrada, dentro da transação explícita; ele executa imediatamente e não participa do tracking. Não rastrear `AuthSession` para depois sobrescrever seu estado com `SaveChanges`. A operação em lote contorna a barreira `ValidateTrackedEntities`, então o próprio update deve preservar invariantes e as constraints SQL continuam obrigatórias.

O instante de referência UTC é obtido após adquirir o lock; cada timestamp novo será o maior entre esse instante e `CreatedAtUtc` da linha, para respeitar revogação ≥ criação mesmo com pequeno recuo do relógio entre processos. O timestamp é técnico, não ordem de segurança: a garantia depende da serialização/commit, nunca de comparação entre `iat` e hora do reset.

Qualquer falha de revogação desfaz também a credencial/stamp alterados pelo Identity. Sem `AuthSessions`, falhar operacionalmente e desfazer o reset; não criar fallback de compatibilidade, tabela ou migration no startup.

**Alternativas:** revogar após commit da senha cria janela de acesso e estado parcial; segundo contexto/transação exige coordenação desnecessária. Carregar todas as sessões para tracking custa memória e não melhora a atomicidade. Acrescentar versão/security stamp ao JWT e schema seria redundante para os escritores atuais e mudaria contratos de FNR-009.

### D4 — Concorrência com login e alcance da revogação imediata

Preservar a aquisição de lock **antes** da leitura/validação de senha de `IdentityLoginService`. Com `ReadCommitted` e statement novo após lock:

| Ordem de commits no protocolo | Resultado exigido |
|---|---|
| Login antigo → reset | A sessão confirmada pelo login é incluída no reset e fica revogada, mesmo se a resposta do login chegar depois. |
| Reset → login com senha antiga | O login relê a senha atual, retorna 401 e não confirma nova sessão. |
| Reset → login com senha nova, sem lockout | Pode criar sessão válida nova; não reativa nenhuma anterior. |
| Logout sessão A → login | Novo login é uma sessão independente; logout A não o encerra. |
| Reset ↔ logout admitido antes do reset | Ordem serial; nenhuma operação limpa ou sobrescreve revogação já gravada. |

O lock é cooperativo e global, adequado ao único titular e já aprovado. SQL externo ou futuras rotas que alterem credenciais sem respeitar o protocolo não são cobertos; isso deve permanecer documentado. Não usar `SecurityStampValidator` de cookies como suposta revogação de JWT.

“Imediata” significa negar autenticações novas iniciadas após commit, sem cache positivo, também em outro host com mesmo banco/chave. Leitura de autenticação que já terminou pode ter autorizado requisição em andamento; não prometemos interrupção, rollback de ação de negócio ou cancelamento retroativo.

**Alternativas:** bloquear todas as leituras protegidas manteria lock durante negócio/rede e ampliaria escopo/risco de contenção. Uma blacklist por processo não cobre reinício/múltiplos hosts. O protocolo existente evita ambos sem alterar claims.

### D5 — Timeout, cancelamento, commit incerto e saída administrativa

Manter limite operacional de 30 segundos, incluindo espera pelo lock, com timeout de comando e cancellation token encadeado. Não aplicar retry automático. Logout usa o mesmo padrão de `commitAttempted` já usado no login/reset: cancelamento do chamador antes da tentativa propaga para a borda abortar a conexão; timeout interno/falha operacional gera 503. Após iniciar commit, inclusive cancelamento, retornar indisponibilidade sem afirmar rollback.

No reset, manter códigos 0/1/2/3/4/130. Alterar somente a mensagem do estado `PasswordReset` para `User admin: operation=reset_password status=password_reset session_revocation=confirmed`. Falha, timeout, schema ausente, conflito e commit incerto nunca emitem essa mensagem. Cancelamento antes da tentativa usa 130; depois da tentativa, 1. O provider efêmero e token Identity em memória permanecem inalterados.

**Alternativas:** interpretar perda de confirmação como sucesso mentiria ao operador; afirmar rollback garantido também. O operador pode iniciar uma nova operação explicitamente, mas a aplicação não repete uma mutação de resultado incerto.

### D6 — Validação proporcional e preservação das evidências

Usar MSTest/Microsoft.Testing.Platform existentes, `AuthenticationApiFactory`, `PostgreSqlAuthenticationFixture` e a barreira `DisposablePostgresTestConfiguration`. Testes HTTP terão autenticação/JWT/Identity reais e probe somente do assembly de testes; reset é chamado pelo fluxo/contrato administrativo no mesmo banco, **não** por endpoint HTTP novo.

| Prova | Cobertura esperada |
|---|---|
| Application/DI/contrato | Delegação/cancelamento, ausência de PII em ToString, handlers selecionados, logout com `[Authorize]` sem `[AllowAnonymous]`, login/health ainda anônimos, CLI sem JWT. |
| Logout HTTP real | Duas sessões, alvo derivado do Bearer mesmo com identificadores extras, 204/no-store, acesso seguinte 401, outra sessão válida, replay 401, tokens ausentes/adulterados/expirados/sem sessão/incompatíveis. |
| Reset real e fluxo HTTP | Múltiplas sessões, zero sessões, expiradas/já revogadas, senha antiga recusada/nova aceita, identidade/grafo preservados, lockout não contornado, saída `confirmed` somente no sucesso. |
| Ordenação real | Ambas as ordens login/reset com escopos/conexões independentes; stale tracking; logout simultâneo e reset/logout. Usar barreiras observáveis no PostgreSQL, não sleeps presumindo ordem. |
| Falhas antes do commit | Falha após gravação Identity e após update real de sessões, conflito, tabela ausente, timeout/cancelamento durante espera e após escrita. Conferir estado com conexão/escopo novo e liberação do lock. |
| Commit incerto | Interceptor após commit físico: reset mantém senha+revogações juntas e retorna 1; logout fica revogado e retorna 503. Nenhum retry/sucesso/rollback fictício. |
| Relógio/hosts | Expiração durante espera de logout, timestamps UTC válidos com recuo de relógio e preservação do primeiro timestamp; outro host/reinício rejeita sessões revogadas. |
| Privacidade/regressão | Sentinelas em logs/respostas/stdout/stderr, inclusive falhas; health mínimo, OpenAPI/Swagger Development, ausência de probe de teste no binário normal e cadastro/reset HTTP/refresh ausentes. Sem Pluggy real. |

Atualizar somente fixtures administrativas que executam reset para aplicar `AddAuthenticationSessions` existente; manter testes deliberados de schema antigo como provas negativas. Em `PostgreSqlLoginTests`, a ordem login→reset deve passar a verificar `RevokedAtUtc` e rejeição HTTP, não apenas contagem da sessão. Adicionar/estender classes dedicadas de revogação HTTP/falhas para não confundir fixture SQL com entrega real.

Comandos de validação na futura aplicação: restore bloqueado/build da solução e `dotnet test --solution backend/Finora.slnx --no-build` em execuções separadas por `FullyQualifiedName!~PostgreSql` e `FullyQualifiedName~PostgreSql`. Para a segunda, usar exclusivamente Compose/variável de teste já protegidos, nunca conexão runtime/pessoal/Neon. Registrar números/códigos reais em AMBIENTE e tarefas; os 225/225 + 64/64 de FNR-009 continuam históricos e não validam FNR-010.

**Alternativas:** mocks sozinhos não provam atomicidade/ordenação; endpoint de reset de teste na produção ampliaria a superfície. Repetir coletas de provedor não é relevante à revogação.

## Risks / Trade-offs

- [Logout acidentalmente anônimo] → Remover anonimato da classe, validar metadados e provar 401/503 antes da action com contagem de mutações.
- [Reset em schema pré-FNR-009] → Falha fechada/rollback; exigir schema de sessões existente antes de usar o novo binário.
- [Contenção do lock global] → Preservar limite de 30 s e não manter lock durante interação TTY, HTTP externo ou execução de ação financeira.
- [Update em lote ignora tracker/validação C#] → Predicados completos, UTC/maior timestamp explícitos, constraints SQL e leitura de conferência nova em testes.
- [Usuário interpreta falha remota como logout confirmado] → Somente 204 confirma; documentar para FNR-020 limpar memória e informar falha de confirmação em rede/503, sem implementar Angular aqui.
- [Reset não desbloqueia conta] → Preservar regra existente e documentar novo login sujeito a lockout; não introduzir desbloqueio implícito.
- [Commit confirmado com resposta perdida] → Indisponibilidade sem retry; provas após commit físico e orientação sem garantia de rollback.
- [Requisição já autenticada ainda termina] → Limite temporal explícito nas specs; bloquear novas autenticações após confirmação, não prometer cancelamento retroativo.
- [Registros históricos tratados como aprovação nova] → Manter TDD 0.13/BACKLOG e aceites anteriores como origens; nova consolidação recebe revisão pendente própria.

## Migration Plan

1. Antes da implementação dependente, obter revisão/aceite de proposta, duas deltas, D1–D6 e tarefas. Consolidar detalhes aprovados no TDD §5 e registrar seleção/status FNR-010 no BACKLOG; preservar histórico e submeter o diff canônico novo a revisão quando necessário. Não alterar requisitos RF14.
2. Implementar na feature branch e validar no PostgreSQL descartável com a migration `20261118173427_AddAuthenticationSessions` existente. Não gerar migration vazia nem editar migrations/snapshot antigos.
3. Documentar a alteração de stdout e o pré-requisito de schema. Destino hospedado/migration/deploy só no fluxo autorizado de publicação; não executar nenhuma dessas operações nesta mudança de planejamento.
4. Encaminhar commits locais pelo `committer` e PR apenas quando autorizados; integração depende das revisões/checks aplicáveis e do aceite. FNR-011/020 e demais gates continuam necessários antes de publicar autenticação.
5. Reversão de aplicação, se necessária, não deve apagar sessões ou desrevogá-las. Voltar a um binário sem FNR-010 remove a garantia de revogação no reset e reabre o gate de segurança: não é rollback seguro de uma publicação autenticada sem suspensão/decisão explícita. Nenhum down migration é necessário ao escopo proposto.
6. Somente após validação/aceite, planejar sync das duas deltas e arquivamento no PR de entrega quando possível; estes artefatos não executam sync nem provam integração.

### Fontes técnicas consultadas

- [ASP.NET Core 10: autorização simples e precedência de AllowAnonymous](https://learn.microsoft.com/en-us/aspnet/core/mvc/security/authorization/simple?view=aspnetcore-10.0) — fundamenta D1, sem substituir inspeção/teste da composição local.
- [EF Core: ExecuteUpdate/ExecuteDelete](https://learn.microsoft.com/en-us/ef/core/saving/execute-insert-update-delete) — transação explícita, efeito imediato, ausência de tracking e contagem de linhas fundamentam D3; baseline local permanece EF Core 10.0.15/Npgsql 10.0.3, sem instalação por esta proposta.
