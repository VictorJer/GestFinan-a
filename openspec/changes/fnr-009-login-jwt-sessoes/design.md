# Design

## Context

Motivação e escopo: [proposal.md](proposal.md). Contrato: [authenticated-sessions](specs/authenticated-sessions/spec.md). Referências canônicas: [PRD RF14](../../../docs/PRD.md), [TDD §5/§10/§14, DT-23–30/38–39/48–49](../../../docs/TDD.md) e [BACKLOG FNR-009](../../../docs/BACKLOG.md).

Estado observado em 18/11/2026:
- `backend/Finora.Api/Program.cs` separa comandos antes de montar HTTP; o ramo HTTP registra Controllers/OpenAPI e expõe somente health.
- `AdministrativeIdentityServiceCollectionExtensions` já compõe `FinoraDbContext`, `AddIdentityCore` e stores; não há SignInManager, cookies, JWT ou composição HTTP de persistência.
- `AdministrativeUserAccessService` usa ReadCommitted, timeout de 30 segundos e lock advisory transacional `0x46494E4F5241L` nos comandos de criação/reset.
- `FinoraDbContext` deriva de `IdentityUserContext<ApplicationUser, Guid>` e aplica configurações por assembly. As migrations existentes possuem campos de lockout do Identity, mas não AuthSessions. `FinoraDbContextModelTests` afirma explicitamente ausência dessa entidade.
- `ApiFactory` usa WebApplicationFactory; a suíte já possui fixture PostgreSQL descartável com barreira de destino, testes de migrations e arquitetura.
- FNR-008 está integrada pelo PR #18, mas seu encerramento documental ainda precisa ser reconciliado fora desta proposta.

Estado das decisões: DT-23–30 e baseline herdados; política de cinco falhas/cinco minutos confirmada pelo titular nesta sessão. D1–D7 abaixo são o desenho proposto para revisão, não aprovação ou implementação. O design é necessário por cruzar camadas, alterar schema e introduzir autenticação.

## Goals / Non-Goals

**Goals:** limitar a confiança ao token validado mais sessão persistida; manter atomicidade do login e contador sob concorrência; preservar diagnóstico independente de infraestrutura; preparar integração posterior da revogação sem alegar que ela está pronta.

**Non-Goals:** servidor OAuth/OIDC, endpoints Identity padrão, refresh/cookies, multiusuário, proteção das futuras APIs financeiras, endpoint público de teste, tarefas de limpeza de sessões, mudanças de UI ou execução de migration/deploy remoto.

## Decisions

### D1 — Fronteiras de composição e diagnóstico

Controller fino em Api envia comando MediatR a Application. Application define contratos/resultados de autenticação, emissão e consulta de sessão; Infrastructure implementa Identity/EF. Emissão JWT fica em adaptador Api, acessado por contrato Application, evitando dependência da Application em IdentityModel. Nenhuma criptografia própria.

Extrair composição compartilhada de contexto/Identity do registro administrativo atual, preservando os registros exclusivos dos comandos e suas opções Version2/128 e senha. HTTP não registra token provider de reset nem ativa o Generic Host administrativo. Alternativa rejeitada: copiar configurações Identity em dois pontos, com risco de drift.

Registrar autenticação Bearer explicitamente, com `UseAuthentication` antes de `UseAuthorization`. Login e health anônimos; futuras rotas protegidas usarão política autenticada explícita. FNR-011 definirá a política geral/propriedade, sem liberar rotas financeiras nesta entrega.

Configuração de autenticação é validada como uma unidade imutável sem imprimir valores. Ausência/erro coloca somente autenticação em estado indisponível: login e rotas protegidas retornam 503. Não usar ValidateOnStart que derrube health. Em estado indisponível, um handler de falha fechada substitui o Bearer; nunca criar chave padrão ou serviços de banco com conexão inventada. No estado configurado, serviços scoped acessam banco somente durante operações. Health e diagnóstico Development não provocam autenticação nem consulta de sessão, inclusive quando enviados headers Bearer inválidos. Alternativa rejeitada: exigir JWT/banco para iniciar o host, incompatível com as specs existentes.

### D2 — Contrato HTTP e controle de tentativas

`POST /api/auth/login`: JSON `{email,password}`. Aceitar e-mail até 256 caracteres e senha até 1024 caracteres, limitando corpo a 16 KiB; exceder limite de corpo retorna 413, campos inválidos retornam 400. Senha não sofre trim/normalização; espaços externos de e-mail são removidos e Identity normaliza caixa. Não reaplicar política de criação de senha ao login.

Sucesso: 200 com `accessToken`, `tokenType: Bearer`, `expiresIn: 1800`, `expiresAtUtc` ISO UTC e `Cache-Control: no-store`. Todos os resultados de login usam no-store. Falhas: 400/413 para entrada inválida; 401 idêntico para conta desconhecida, senha incorreta e bloqueio; 503 para configuração/banco/schema indisponível e conflitos operacionais; 500 sanitizado para falha inesperada. Erros usam Problem Details local à autenticação, título/código fixo e traceId, sem valor recebido, detalhes de Identity ou exceção. Challenge 401 de rota protegida mantém `WWW-Authenticate: Bearer` sem descrição interna. Cancelamento pelo cliente interrompe trabalho e não produz sucesso artificial.

Usar UserManager para checar senha e gerir `IsLockedOutAsync`, `AccessFailedAsync` e `ResetAccessFailedCountAsync`, verificando cada IdentityResult. Configurar explicitamente AllowedForNewUsers=true, MaxFailedAccessAttempts=5 e DefaultLockoutTimeSpan=5 minutos. Para o titular legado com LockoutEnabled=false, habilitar sob a mesma transação antes de avaliar o login. Não expor desbloqueio administrativo nem modificar o reset. Falhas durante bloqueio não incrementam nem prorrogam prazo. Sucesso zera contador; falha persistida é confirmada antes de retornar 401. Falha na gravação do contador retorna 503, não 401.

Para conta inexistente, executar uma verificação de hash sintético com o hasher configurado, sem usuário persistido nem log do material; reduz diferença evidente de custo, sem prometer tempo constante. Sem contagem global por e-mail inexistente. Rate limiting distribuído/IP não entra implicitamente nesta política; deverá ser avaliado na segurança geral FNR-011.

Alternativa rejeitada: PasswordSignInAsync/cookies ou MapIdentityApi, que introduziriam mecanismos/endpoints fora do contrato. O fluxo explícito mantém os efeitos de lockout auditáveis e testados.

### D3 — Atomicidade e concorrência

Login abre transação ReadCommitted e adquire o mesmo lock advisory transacional dos comandos administrativos antes de consultar o titular e validar credenciais. Centralizar a chave/protocolo em helper interno compartilhado, preservando o comportamento administrativo. Sob lock: detectar estado de múltiplos usuários (503), carregar titular, conferir bloqueio, verificar senha, confirmar contador ou criar sessão. Timeout de operação de 30 segundos inclui espera pelo lock; cancelamento HTTP é propagado. Sem retry automático de login/commit.

Em sucesso, preparar token em memória após validação e antes do commit, gravar sessão e reset de contador na mesma transação; entregar o token somente após confirmação. Falha de assinatura antes do commit desfaz alterações. Falha de confirmação do commit não entrega token nem alega rollback garantido; uma sessão órfã pode expirar naturalmente. Repetição explícita de login cria outra sessão, não retorna token anterior.

Alternativa rejeitada: ler senha fora do lock e salvar depois, pois perde incrementos concorrentes e permite usar estado obsoleto durante reset. O lock global é proporcional ao único titular, mas serializa também tentativas inválidas. Ele prepara a ordenação necessária à FNR-010; não revoga sessões nem resolve sozinho logout/reset. FNR-010 deverá revogar sessões dentro da transação de reset: login anterior ao reset cria sessão a revogar; login posterior revalida a nova credencial.

### D4 — Modelo de sessão e migration

Entidade de persistência de autenticação em Infrastructure, sem dependências Identity no Domain financeiro. Tabela `AuthSessions`: `Id` varchar(64), chave primária; `UserId` UUID obrigatório/FK AspNetUsers com Restrict; `CreatedAtUtc` e `ExpiresAtUtc` timestamptz obrigatórios; `RevokedAtUtc` timestamptz opcional. Id representa 32 bytes de aleatoriedade criptográfica em base64url sem padding (43 caracteres), usado como `jti`. Índice por UserId para revogação futura. Constraints: expiração maior que criação e revogação nula ou posterior/igual à criação; formato/comprimento de Id validado na fronteira de persistência.

Não armazenar JWT, senha, e-mail, security stamp, IP ou user agent. Uma sessão não usa token tables do Identity: o estado e as consultas por expiração/revogação são próprios. Criar migration incremental sem reescrever FNR-006/007, sem seed e sem execução automática. Atualizar asserção de ausência de AuthSession para conferir presença/colunas/FK/minimização, mantendo demais limites do modelo.

### D5 — Emissão, validação e operação das chaves

Concretização proposta da DT-39: HS256 exclusivamente; chave base64 contendo pelo menos 32 bytes aleatórios, estável por ambiente, fornecida por segredo externo. `Authentication:Jwt` define Issuer, Audience e SigningKeyBase64; sem valores operacionais no repositório. Usar `Microsoft.AspNetCore.Authentication.JwtBearer` na linha 10.0 e `Microsoft.IdentityModel.JsonWebTokens` para emissão. Versão candidata do JwtBearer: 10.0.15 alinhada ao projeto; versão exata IdentityModel deve acompanhar o grafo compatível e ser registrada em AMBIENTE/locks após consulta de metadados, licença e advisories, antes de instalar. Troca de baseline/linha exige decisão explícita.

Claims: `sub` UUID canônico, `jti` no formato acima, `iss`, `aud`, `iat`, `exp`; cabeçalho `typ: JWT`. Sem claims duplicadas obrigatórias, papéis ou dados pessoais adicionais. `iat`/criação e `exp`/expiração usam o mesmo instante UTC truncado a segundos; expiração = emissão + 1800 segundos. `expiresIn` expressa validade nominal desde emissão, não garantia de 1800 segundos depois da recepção.

JwtBearer com `MapInboundClaims=false`, assinatura obrigatória, HS256 allowlist, issuer/audience exatos e validação de lifetime. ClockSkew zero para preservar a expiração estrita de 30 minutos e igualdade com a sessão. Rejeitar emissão no futuro, identificadores malformados e claims duplicadas/ausentes antes de consultar o banco. Usar relógio controlável nos testes e mesma referência temporal na emissão/consulta; não depender de sleeps longos.

Rotação proposta: troca coordenada de chave/configuração e reinício de todas as instâncias; sem chave anterior aceita. Tokens antigos deixam de validar, e titular faz novo login. Reinício sem rotação mantém tokens válidos, se sessão ativa. FNR-025 detalhará operação de publicação; FNR-009 documenta/testa comportamento sem rotacionar segredos reais. Alternativas rejeitadas: chave efêmera por processo (invalidação acidental), chave assimétrica/JWKS (complexidade não exigida pelo único emissor/consumidor).

### D6 — Validação da sessão e erros operacionais

Depois da validação criptográfica, `OnTokenValidated` chama contrato scoped de consulta na Infrastructure. Consultar sem tracking por jti/sub, confirmar existência do usuário, revogação nula, prazo ainda válido e correspondência de criação/expiração com iat/exp. Nenhum cache positivo. Identidade confiável usa `sub`; corpo/filtro não escolhe titular.

Sessão inválida: falha de autenticação/401. Falha operacional: sinal tipado interno para tratamento HTTP de autenticação produzir 503, sem virar challenge 401 por acidente; não lançar exceção bruta na resposta. Garantir que nenhum principal parcialmente validado alcance Controller. Não transformar cancelamento em senha incorreta. Testar evento/handler e pipeline HTTP real, incluindo indisponibilidade após assinatura válida.

Como não há rota de negócio protegida neste escopo, registrar Controller de prova somente no assembly/host de testes com `[Authorize]`; retorno mínimo comprova execução e identidade. Ele não integra o binário ou OpenAPI de produção. Revogação de fixture por SQL testa rejeição de estado, não implementação de endpoint logout.

Alternativa rejeitada: JWT stateless ou cache de sessão, incompatíveis com DT-30. Comparação com security stamp para invalidar sessões automaticamente fica fora: não substituir a entrega transacional FNR-010 por um mecanismo parcial.

### D7 — Validação e rastreabilidade

Testes unitários: contratos sem ToString sensível, normalização, configuração, emissão/claims, relógio/limites e erros tipados. HTTP com autenticação real: sucesso, entradas inválidas, challenge, adulteração, algoritmo/issuer/audience/claims e 503. PostgreSQL descartável real: lockout entre contextos/reinício, cinco falhas concorrentes, sessões distintas, commit/rollback/falha incerta, FK/constraints, migration em base vazia e atualização com titular/grafo financeiro existentes.

Regressões: health sem banco/JWT/Pluggy inclusive com header inválido; OpenAPI/Swagger só Development; comandos administrativos e Pluggy continuam isolados; ausência de rotas extras, auto-migration e segredos. Usar barreira `FINORA_TEST_CONNECTION_STRING` da fixture existente, sem banco pessoal/Neon/coleta real. Atualizar README/AMBIENTE, BACKLOG e somente decisões novas do TDD durante implementação autorizada, preservando aprovações históricas e obtendo revisão documental necessária antes de código dependente. Não alterar PRD para repetir RF14.

Fontes consultadas via Context7 em 18/11/2026: [configuração Identity/lockout](https://learn.microsoft.com/aspnet/core/security/authentication/identity-configuration?view=aspnetcore-10.0), [validação JwtBearer](https://learn.microsoft.com/aspnet/core/security/authentication/configure-jwt-bearer-authentication?view=aspnetcore-10.0). São referências de APIs; a emissão local é a arquitetura já escolhida no TDD, não adoção de servidor OAuth.

## Risks / Trade-offs

- Bloqueio do único titular por terceiros → duração curta confirmada, resposta genérica, documentação; rate limiting geral é FNR-011.
- Hash e lock serializam tentativas → timeout/cancelamento e testes concorrentes; adequado ao escopo de um titular, sem promessa de escala.
- Consulta ao banco em toda chamada → custo deliberado para revogação imediata; falha retorna indisponibilidade, nunca acesso permissivo.
- API pode estar viva com autenticação indisponível → health permanece liveness; documentação diferencia disponibilidade de autenticação.
- Chave rotacionada/relógios divergentes → reinício coordenado, novo login e relógios UTC sincronizados; nenhuma tolerância expande validade.
- Revogação ainda incompleta → bloqueio de publicação autenticada até FNR-010 validada; lock compartilhado não é prova de revogação.
- Resultado incerto no commit → não devolver token nem repetir automaticamente; sessão eventual expira.

## Migration Plan

1. Antes de implementar, reconciliar encerramento FNR-008 e obter aprovação desta proposta/spec/design/tasks; registrar revisões canônicas necessárias em feature branch.
2. Adicionar migration somente para AuthSessions. Demonstrar aplicação explícita em base vazia e upgrade da FNR-007 com usuário/dados sintéticos preservados; testar rollback removendo somente a nova tabela e reaplicação.
3. Validar implementação e aceite com banco descartável. Não executar schema changes no startup nem em banco real durante essa validação.
4. Integrar por PR conforme autorizações aplicáveis. Não publicar autenticação pela conclusão desta mudança; FNR-010/011 e fluxo de publicação ainda se aplicam.
5. Quando houver destino autorizado, aplicar migration compatível antes da integração que dispara deploy dependente. Rollback de aplicação pode manter tabela adicional; down destrutivo apenas sob decisão explícita, pois remove sessões e exige novo login. Não apagar usuário ou dados financeiros.
