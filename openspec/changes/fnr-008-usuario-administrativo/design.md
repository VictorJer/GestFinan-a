# Design

## Context

Motivação e escopo em [proposal.md](proposal.md); contrato observável em [administrative-user-access](specs/administrative-user-access/spec.md). **Estado: aprovado explicitamente pelo titular no harness em 17/11/2026**, junto da proposta, spec e tarefas. A política de senha foi escolhida no harness em 17/11/2026; as decisões técnicas abaixo passam a ser o desenho aprovado desta mudança, sem comprovar implementação ou validação.

Inspeção read-only realizada nesta preparação:

| Evidência atual | Consequência para o desenho |
|---|---|
| `backend/Finora.Api/Program.cs` reconhece somente `pluggy-read`/`pluggy-analyze`, constrói Generic Host em Development e preserva uma assinatura fixa de `WebApplicationFactory`. | Incluir roteamento administrativo explícito, sem usar os argumentos como configuração nem quebrar o host de testes. Não reaproveitar a composição Pluggy para os novos comandos. |
| `PluggyReadCommand.cs` rejeita extras sem refletir argumentos, cria escopo e apresenta resultado tipado. | Reutilizar o padrão de isolamento/códigos, não seus timeouts, dependências ou mensagens de provedor. |
| `ApplicationUser : IdentityUser<Guid>`, `FinoraDbContext : IdentityUserContext<ApplicationUser, Guid>` e migration `20261117193137_Fnr007CorePersistence` já existem. | Não criar outro contexto, tabela de usuário, role, passkey ou migration. Ainda não há `UserManager` registrado, seed ou login. |
| `FinoraIdentityModelOptions.Configure` fixa Version2/128 e `FinoraDbContextConfiguration.Configure` aceita provider de serviços da aplicação. | Compartilhar exatamente essas opções no runtime administrativo e comprovar ausência de drift do modelo. |
| `ArchitectureTests.cs` mantém Domain/Application sem EF/Identity; Application já usa MediatR 12.5.0. | Requests/handlers e contratos próprios na Application; Identity, transação e SQL somente na Infrastructure. |
| `DisposablePostgresTestConfiguration` exige `FINORA_TEST_CONNECTION_STRING`, loopback e `finora_integration_test`; Compose de teste usa PostgreSQL 18 em tmpfs. | Reutilizar a barreira existente e dados sintéticos; nunca usar `.env`, conexão de runtime, banco pessoal ou Neon como fallback de teste. |
| Health/OpenAPI/Swagger funcionam sem banco; README contém estados históricos desatualizados em outras áreas. | Preservar os contratos e limitar as futuras edições documentais aos comandos FNR-008, sem reescrever histórico de entregas alheias. |

Fontes canônicas: [BACKLOG FNR-008, §§5/7](../../../docs/BACKLOG.md), [PRD RF14/RNF-01–03](../../../docs/PRD.md), [TDD §§3/5/7/10/14](../../../docs/TDD.md). FNR-006 satisfaz a dependência formal e FNR-007 fornece a implementação de armazenamento já integrada. `financial-data-persistence` continua inalterada. As mudanças explorador/recorrência existentes não fazem parte desta entrega.

## Goals / Non-Goals

**Goals:**

- Compor serviços Identity reutilizáveis sem registrar endpoints, cookies ou servidor HTTP no processo administrativo.
- Separar interação de terminal, casos de uso e persistência, com resultados seguros e testáveis.
- Garantir idempotência e único titular mesmo entre processos concorrentes, sem alterar schema.
- Manter um limite transacional real no reset para futura integração FNR-010, sem simular revogação.

**Non-Goals:**

- Criar um novo executável/projeto, uma biblioteca de CLI ou infraestrutura genérica de usuários.
- Incluir autenticação/sessões, mecanismos de e-mail/2FA ou persistência de tokens administrativos.
- Garantir proteção contra administrador do sistema, keylogger, dump de memória ou alteração direta do banco; a autorização operacional é a posse de acesso ao processo e às credenciais do banco.

## Decisions

### D1 — Comandos no entry point existente, com composição separada

Adicionar `user-create`/`user-reset-password` ao roteador do `Program.cs`, aceitando exatamente um token. Validar a invocação antes de construir qualquer host; nunca encaminhar argumentos desconhecidos para o host HTTP ou para providers de configuração. Preservar a assinatura estreita do host de testes e o comportamento dos comandos Pluggy.

O ramo administrativo usa Generic Host apenas como configuração/DI, sem `StartAsync`/`Run`, listener ou hosted service. A composição recebe aplicação `Finora.Api`, respeita `DOTNET_ENVIRONMENT` e o padrão Production quando ausente; User Secrets somente em Development explicitamente escolhido. Não copiar o Development forçado dos comandos Pluggy. Connection string vem de `ConnectionStrings:Finora` em configuração externa protegida; validá-la em memória antes da conexão. Não carregar `.env` automaticamente nem aceitar conexão por argumento. Não registrar cliente Pluggy ou serviços JWT nesse ramo.

Remover providers de logging **nesse host** antes de resolver Identity/EF; a saída controlada do comando é sua evidência operacional. Capturar e traduzir também falhas de configuração/construção/descartes do host, sem stack trace. O modo HTTP continua sem registro/resolução obrigatória de banco neste incremento.

**Alternativas:** projeto console separado duplicaria publicação/configuração; executar a operação em endpoint ou seed de startup amplia exposição e viola DT-25/26. Uma biblioteca de CLI não se justifica para dois tokens sem opções.

### D2 — Fronteiras e composição Identity

Na Application, criar requests/handlers específicos para consulta prévia de disponibilidade e para criação/reset, despachados por `ISender`, e um contrato de administração de acesso implementado na Infrastructure. Resultados expõem apenas estados/erros enumerados; não retornam entidade Identity, hash, security stamp ou token. O identificador interno necessário à revalidação pode circular entre camadas, nunca na saída.

Senhas transitam apenas durante o caso de uso, sem serialização, logging ou `ToString()` gerado que exponha o segredo; usar request com representação explicitamente redigida, não record padrão contendo senha. Não adicionar behaviors, notifications ou serviço genérico de repositório. Domain permanece intocado.

Registrar `FinoraDbContext` scoped com `FinoraDbContextConfiguration.Configure`, passando o provider do escopo; usar `AddIdentityCore<ApplicationUser>`/`AddEntityFrameworkStores<FinoraDbContext>`, sem roles/UI/passkeys/cookies. Aplicar `FinoraIdentityModelOptions.Configure` ao mesmo `IdentityOptions`. A raiz de composição na Api acrescenta o token provider padrão e Data Protection efêmera de D5, usando o shared framework existente, sem instalar pacotes por antecipação.

Política de senha escolhida pelo titular: fixar os defaults aprovados (`RequiredLength = 6`, quatro `Require* = true`, `RequiredUniqueChars = 1`) em composição reutilizável para criação e reset, sem opção externa que os reduza silenciosamente. Manter o hasher padrão do Identity da baseline; não confundir `IdentitySchemaVersions.Version2` do modelo com a versão do hash. Habilitar `RequireUniqueEmail`; `UserName` e `Email` recebem o e-mail informado sem espaços externos. Usar validadores e normalizador do Identity, sem regras artesanais de caixa/Gmail/remoção de pontos. Limites de e-mail/username continuam os do modelo existente (256).

**Alternativas:** escrever hash ou atualizar `PasswordHash` diretamente perde os validadores/security stamps; referenciar Identity na Application quebra DT-38. `AddDefaultIdentity`/mapeamento de APIs trazem superfície desnecessária. Não marcar `EmailConfirmed` como se tivesse havido confirmação por e-mail.

### D3 — Terminal interativo e pré-consulta sem manter transação aberta

Criar adaptador de terminal testável na Api, com leitura de teclas sem eco para senha e confirmação. Também ler e-mail sem eco para que a saída capturável não contenha dados pessoais. Suportar Enter, Backspace e Ctrl+C; rejeitar stdin/stdout redirecionados e terminal sem suporte, sem fallback para `ReadLine` com eco. Testes podem injetar adaptador falso, mas o executável não aceita modo de senha por pipe/configuração.

Após validar configuração/terminal, solicitar e-mail, remover somente espaços externos e fazer pré-consulta sem tracking. E-mail inválido falha; múltiplos usuários falham fechados. Criação de mesmo titular retorna `already_exists` antes de pedir senha; outro titular retorna conflito. Reset de ausente retorna `not_found` antes de pedir senha. Só nos caminhos mutáveis solicitar senha e confirmação; não aplicar `Trim`, transformação de caixa ou normalização à senha.

Não manter conexão/transação/lock durante a interação. A pré-consulta não autoriza a escrita: a operação final revalida tudo em novo escopo/contexto sob lock D4. Tratar EOF como entrada inválida e cancelamento como 130 antes da confirmação. Limpar buffers mutáveis em `finally`, minimizar vida útil das strings exigidas pelo Identity e não prometer zerar strings gerenciadas.

**Alternativas:** variáveis de ambiente para senha e stdin facilitariam automação, mas ampliam retenção/exposição sem requisito; uma configuração temporária de segredo era permitida no TDD, não é necessária nesta interface interativa.

### D4 — Único titular protegido por lock transacional entre processos

O serviço da Infrastructure inicia transação PostgreSQL `ReadCommitted`, adquire `pg_advisory_xact_lock` exclusivo com uma chave fixa reservada à administração do titular (a mesma em criação/reset) e **só depois**, em consulta separada, relê até dois usuários. A chave é uma constante técnica documentada, não hash de e-mail nem ID por invocação. ReadCommitted permite que a consulta posterior veja o commit de quem liberou o lock; não combinar lock e leitura num único snapshot nem usar o usuário rastreado da pré-consulta.

Se houver mais de um usuário, recusar sem correção automática. Na criação: vazio → `UserManager.CreateAsync(user, password)`; mesmo e-mail normalizado → `already_exists` sem alteração; outro → conflito. No reset: localizar o mesmo titular/identificador da pré-consulta, aplicar D5 e confirmar na mesma transação/contexto. O `SaveChanges` interno do store não substitui o commit externo. Liberar lock pelo término da transação, inclusive em rollback/cancelamento; não usar lock de sessão ou mutex somente em memória.

Respeitar cancelamento e limitar espera/operação de banco a 30 segundos após a coleta das entradas, sem timeout sobre o tempo de digitação. Não repetir mutação automaticamente em falha, timeout ou commit de resultado incerto. Conflitos otimistas de Identity ou unicidade são código 4; indisponibilidade/timeout são código 1. Emitir sucesso apenas após `CommitAsync` confirmado. Cancelamento antes do commit faz rollback; se o commit já foi confirmado, não relatar que nada foi gravado. Se a conexão cair durante commit, informar falha operacional/resultado não confirmado, sem garantir rollback que não pode ser comprovado.

**Alternativas:** somente índice por e-mail/username não impede titulares com e-mails distintos; contar usuários sem sincronização tem race. Constraint global exigiria migration/restrição do modelo que também suporta testes de propriedade com usuários sintéticos. Advisory lock preserva schema, mas coordena apenas escritores que seguem o protocolo: acesso SQL direto está fora do contrato administrativo e precisa de controle operacional.

### D5 — Reset pelo Identity com token restrito ao processo

Usar `GeneratePasswordResetTokenAsync` e `ResetPasswordAsync`, no mesmo escopo, com token apenas em variável local. A composição administrativa usa uma única instância de `EphemeralDataProtectionProvider` por processo como `IDataProtectionProvider` e o token provider padrão do Identity. Não emitir, imprimir, armazenar ou transportar o token entre processos, nem usar `RemovePasswordAsync` seguido de `AddPasswordAsync`, que cria janela sem senha. Testar validação, hash e renovação de security stamp reais.

Essa escolha evita criar key ring persistido só para um token gerado e consumido imediatamente. Não transferir o provider efêmero para a futura autenticação/recuperação entre processos; FNR-025 continua responsável por documentar os mecanismos/chaves que esses recursos efetivamente exigirem. Tokens JWT futuros têm chave e ciclo próprios.

Não modificar e-mail, `UserId`, dados financeiros, contadores de login ou lockout manualmente. Não adicionar um revogador fictício/no-op. A futura FNR-010 deve integrar revogação de todas as sessões à transação real do reset, tratar login concorrente e remover o aviso de pendência somente após testes; security stamp atualizado sozinho **não revoga JWT**. Essa dependência é gate antes da publicação autenticada, não uma tarefa de sessões escondida na FNR-008.

**Alternativas:** provider/token artesanal aumenta risco criptográfico; key ring em filesystem efêmero é dependência operacional desnecessária para este fluxo de processo único. Persistir chaves só seria necessário se o contrato passasse a compartilhar tokens entre execuções.

### D6 — Saída allowlist e códigos estáveis

Prompts são mensagens fixas sem valores inseridos. Resumo final usa prefixo `User admin:` com `operation`, `status`, `failure` e, no reset, `session_revocation=not_implemented`. Sucessos vão a stdout; falhas a stderr. Não incluir objetos de request, `IdentityError.Description`, exceções ou SQL; mapear somente códigos Identity conhecidos para mensagens fixas em português (regras de senha/e-mail), com fallback operacional constante.

| Código | Resultado |
|---|---|
| 0 | `created`, `already_exists` ou `password_reset` (distinguidos pelo estado). |
| 1 | `operational_failure`: banco/schema indisponível, timeout, exceção inesperada ou commit não confirmado. |
| 2 | `invalid_arguments`, `invalid_configuration`, `invalid_input`, `password_mismatch`, `password_policy` ou `interactive_terminal_required`. |
| 3 | `not_found`, exclusivo de reset sem usuário correspondente. |
| 4 | `conflict`: outro titular, múltiplos titulares, conflito de unicidade/concorrência ou identidade alterada após a pré-consulta. |
| 130 | `cancelled`, antes de confirmação de commit. |

Nenhum sucesso significa revogação ou prontidão hospedada. O reset imprime também aviso fixo em português: a revogação de sessões depende da FNR-010 e ainda não está implementada nesta entrega.

**Alternativas:** descrições livres do Identity podem conter nome/e-mail; deixar logs EF/host ativos pode vazar comandos/parâmetros ou configuração mesmo com `EnableSensitiveDataLogging` desativado. Apenas capturar a exceção no comando não resolve esse vazamento.

### D7 — Validação proporcional, rastreável e isolada

| Risco/contrato | Evidência a produzir na implementação |
|---|---|
| Roteamento/entrada/saída D1/D3/D6 | MSTest de tokens exatos, extras com sentinelas, terminal falso, falta de terminal, Backspace, divergência, EOF, Ctrl+C, mensagens e códigos; capturar stdout/stderr e logging. |
| Serviços e modelo D2 | Resolver DI com escopos validados; comparar opções Version2/128/modelo runtime/factory, ausência de schema drift e regras de arquitetura. |
| Criação/reset D4/D5 | Identity real e PostgreSQL descartável: hash diferente da senha, `CheckPasswordAsync`, e-mail normalizado, repetição sem alterar hash/stamps/ID, segundo titular, usuário ausente/múltiplo e vínculos preservados. |
| Concorrência/atomicidade D4 | Contextos/conexões independentes, coordenação determinística, duas criações iguais/diferentes, dois resets e falha injetada após save antes de commit; comprovar rollback e liberação do lock. |
| Privacidade/falhas | Configuração ausente/malformada, banco indisponível, schema ausente, falha de construção e exceção com sentinela; nenhuma saída/log inclui segredos ou PII. |
| Compatibilidade | Health e OpenAPI/Swagger Development/Production/Staging sem banco/Pluggy; ausência de rotas de cadastro/reset, comandos Pluggy preservados sem chamada real. |
| Terminal real | Verificação manual com dados sintéticos em terminal Windows e PostgreSQL descartável; não substituir prova de não eco por teste de `StringReader`. |

Usar `compose.test.yaml` e a allowlist existente; executar setup de schema explicitamente na fixture, nunca no código do comando. Separar testes unitários/HTTP daqueles que exigem banco. Qualquer integração não executada fica pendente/bloqueada, não aprovada por mocks. Não acessar Pluggy, Neon ou banco pessoal para validar esta mudança.

## Risks / Trade-offs

- [Política mínima de 6 caracteres é limitada] → Escolha explícita do titular, não recomendação de robustez; não fortalecê-la silenciosamente, registrar risco e exigir revisão própria se houver mudança futura. Controle de tentativas será FNR-009.
- [Acesso administrativo implica poder de redefinir o titular] → Restringir acesso ao sistema e à conexão; não aceitar chamadas HTTP ou credenciais em argumentos. Não afirmar que o comando autentica o operador pelo Finora.
- [Sem automação não interativa] → Falhar fechado sem TTY; scripts ficam fora do escopo e exigirão novo desenho de proteção.
- [Lock cooperativo não impede SQL externo] → Todos os caminhos administrativos usam a mesma chave/transação; operações diretas são responsabilidade do administrador, não garantia de unicidade física global.
- [Falha durante commit tem resultado potencialmente incerto] → Não anunciar sucesso/rollback garantido nem repetir reset automaticamente; operador pode verificar acesso/reexecutar conscientemente.
- [Senha existe brevemente em memória gerenciada] → Sem logging/serialização, buffers temporários limpos e escopos curtos; não prometer defesa contra dump/keylogger.
- [RF14 ainda não está completo] → Documentar FNR-009/010 como pendências, manter aviso do reset e bloquear publicação autenticada sem revogação transacional testada.

## Migration Plan

1. **Antes de implementar:** revisar proposta/spec/SDD/tarefas e registrar aprovação necessária. Se concretizar essas decisões no TDD canônico, preparar revisão pendente preservando a aprovação 0.11 e obter aprovação do diff; não alterar PRD/IDs ou reabrir a escolha de Identity/PostgreSQL.
2. **Durante a implementação autorizada:** usar a feature branch desta mudança e as tabelas existentes. A operação administrativa exige a migration FNR-007 já aplicada por comando separado; schema ausente é erro, não gatilho de auto-migration. Nenhuma migration nova prevista; se surgir necessidade, parar para revisar o plano afetado.
3. **Validação local:** somente banco descartável validado, restore bloqueado, build, MSTest/MTP e checks documentais/OpenSpec. Registrar comandos/resultados reais em AMBIENTE/backlog, separando-os de checks GitHub. Não repetir a evidência histórica FNR-007 apenas para preencher tarefas novas.
4. **Entrega posterior:** commits locais pelo committer, PR, revisão/aceite, integração e encerramento dependem de autorização aplicável; este planejamento não os executa. A integração FNR-008 não constitui deploy autenticado ou aceitação das FNR-009/010. Sync/arquivo somente após validação e aceite, com estado real de integração.
5. **Rollback:** reverter a versão do executável pelo fluxo de PR quando autorizado, sem excluir usuário, redefinir senha automaticamente ou desfazer migrations históricas. Credencial alterada não é restaurada pelo rollback de código; recuperação exige novo comando explícito. Sem publicação neste planejamento.

## Referências técnicas consultadas

- [Identity options, senha, usuário e hasher — ASP.NET Core 10](https://learn.microsoft.com/en-us/aspnet/core/security/authentication/identity-configuration?view=aspnetcore-10.0).
- [GeneratePasswordResetTokenAsync](https://learn.microsoft.com/dotnet/api/microsoft.aspnetcore.identity.usermanager-1.generatepasswordresettokenasync?view=aspnetcore-10.0) e [ResetPasswordAsync](https://learn.microsoft.com/dotnet/api/microsoft.aspnetcore.identity.usermanager-1.resetpasswordasync?view=aspnetcore-10.0).
- [Data Protection efêmera, inclusive console/testes](https://learn.microsoft.com/en-us/aspnet/core/security/data-protection/implementation/key-storage-ephemeral?view=aspnetcore-10.0).
- [PostgreSQL 18 — advisory locks](https://www.postgresql.org/docs/18/explicit-locking.html#ADVISORY-LOCKS) e [Read Committed](https://www.postgresql.org/docs/18/transaction-iso.html#XACT-READ-COMMITTED).

Consulta documental não é evidência de restore, compatibilidade executada ou testes desta mudança.
