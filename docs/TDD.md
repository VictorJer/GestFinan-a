# Finora — TDD v0

**Technical Design Document — documento de desenho técnico**  
**Estado:** v0, revisão documental FNR-001 aprovada explicitamente pelo autor em 13 de novembro de 2026; propostas técnicas e validações pendentes identificadas no texto, sem aprovação de implementação futura.\
**Base:** Finora — PRD v0, revisão 0.2.\
**Data de início:** 9 de novembro de 2026.

## 1. Decisões confirmadas

| Área | Decisão |
|---|---|
| Arquitetura | Monólito modular: uma API, um frontend e um banco, com organização por funcionalidades |
| Projetos backend | Finora.Api, Finora.Application, Finora.Domain e Finora.Infrastructure |
| Endpoints | Controllers |
| Backend | ASP.NET Core em .NET 10 |
| Frontend | Angular 22, conforme versão informada pelo autor |
| Node | Versão 24, conforme ambiente informado pelo autor |
| Persistência | Entity Framework Core 10 com migrations |
| Acesso à persistência | Repositórios específicos, com contratos na Application e implementações na Infrastructure; sem repositório genérico |
| Mapeamento | Explícito em C# no MVP; sem AutoMapper |
| Interface visual | Templates HTML do Angular com Tailwind CSS; sem Angular Material |
| Gráficos | Chart.js em componentes Angular próprios |
| Testes backend | MSTest, em substituição à proposta de xUnit |
| Repositório Git | Único, com frontend e backend em pastas separadas e publicações independentes |
| Banco de desenvolvimento | PostgreSQL local em Docker, separado da Neon |
| Migrations | Comando administrativo antes da publicação; não executar na inicialização da API |
| Publicação de versões | Automática após atualização da main por integração de PR, condicionada aos checks aprovados do commit publicado; sem push direto |
| Condição para publicar | Build e testes aprovados; falha impede nova publicação |
| Estado no Angular | Serviços com Signals; HttpClient/RxJS para chamadas HTTP; sem NgRx no MVP |
| Banco | PostgreSQL na Neon; substitui a escolha anterior de SQL Server |
| Frontend hospedado | Cloudflare Pages, plano gratuito, publicação estática |
| Backend hospedado | Render Free, publicação por Docker |
| Publicação | Frontend e API em hospedagens separadas |
| Público | Apenas o autor, sem cadastro público |
| Autenticação | E-mail e senha com ASP.NET Core Identity |
| Criação do usuário | Comando administrativo inicial, sem cadastro público e sem senha fixa no código |
| Recuperação de acesso | Redefinição por comando administrativo no MVP; sem envio de e-mail |
| Configuração da Pluggy | Credenciais e itemId nos segredos do backend; sem autorização bancária iniciada pelo Angular no MVP |
| Sessão no MVP | Token de acesso por 30 minutos, somente na memória do Angular; novo login ao recarregar ou expirar |
| Token | JWT assinado; Identity valida as credenciais e JwtBearer valida as chamadas |
| Revogação | Sessões persistidas no PostgreSQL e verificadas em cada requisição autenticada; logout revoga a sessão atual e redefinição de senha revoga todas |
| Evolução da sessão | Manter login ao recarregar, com mecanismo de persistência e renovação a definir futuramente |
| Sincronização | Ao entrar, se os dados estiverem antigos, e por botão de atualização |
| Dados antigos | 24 horas desde a última importação completa bem-sucedida; intervalo configurável |
| Histórico inicial | Até 12 meses anteriores, limitado aos dados disponíveis no provedor; parcelas futuras em situação separada |
| Edição de compras | Categoria e observação pessoal; preservar valor, data e descrição originais |
| Categorias | Conjunto inicial e criação de novas categorias pelo usuário |
| Docker | Autor conhece o básico; documentação deverá incluir passos de build e publicação |

Escolhas de plano não garantem gratuidade ilimitada nem disponibilidade contínua. Limites devem ser conferidos na publicação. A aplicação deve tolerar a retomada do backend após inatividade.

## 2. Visão de implantação

```text
Navegador
  ├── HTTPS → Cloudflare Pages → arquivos estáticos do Angular
  └── HTTPS → Render → API ASP.NET Core
                        ├── conexão TLS → PostgreSQL na Neon
                        └── HTTPS → API da Pluggy
```

O Angular não acessará diretamente o banco ou a API da Pluggy com credenciais de servidor. Client Secret, chave da API e string de conexão devem permanecer no backend e fora do repositório.

A API deve exigir autenticação e autorização em todos os endpoints de dados financeiros. A tela de login pode ser pública; isso não torna os dados públicos. CORS deve ser restrito às origens autorizadas e não substitui autenticação.

## 3. Estrutura confirmada

```text
Finora.Api             endpoints, autenticação e composição das dependências
Finora.Application     casos de uso e contratos de entrada/saída
Finora.Domain          entidades e regras de negócio
Finora.Infrastructure  EF Core, PostgreSQL e cliente da Pluggy
finora-web             Angular
```

Organizar as funcionalidades por módulos dentro dessa estrutura: integração, transações, dashboard e investimentos. Endpoints implementados em Controllers no projeto Finora.Api; regras financeiras e importação não devem ficar nos Controllers.

Direção proposta das referências: Domain não referencia os outros projetos; Application referencia Domain; Infrastructure implementa os contratos necessários e referencia Application/Domain; Api referencia Application e Infrastructure para compor a aplicação. Contratos HTTP não devem expor entidades do EF ou respostas brutas da Pluggy.

Application acessa a persistência por contratos específicos, como ITransactionRepository, implementados na Infrastructure com EF Core. As interfaces devem atender às operações dos casos de uso; não retornar IQueryable para que a camada Application componha consultas dependentes do provedor. Fronteiras de transação e gravação serão detalhadas com o fluxo de importação.

O mapeamento entre contratos externos, entidades e DTOs será explícito em C#. A normalização de valor, moeda, status e natureza da transação deve ter regras verificáveis; não é mera cópia de campos.

O frontend usará templates HTML do Angular com Tailwind CSS, preservando o protótipo como referência visual. Não adotar Angular Material. Componentes próprios devem contemplar foco, navegação por teclado, rótulos, mensagens de erro e estados de carregamento.

### Repositório e frontend

Um único repositório Git conterá `backend/`, `frontend/` e `docs/`. Cloudflare Pages e Render publicarão suas respectivas partes de forma independente. A configuração de publicação precisa definir os diretórios de trabalho e os caminhos que disparam cada build.

O Angular manterá estado em serviços com Signals e usará HttpClient/RxJS para as chamadas HTTP, sem NgRx no MVP. Separar funcionalidades de autenticação, integração, transações, dashboard e investimentos; manter componentes reutilizáveis em uma área compartilhada. Gráficos implementados com Chart.js em componentes próprios, com descrição acessível e resumo dos valores em HTML. A ferramenta de testes do frontend ainda será definida.

O token de sessão deve ficar em um serviço de autenticação, apenas em memória. O interceptor de autenticação só pode anexá-lo a requisições para a API configurada do Finora. A resposta 401 deve encerrar a sessão local e orientar novo login; uma resposta 403 deve informar falta de permissão sem iniciar uma repetição de login.

## 4. Integração bancária: ponto de partida

O autor já conectou o Nubank ao Meu Pluggy e visualizou cartão e transações no painel do provedor. Informou também ter guardado Client ID, Client Secret e itemId. Os valores não devem ser inseridos neste documento.

A leitura pela API .NET, os campos efetivamente disponíveis, a paginação e a normalização de status e valores ainda precisam ser validados. A prova de conceito continua sendo o primeiro marco técnico.

Essa prova de conceito (FNR-004) será local, após a preparação documental, de ambiente e da estrutura mínima (FNR-001 a FNR-003), sem depender de dashboard ou autenticação hospedada já implementados. Não publicar endpoints financeiros desprotegidos; a evidência deve ser sanitizada. FNR-005 validará o contrato e a normalização antes de fechar a importação persistente.

O dashboard deve consultar o banco da aplicação. Uma nova coleta externa não deve ser executada a cada mudança de tela ou filtro.

Confirmado: configurar Client ID, Client Secret e itemId nos segredos do backend. No MVP, o Angular exibe estado da conexão e permite consultar atualizações; a autorização ou renovação junto ao banco é realizada no ambiente do provedor. Não publicar uma tela que solicite senha bancária. A orientação de reconexão deve diferenciar a configuração no provedor da sessão de login do Finora.

## 5. Autenticação

Confirmado: Identity, e-mail/senha, apenas um usuário e nenhum cadastro público.

Decisão confirmada para o MVP: token de acesso com validade de 30 minutos, armazenado apenas na memória do Angular. Recarregar ou fechar a página perde a sessão local. A expiração exige novo login; a interface deve explicar o motivo. Não armazenar o token em localStorage ou sessionStorage.

Não haverá renovação automática no MVP. Não emitir ou disponibilizar refresh token ao frontend por conveniência; o desenho de permanência do login será uma evolução explícita. O logout confirmado pelo servidor revoga a sessão atual e o Angular remove seu token da memória. Se a chamada falhar, limpar a memória local e informar que não foi possível confirmar o encerramento no servidor; não apresentar revogação remota como concluída.

Criação do usuário confirmada por comando administrativo inicial, fora dos endpoints públicos. A implementação deverá usar os serviços do Identity para validar e gerar o hash da senha, obter os dados por entrada protegida ou configuração temporária de segredos e não registrar senha em saída, histórico ou código. O comando deve detectar usuário existente e não redefinir sua senha silenciosamente. Não executar provisionamento com senha a cada inicialização da API.

Recuperação de acesso confirmada por comando administrativo de redefinição no MVP, sem serviço de e-mail. Usar os serviços do Identity e entrada protegida para a nova senha; não aceitar senha por argumentos que apareçam no histórico ou na lista de processos. A resposta operacional não pode exibir o segredo. A invalidação das sessões após redefinição deverá ser tratada no mecanismo de autenticação escolhido.

Não expor os endpoints padrão de cadastro indiscriminadamente. O backend valida identidade e autorização em todas as operações financeiras; o frontend não é a barreira de acesso.

Desenho confirmado: Identity valida credenciais, uma biblioteca de tokens emite JWT assinado para a API do Finora e o middleware JwtBearer valida assinatura, algoritmo, emissor, destinatário e expiração. Claims mínimas propostas: identificador do usuário (`sub`), identificador da sessão/token (`jti`), emissor (`iss`), destinatário (`aud`), emissão (`iat`) e expiração (`exp`). Não incluir dados financeiros, senha ou credenciais da Pluggy. O JWT assinado não deve ser tratado como conteúdo confidencial criptografado.

Direção técnica de assinatura para este único backend emissor e consumidor: HS256, chave aleatória com pelo menos 256 bits guardada em segredo estável entre reinicializações, fora do repositório e diferente por ambiente. Validar o algoritmo explicitamente. Usar bibliotecas consolidadas para gerar/validar o token; não implementar criptografia própria. Definir tolerância pequena de relógio e processo explícito de rotação na configuração de autenticação.

### Sessões e revogação imediata

Persistir uma sessão em `AuthSessions` para cada login, com identificador aleatório de alta entropia correspondente ao `jti`, UserId, CreatedAtUtc, ExpiresAtUtc e RevokedAtUtc opcional. Não persistir o JWT completo. Somente entregar o token após gravar a sessão com sucesso.

Após validar criptograficamente o JWT, consultar a sessão pelo identificador e usuário e exigir que esteja ativa e não expirada. Sessão ausente, revogada ou expirada não autoriza a chamada. Não usar cache que crie uma janela de aceitação de sessões revogadas no MVP. A autenticação, portanto, não é totalmente stateless: a consulta ao PostgreSQL é uma decisão deliberada para revogação imediata.

O logout revoga apenas a sessão atual. A redefinição de senha revoga todas as sessões do usuário; alteração de senha e revogação devem ser transacionais. Invalidar também sessões anteriores a uma mudança de senha/versão de segurança, para que um login concorrente iniciado com credenciais antigas não crie sessão válida após a redefinição. Em caso de indisponibilidade do banco, falhar sem liberar dados e informar indisponibilidade; não confundir falha operacional com senha incorreta.

Frontend e API separados usarão o cabeçalho Bearer no MVP, sem cookies de autenticação entre sites. A eventual persistência de login será redesenhada futuramente; não habilitar localStorage ou refresh token silenciosamente como atalho.

## 6. Sincronização

Confirmado: ao entrar na aplicação, consultar os dados disponíveis no provedor se estiverem antigos; permitir também atualização por botão. A navegação entre telas e mudanças de filtro consultam o banco local, sem acionar uma nova importação.

Limiar confirmado para dados antigos: 24 horas desde a última importação completa bem-sucedida, configurável no backend. Se nunca houve importação, tentar a primeira ao entrar. Falha ou importação incompleta não reinicia esse prazo.

Propostas operacionais para detalhamento: usar um único fluxo de importação para entrada e botão; impedir duas importações simultâneas da mesma conexão; desabilitar o botão durante a execução e aplicar um intervalo mínimo entre tentativas. Erros não avançam o horário de sucesso nem apagam o histórico. O intervalo mínimo e a política de nova tentativa serão definidos com os limites da API.

Exibir separadamente a última importação bem-sucedida no Finora e a atualização na origem, quando informada. Consultar o provedor não força atualização imediata no Nubank. Nenhuma rotina agendada com o aplicativo fechado foi solicitada.

Importação inicial confirmada: solicitar até 12 meses anteriores à referência de execução, sem prometer histórico que o provedor não retornar. Não eliminar parcelas futuras retornadas por serem posteriores à referência: preservá-las com classificação própria e fora dos gastos efetivados. Registrar cobertura solicitada e efetivamente disponível quando identificável.

Categoria e observação pessoal são os únicos campos de compra editáveis pelo usuário. Separar esses campos dos dados da origem, para que uma sincronização possa atualizar descrição, data, valor e situação do provedor sem apagar a organização pessoal. Não oferecer endpoint de exclusão ou alteração de valor de compra importada.

## 7. Validações no início da implementação

1. Confirmar versões instaladas e compatibilidade de pacotes; fixar as dependências e o runner de testes do frontend.
2. Validar pela API real da Pluggy paginação, histórico, identidade de contas/transações, sinais, datas e situações. Ajustar a normalização aos resultados observados.
3. Medir duração e volume da primeira importação e validar a proposta de processamento persistente da seção 12.
4. Conferir limites dos provedores, intervalo mínimo e parâmetros de repetição antes da publicação.
5. Verificar emissão/revogação do JWT, comandos administrativos e persistência das chaves de proteção usadas pelos recursos do Identity.
6. Configurar publicação do commit aprovado pelos checks sem disparos paralelos dos provedores.

As escolhas principais de arquitetura e ferramentas estão fechadas nesta versão. As validações acima são tarefas técnicas, sem necessidade de novas rodadas sobre detalhes rotineiros, salvo descoberta que altere escopo, custo ou comportamento definido.

## 8. Referências técnicas consultadas

- Identity para SPAs: https://learn.microsoft.com/en-us/aspnet/core/security/authentication/identity-api-authorization?view=aspnetcore-10.0
- EF Core para PostgreSQL: https://www.npgsql.org/efcore/
- Angular no Cloudflare Pages: https://developers.cloudflare.com/pages/framework-guides/deploy-an-angular-site/
- Limites do Render Free: https://render.com/docs/free
- Integração pessoal Pluggy: https://docs.pluggy.ai/en/docs/guides/meu-pluggy-personal-use

Este documento registra decisões e propostas de implementação; não representa componentes já implementados ou validados. A revisão 0.2 do PRD incorpora os comportamentos já confirmados de acesso hospedado, categorias pessoais, observações e atualização. Detalhes técnicos permanecem neste TDD. As propostas das seções 10–12 e 15 continuam propostas, não aprovações implícitas pela revisão documental.

## 9. Desenvolvimento e publicação

Decisões confirmadas: PostgreSQL local em Docker para desenvolvimento; PostgreSQL na Neon para o ambiente hospedado; migrations executadas por comando administrativo antes da publicação; publicação automática de frontend e API após integração de PR na `main`, somente com os checks aprovados do commit a publicar.

Não executar `Database.Migrate()` como efeito da inicialização da API. Separar as configurações de desenvolvimento e ambiente hospedado e não usar o banco da Neon como base de testes automatizados.

### Sequência operacional proposta

1. Selecionar uma mudança do backlog e criar uma feature branch. Conferir PRD → TDD → SDD no OpenSpec, com rastreabilidade e revisão da especificação antes de implementar. Artefatos existentes ou completos no CLI não significam aprovação humana.
2. Após as revisões necessárias, implementar somente o escopo aprovado, gerar a migration quando necessária e validá-la no PostgreSQL local; executar os testes aplicáveis.
3. Abrir PR vinculando PRD, TDD, SDD/OpenSpec e item do backlog; registrar testes, riscos e solicitar revisão. Não substituir aprovações documentais pendentes pela abertura ou aprovação do PR.
4. Para alteração de esquema, após revisão e checks necessários e antes da integração que disparará a publicação dependente, aplicar o comando administrativo no ambiente hospedado, com as credenciais do destino correto. Não executar comandos destrutivos sem plano específico de migração e recuperação.
5. Integrar na `main` exclusivamente pelo PR, após revisões, aprovações, checks e migration aplicável bem-sucedida, sem contornar proteções. Publicar automaticamente apenas após os checks aprovados do commit a publicar; se falharem, bloquear o deploy.
6. Verificar a saúde da API, o login e os fluxos afetados após a publicação. Registrar evidências e atualizar o backlog; manter apenas uma mudança principal em implementação por vez.

Como a versão anterior da API pode continuar ativa durante a migration, as alterações devem ser compatíveis com ela. Mudanças incompatíveis exigem etapas separadas ou janela de manutenção explicitamente planejada. Falha na migration bloqueia a integração do PR que dispararia a publicação dependente. Não fazer push direto na `main` como atalho.

Confirmado: build e testes são uma condição obrigatória para publicar. Se os checks falharem, a nova versão não deve ser enviada à hospedagem. O pipeline deverá evitar que o deploy nativo dos provedores dispare em paralelo antes do resultado dos checks; o mecanismo exato de acionamento será definido na configuração da integração contínua.

A publicação deve usar o commit verificado. Publicações mais antigas não devem sobrescrever uma versão mais nova por terminarem depois. Frontend e API têm implantações independentes, sem atomicidade garantida entre provedores; contratos compatíveis reduzem esse risco. Credenciais de publicação e de banco devem ficar em configurações protegidas, fora dos arquivos versionados.

## 10. Modelo de dados — desenho técnico proposto

As decisões de negócio acima são confirmadas. Tipos, nomes e índices abaixo são uma proposta técnica para implementar essas decisões e podem ser ajustados ao validar o contrato real da Pluggy.

| Entidade | Campos principais e finalidade |
|---|---|
| ApplicationUser | Usuário gerenciado pelo Identity; credenciais com hash e versão de segurança |
| AuthSession | Id/jti, UserId, emissão, expiração, revogação e referência de versão de segurança |
| FinancialConnection | Id, UserId, Provider, ExternalItemId, situação, referência de expiração, última tentativa, último sucesso e atualização na origem |
| CardAccount | Id, UserId, ConnectionId, identificador externo, nome, final mascarado, moeda e dados opcionais de crédito |
| FinancialTransaction | Id, CardAccountId, identificador externo, descrição original, valor, moeda, data original/normalizada, natureza, situação e dados opcionais de parcela/fatura |
| Category | Id, nome, nome normalizado, UserId opcional e indicador de categoria de sistema; categorias iniciais e categorias criadas pelo titular |
| TransactionAnnotation | TransactionId único, CategoryId opcional, observação pessoal e UpdatedAtUtc |
| SyncRun | Id, ConnectionId, início, fim, estado, intervalo solicitado, contadores e código de falha sanitizado |
| Investment — P1 | Id, UserId, nome, classe, valor aplicado, valor atual e datas de criação/alteração |

### Convenções

- IDs internos como UUID; os identificadores externos permanecem strings opacas, sem reinterpretar seu formato.
- UserId obtido da identidade autenticada, nunca aceito livremente do corpo da requisição. Mesmo com apenas um usuário, consultas devem verificar propriedade dos recursos.
- Dinheiro em `decimal` no .NET e `numeric(19,4)` no PostgreSQL; moeda identificada separadamente. Arredondar para apresentação segundo a moeda, sem arredondamentos intermediários desnecessários.
- Horários de execução, criação e auditoria em UTC, com tipo apropriado no PostgreSQL. Preservar a semântica de data da origem: um campo que representa apenas um dia não deve mudar de dia por conversão de fuso.
- Guardar data normalizada de competência da transação para filtros; não confundir com vencimento de fatura. A regra de normalização depende da prova de conceito.
- Restrição única para sessão/jti, para anotação por transação e para identificador externo de transação dentro da conta normalizada.
- Índices para conta + data + Id, categoria da anotação, conexões por usuário e execuções por conexão/estado. Refinar com consultas reais, sem índices redundantes por antecipação.
- FKs, limites de texto e precisão definidos nas configurações do EF Core. Migrations versionadas na Infrastructure.
- Não armazenar número completo do cartão, senha bancária ou respostas de identidade desnecessárias.

### Reconexões e exclusões

ExternalItemId é referência da conexão, não identidade permanente do cartão. Se mudar, reconciliar a conta e os identificadores de transação antes de importar. Se não houver informação confiável para reconhecer a continuidade, interromper a reconciliação automática e orientar revisão; não usar apenas nome ou final do cartão para fundir contas.

Um registro ausente numa resposta parcial ou vazia não é uma exclusão. Atualizar lançamentos conhecidos pela identidade externa e manter histórico. Exclusões/cancelamentos na origem exigem evidência contratual da API, não inferência por ausência.

## 11. Contratos HTTP — proposta inicial

Base: `/api`. Todos os endpoints financeiros exigem JWT válido, sessão ativa e propriedade do recurso. O retorno de health deve ser mínimo e não expor segredos ou erros internos. Contratos de resposta usam DTOs próprios.

| Método e rota | Responsabilidade |
|---|---|
| POST /api/auth/login | Validar credenciais; criar sessão; retornar accessToken, tokenType e expiresIn, sem refreshToken |
| POST /api/auth/logout | Revogar a sessão atual |
| GET /api/auth/me | Retornar identificação mínima do usuário autenticado |
| GET /api/connections/current | Situação da conexão configurada, horários e necessidade de intervenção; sem credenciais |
| POST /api/connections/current/sync | Solicitar atualização; origem automática verifica o prazo de 24h no servidor, manual respeita intervalo mínimo |
| GET /api/sync-runs/{id} | Estado e contadores da execução pertencente ao usuário |
| GET /api/cards | Listar cartões disponíveis para o titular |
| GET /api/transactions | Filtrar por conta, intervalo, categoria, situação e busca; retornar página e total filtrado |
| PATCH /api/transactions/{id}/annotation | Alterar categoria e observação; validar propriedade e limites dos campos |
| GET /api/categories | Listar categorias disponíveis |
| POST /api/categories | Criar categoria pessoal, validando nome e duplicidade |
| GET /api/dashboard | Resumo agregado do período, com critérios e cobertura explícitos |
| GET /api/transactions/export — P1 | CSV das transações filtradas |
| GET/POST /api/investments — P1 | Listar/criar investimento manual |
| PUT/DELETE /api/investments/{id} — P1 | Atualizar/excluir investimento manual do usuário |
| POST /api/simulations/calculate — P1 | Validar parâmetros e retornar projeção calculada no backend |
| GET /health | Estado mínimo para verificação da aplicação |

Paginação proposta de transações: `page`, `pageSize` com padrão 25 e máximo 100; ordenação estável por data e Id. Resposta com itens, página, tamanho, quantidade total e total monetário elegível por moeda. A soma apresentada para o filtro deve considerar todo o conjunto filtrado, não apenas os itens da página.

Usar intervalo de datas explícito e validado, com início incluso e fim exclusivo no contrato técnico, convertido de forma consistente pelos filtros de mês da interface. Não enviar filtros SQL arbitrários ou expressões de ordenação não permitidas.

Erros em Problem Details, com código de aplicação e traceId sanitizado. Usar 400 para entrada inválida, 401 para credenciais/sessão inválidas, 403 para acesso sem permissão, 404 para recurso inexistente/não acessível, 409 para conflito, 429 para limite de tentativa e 503 para dependência indisponível. Não retornar stack traces ou mensagens brutas do provedor.

## 12. Execução da sincronização — proposta

O processamento deve ter registro persistente e idempotência, sem depender de uma tarefa em memória iniciada pelo Controller e abandonada após responder.

Proposta: o endpoint registra uma SyncRun e responde 202 com seu identificador; um BackgroundService na própria API processa execuções pendentes enquanto a instância está ativa. A interface consulta o estado em intervalos limitados. Não há serviço de fila externo nem execução por horário com o aplicativo fechado. O despertar da instância é provocado por acesso normal à aplicação.

Somente uma execução por conexão pode processar por vez. Usar controle persistente de posse/expiração e retomada idempotente para lidar com reinicializações. Ao reiniciar, detectar execuções interrompidas; marcar/reprocessar com controle de tentativas, sem deixá-las eternamente em andamento.

Fluxo: validar conexão → recuperar contas → paginar os lançamentos do intervalo → normalizar → persistir de forma consistente → publicar sucesso e horários. Evitar transação de banco aberta durante chamadas HTTP externas. O desenho de preparação e gravação deve impedir que uma importação incompleta seja apresentada como conjunto completo; para o volume do MVP, reunir um intervalo limitado antes da gravação transacional é a primeira alternativa a medir. Caso seja necessário processar em lotes, introduzir área de preparação identificada por SyncRun e promover apenas a execução completa.

Incrementais devem revisitar registros recentes e pendentes, além de recuperar novos lançamentos, para refletir efetivação ou estorno. A janela e o cursor serão fechados após validar a API; não presumir que apenas a data da última importação identifica mudanças.

Proposta operacional: intervalo mínimo manual de cinco minutos por conexão, configurável, inclusive após tentativa que falhou, com informação de próxima tentativa. Respeitar Retry-After e limites do provedor; repetir automaticamente apenas falhas transitórias de operações seguras, com poucas tentativas e espera limitada. Falhas de autorização exigem intervenção, sem repetição automática contínua.

O sucesso registra cobertura e horário da importação. A atualização na origem é outro campo; importar dados idênticos não significa coleta mais recente no banco. Dados anteriores continuam legíveis durante a execução. Se o histórico solicitado exceder o disponível, informar a cobertura efetiva em vez de fabricar meses sem registros como gastos iguais a zero.

## 13. Cálculos e interface

Os cálculos financeiros e os agregados do dashboard são responsabilidade do backend, para usar as mesmas regras na listagem e nos totais. Chart.js apenas representa os resultados retornados.

Aplicar as regras do PRD: distinguir compras efetivadas, estornos, pagamentos de fatura, encargos, pendências e parcelas futuras. Não usar um único sinal numérico como prova de natureza. O dashboard apresenta gastos do cartão em BRL; não inventa receitas ou saldo bancário.

Para o simulador P1, calcular no backend com a fórmula do PRD e retornar série por período, total aportado e valor projetado. O Angular pode aplicar debounce nas alterações de campos e cancelar leituras substituídas. Cenários simulados não alteram a carteira manual e não precisam de persistência no MVP.

Rotas Angular propostas: `/login`, `/dashboard`, `/card`, `/transactions`, `/investments` e `/simulator`. Guards organizam a navegação, mas a autorização real permanece na API. Rotas P1 somente aparecem quando implementadas; não deixar botões que aparentem funcionalidades concluídas.

## 14. Testes e verificação

Confirmado: MSTest para o backend e Chart.js para os gráficos. O pacote/runner exato será fixado conforme compatibilidade com .NET 10 e ambiente de CI.

Plano técnico proposto:

- Testes unitários das regras de natureza/situação, estornos, critérios de inclusão nos totais e projeção com juros e taxa zero.
- Testes de integração com PostgreSQL descartável, isolado de Neon e dados reais, para migrations, unicidade, duas importações idênticas, atualização de pendentes, preservação de anotações e falha transacional.
- Testes HTTP de autenticação: token ausente, assinatura inválida, destinatário incorreto, expiração, logout, redefinição de senha e acesso a recurso de outro titular.
- Provedor Pluggy simulado nos testes automáticos, com paginação, falha na segunda página, autorização expirada, resposta vazia e repetição de registro. A prova de conceito real é uma validação manual separada e não requisito para todo build.
- Testes do Angular nos fluxos críticos: login, perda/expiração da sessão, filtros e total, edição de anotação, carregamento e falha da sincronização. Runner escolhido após criar o projeto com a versão confirmada do CLI.
- Verificação manual de layout responsivo, teclado, feedback de erro, resumo acessível dos gráficos e retomada da hospedagem após inatividade.

Não adotar meta arbitrária de cobertura percentual. Os checks obrigatórios devem falhar para regressões nas regras acima, além de exigir builds de backend e frontend. Antes da apresentação, conferir a versão publicada e ter dados fictícios separados para demonstrar sem expor as finanças do autor.

## 15. Categorias iniciais e pessoais

Decisão confirmada: entregar categorias iniciais e permitir criar novas. Renomear, arquivar e excluir categorias não foram solicitados para este MVP e ficam para evolução posterior.

Categorias iniciais: Alimentação, Transporte, Compras, Lazer, Saúde, Educação, Outros e Sem categoria. Provisionar de forma idempotente, com identificadores estáveis; reiniciar ou atualizar o sistema não cria cópias.

Regras técnicas propostas:

- `POST /api/categories` recebe somente o nome. O backend define o titular pela sessão autenticada; o cliente não pode criar categoria de sistema ou escolher outro usuário.
- Nome obrigatório, após remoção de espaços nas extremidades, com limite de 60 caracteres. Uma chave normalizada deve unificar diferenças de caixa e espaços redundantes; definir uma política consistente de acentos e aplicá-la no banco e nas validações.
- Impedir duplicidade entre categorias pessoais do mesmo usuário e entre uma categoria pessoal e um nome inicial. Usar restrição de unicidade para as categorias pessoais e validação dos nomes iniciais, com tratamento do conflito concorrente.
- Listar categorias de sistema e categorias do titular em ordem previsível. Nunca permitir associar uma compra a categoria pessoal de outro usuário.
- Após criar, atualizar o seletor sem recarregar toda a aplicação. A nova categoria pode ser aplicada às compras existentes e utilizada nos filtros e no dashboard.
- Criar uma categoria não reclassifica automaticamente compras anteriores. A anotação manual sempre prevalece sobre classificação externa.
- A ausência de escolha manual deve ter uma representação única e documentada: proposta de `CategoryId` nulo na anotação, resolvido como Sem categoria para apresentação, enquanto não houver classificação externa confiável.
- Observação pessoal: texto simples de até 500 caracteres; não interpretar como HTML. Valor vazio pode limpar a observação sem remover a categoria.

Adicionar verificações de criação, nome vazio, duplicidade, propriedade, preservação após sincronização e inclusão nos filtros/agregados aos testes críticos.
