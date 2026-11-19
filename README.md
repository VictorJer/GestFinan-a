# Finora

**Gestão financeira pessoal com foco nas transações do cartão de crédito.**

Projeto desenvolvido como proposta de Trabalho de Conclusão de Curso (TCC), com desenvolvimento individual. O Finora pretende reunir dados do cartão Nubank em um dashboard que permita consultar compras, organizar categorias e compreender os gastos por período.

Como complementos, o projeto prevê uma carteira de investimentos com cadastro manual e um simulador de evolução patrimonial com aportes.

> **Status:** ideia aprovada pelo professor e protótipo navegável disponível. FNR-001/002/003 concluídas nos respectivos escopos conforme o backlog. Base backend FNR-003 validada localmente, revisada, aceita pelo autor e integrada pelo [PR #8](https://github.com/VictorJer/GestFinan-a/pull/8), merge `2bd63ae`. FNR-004 foi aceita e integrada pelo [PR #10](https://github.com/VictorJer/GestFinan-a/pull/10), com prova real limitada a uma página (321 transações). FNR-005 está em validação parcial: incremento técnico e três coletas reais limitadas a uma página cada; a matriz não reconhece compras e a competência permanece semânticamente não confirmada, portanto o gate financeiro está bloqueado. Ainda não há importação persistente, dashboard ou publicação desta funcionalidade; a tarefa 5.3 e o aceite final continuam pendentes.

## Objetivo

Desenvolver uma aplicação web que consulte dados de cartão de crédito por meio de um provedor de integração bancária, preserve o histórico e transforme as transações em informações úteis para o controle financeiro pessoal.

**Pergunta orientadora:** como uma aplicação web pode apoiar o controle financeiro pessoal ao reunir dados de cartão de crédito, indicadores de gastos e recursos de planejamento em uma única interface?

## Situação atual

| Entrega | Situação |
|---|---|
| Aprovação da ideia | Concluída, conforme confirmação do autor |
| Protótipo com dados fictícios | Disponível |
| PRD v0 | Revisão 0.2 aprovada documentalmente |
| TDD v0 | Revisões 0.5 e 0.6 aprovadas documentalmente; 0.6 registra DT-49/MediatR 12.5.0 |
| Base backend FNR-003 | Restore com --locked-mode/build e 20 testes aprovados localmente; Swagger verificado no navegador; revisão e aceite explícitos; PR #8 integrado. Sem reviews formais/checks reportados no GitHub; evidências e limites no backlog e guia. |
| Conexão no painel do provedor | Cartão e transações visualizados pelo autor |
| Consulta local pelo backend .NET (FNR-004) | Concluída no escopo limitado, com prova real de uma página; não comprova importação completa |
| Contrato e normalização Pluggy (FNR-005) | Em validação; análise limitada documentada; regra positiva de compra e competência bloqueadas por evidência insuficiente |
| Login JWT e sessões persistentes (FNR-009) | Implementação aceita pelo titular e [PR #20](https://github.com/VictorJer/GestFinan-a/pull/20) integrado; 225 testes sem PostgreSQL e 64 no PostgreSQL descartável aprovados localmente. Revogação completa FNR-010, FNR-011/020 e publicação permanecem pendentes; publicação autenticada bloqueada até FNR-010 e demais gates. |
| Aplicação integrada e publicação definitiva | Pendentes |

A visualização no painel da Pluggy é evidência preliminar de acesso aos dados. A leitura local pela API foi demonstrada em FNR-004; FNR-005 observou campos e agregados em três coletas autorizadas de uma página cada, mas não valida histórico completo, regra positiva de compra, competência por instituição ou comportamento de sincronização.

A prova de conceito local de consulta real foi concluída em FNR-004 sem exposição pública. Validar o contrato financeiro conservador em FNR-005 ainda é um gate anterior à persistência, dashboard e aplicação hospedada.

## Documentação e fluxo de trabalho

- [PRD](docs/PRD.md): requisitos e critérios de aceite do produto.
- [TDD](docs/TDD.md): decisões técnicas, propostas e validações pendentes.
- [Backlog](docs/BACKLOG.md): ordem de execução e evidências por tarefa.
- [Ambiente de desenvolvimento](docs/AMBIENTE.md): versões fixadas, inventário e gates de validação por mudança.
- [FNR-001 no OpenSpec](openspec/changes/archive/2026-11-13-fnr-001-alinhar-documentacao/proposal.md) e [desenho/SDD](openspec/changes/archive/2026-11-13-fnr-001-alinhar-documentacao/design.md): aprovados e concluídos no escopo documental; mudança arquivada em 13/11/2026 por solicitação do autor, com 11/11 tarefas concluídas e sem delta specs. Evidências e limites da verificação preservados nas tarefas e no backlog; FNR-002 não iniciada.

Uma mudança por vez: backlog → feature branch → especificação OpenSpec e revisão → implementação → testes → PR → integração na `main` → deploy após checks. Não desenvolver nem enviar alterações diretamente à `main`; artefatos prontos não significam aprovação.

## Executar o backend local

Requer SDK .NET `10.0.402`. Da raiz do repositório:

```powershell
# Restaura somente o grafo aceito nos locks versionados.
dotnet restore backend/Finora.slnx --locked-mode
# Compila a solução sem novo restore.
dotnet build backend/Finora.slnx --no-restore
# Executa a suíte sem PostgreSQL pelo runner MTP selecionado em global.json.
dotnet test --solution backend/Finora.slnx --no-build --filter 'FullyQualifiedName!~PostgreSql'
# Inicia em Development e loopback, sem banco ou segredos.
dotnet run --project backend/Finora.Api --launch-profile http --no-build --no-restore
```

Acesse `http://127.0.0.1:5080/health`: HTTP 200 e `{"status":"ok"}`. É apenas **liveness**: não valida banco, Pluggy, autenticação ou dados reais. Não existem endpoints financeiros nesta base.

Para testar pelo navegador, abra `http://127.0.0.1:5080/swagger`, expanda `GET /health`, clique em **Try it out** e **Execute**. Confira código 200 e corpo `{"status":"ok"}`. O documento fica em `/openapi/v1.json`; documento, UI e assets locais estão disponíveis **somente em Development** (404 em Production/Staging). O documento descreve somente health e login; não há rota financeira ou cadastro público. Encerre com `Ctrl+C`.

### Login e sessões locais (FNR-009)

`POST /api/auth/login` recebe JSON com `email` e `password`: até 256/1024 caracteres, corpo de até 16 KiB. Identity verifica a senha exata, normaliza somente o e-mail e bloqueia novos logins por cinco minutos após cinco falhas. Sucesso confirma uma nova `AuthSessions` antes de entregar JWT HS256 de 30 minutos, sem cookie/refresh; o token completo não é persistido. Toda resposta de login usa no-store. Conta desconhecida/senha incorreta/bloqueio retornam 401 genérico; falha operacional retorna 503, não senha incorreta. A sessão é consultada em cada chamada protegida; a rota de prova existe somente no projeto de testes.

Configure externamente `ConnectionStrings__Finora`, `Authentication__Jwt__Issuer`, `Authentication__Jwt__Audience` e `Authentication__Jwt__SigningKeyBase64`, sem argumentos ou arquivos versionados. A chave codifica pelo menos 32 bytes aleatórios e precisa ser estável por ambiente/reinício; rotação coordenada invalida tokens anteriores. Sem configuração válida, login/proteção retornam 503 e health continua independente. A migration `20261118173427_AddAuthenticationSessions` precisa ser aplicada explicitamente pelo processo do [guia de ambiente](docs/AMBIENTE.md); nunca é executada no startup.

**Não publicar autenticação antes de FNR-010/011 e demais gates.** Logout/reset revogatório e frontend FNR-020 não estão entregues por FNR-009; atualizar security stamp ou ler uma revogação SQL de fixture não substitui essa entrega. Contrato, configuração, erros, chaves e evidências estão em [AMBIENTE.md](docs/AMBIENTE.md). Para a suíte PostgreSQL, primeiro preparar somente o Compose descartável e a variável dedicada `FINORA_TEST_CONNECTION_STRING` conforme a barreira documentada; depois executar:

```powershell
# A fixture recusa banco pessoal/Neon e aplica migrations somente no database descartável allowlisted.
dotnet test --solution backend/Finora.slnx --no-build --filter 'FullyQualifiedName~PostgreSql'
```

### Consulta local Pluggy (em validação)

O comando explícito `pluggy-read` executa sem iniciar listener HTTP. Para executá-lo após configurar os segredos localmente, use:

```powershell
dotnet run --project backend/Finora.Api --no-build --no-restore --no-launch-profile -- pluggy-read
```

Configure `Pluggy:ClientId`, `Pluggy:ClientSecret` e `Pluggy:ItemId` em User Secrets do projeto `backend/Finora.Api` ou em variáveis de ambiente protegidas; `Pluggy:AccountId` é opcional e serve para selecionar um cartão quando houver mais de um. Não passe credenciais ou IDs como argumentos do comando, não os cole em logs/saídas e não os adicione a arquivos versionados. As chaves de configuração são descritas em [AMBIENTE.md](docs/AMBIENTE.md#início-e-aprovação-do-sdd--fnr-004--15112026) e os segredos de desenvolvimento devem permanecer fora do repositório.

Para analisar a cobertura disponível, sem iniciar listener HTTP, usar explicitamente `pluggy-analyze` com a mesma configuração protegida:

```powershell
dotnet run --project backend/Finora.Api --no-build --no-restore --no-launch-profile -- pluggy-analyze
```

A análise consulta desde a data de calendário de 12 meses antes da referência UTC, não envia `dateTo` para preservar registros futuros e percorre no máximo 100 páginas, com 30 segundos por chamada e 120 segundos totais, sem retry automático. A saída contém somente contagens agregadas, duração, meses distintos com registros e disponibilidade de campos; não comprova 12 meses de histórico e não valida regras financeiras sem evidência. Consulte [PLUGGY-CONTRACT.md](docs/PLUGGY-CONTRACT.md) para filtros, limites e limitações. O código 0 indica apenas coleta terminada com registros; amostra vazia retorna 3, falhas operacionais/incompletas 1, configuração/argumentos 2, conexão bloqueada 4 e cancelamento 130.

O comando consulta a conexão, seleciona uma conta de cartão e lê **somente a primeira página** de `/v2/transactions`. A saída resume etapa, quantidade e existência de continuação; cursor e dados de compra não são exibidos. Uma resposta vazia é inconclusiva, e uma página com transações não significa importação completa, persistência no Finora ou atualização forçada na instituição. Cada chamada tem timeout de 30 segundos; o fluxo inteiro, 120 segundos; não há retries. Códigos atuais: `0` leitura com lançamentos; `1` falha operacional/timeout; `2` configuração/argumentos inválidos ou conta não selecionável; `3` leitura sem lançamentos (inconclusiva); `4` conexão bloqueada; `130` cancelamento. Logs padrão do `HttpClientFactory` estão desativados para o cliente Pluggy. A prova limitada da FNR-004 e a evidência agregada da FNR-005 estão registradas em `docs/AMBIENTE.md`; a segunda não confirmou regra positiva de compra nem competência. A revisão final e o aceite da FNR-005 continuam pendentes.

Configuração versionada contém apenas opções não sensíveis. Detalhes de JSON, locks, portas e evidências no [guia de ambiente](docs/AMBIENTE.md#backend-fnr-003--versões-verificadas-em-14112026). Banco, Docker e credenciais de provedor não são pré-requisitos para estes comandos.

### Provisionamento administrativo local (FNR-008)

Os comandos `user-create` e `user-reset-password` usam o processo da API sem iniciar listener HTTP, autenticação JWT ou cliente Pluggy. Execute um comando por vez, sem argumentos adicionais e sem perfil de lançamento:

```powershell
# Development precisa ser escolhido explicitamente para usar User Secrets locais.
$env:DOTNET_ENVIRONMENT = 'Development'
# Configure ConnectionStrings:Finora fora do repositório; não inclua o valor neste comando.
dotnet run --project backend/Finora.Api --no-launch-profile -- user-create

# O reset solicita o titular e a nova senha com confirmação, no mesmo terminal protegido.
dotnet run --project backend/Finora.Api --no-launch-profile -- user-reset-password
```

Configure `ConnectionStrings:Finora` em User Secrets somente para `Development` explicitamente escolhido ou por configuração externa protegida nos demais ambientes. Os comandos não aceitam e-mail, senha, connection string ou opções como argumentos; não usam pipe, arquivo ou `.env`. Digite e-mail, senha e confirmação apenas nos prompts: as teclas são interceptadas, sem eco; Enter finaliza o campo, Backspace remove o caractere anterior e Ctrl+C cancela. EOF, stdin/stdout redirecionados, terminal sem leitura protegida e confirmação divergente são recusados sem fallback com eco. Use a migration FNR-007 aplicada pelo processo documentado em [AMBIENTE.md](docs/AMBIENTE.md); os comandos não criam nem alteram o schema.

Códigos de saída administrativos:

| Código | Significado |
|---|---|
| `0` | Titular criado (`created`), já existente sem alteração (`already_exists`) ou senha redefinida (`password_reset`). |
| `1` | Falha operacional, incluindo banco/schema indisponível ou resultado de persistência não confirmado. |
| `2` | Argumentos/configuração/entrada inválidos, política de senha não atendida, confirmação divergente ou terminal interativo indisponível. |
| `3` | Nenhum titular encontrado para redefinição. |
| `4` | Conflito, como outro titular, múltiplos usuários ou concorrência. |
| `130` | Cancelamento com Ctrl+C antes da confirmação da persistência. |

O reset não envia e-mail, não desbloqueia a conta e não revoga JWT/sessões existentes. Seu resultado inclui `session_revocation=not_implemented`; FNR-010 continua sendo gate obrigatório antes da publicação autenticada, e a FNR-008 isolada não representa atendimento integral do RF14. A verificação manual em TTY real da tarefa 4.4 foi concluída; evidências combinadas e limitações estão registradas no [guia de ambiente](docs/AMBIENTE.md). O roteiro usa somente o Compose PostgreSQL descartável local.

## Funcionalidades planejadas

### Núcleo do MVP

- Login por e-mail e senha para um único usuário, sem cadastro público.
- Consulta das transações do cartão por integração com Meu Pluggy / API da Pluggy.
- Importação inicial de até 12 meses anteriores, limitada ao histórico disponibilizado pelo provedor.
- Persistência e atualização das compras sem duplicações.
- Atualização ao entrar quando a última importação completa tiver mais de 24 horas, além de acionamento manual.
- Busca e filtros por período, categoria e situação.
- Categorias iniciais e criação de novas categorias pessoais.
- Edição de categoria e observação, preservando os dados originais da compra.
- Dashboard com gastos efetivados, distribuição por categoria e evolução mensal.
- Exibição da situação da conexão e dos horários de atualização.

### Complementos após o núcleo

- Carteira de investimentos cadastrada manualmente.
- Simulador com valor inicial, aporte mensal, taxa anual e prazo.
- Exportação das transações filtradas em CSV.

Esses complementos dependem da estabilidade do núcleo e do prazo disponível. Compra de ativos, pagamentos, recomendações de investimento e atendimento a múltiplos usuários ficam fora da primeira entrega.

## Tecnologias definidas

| Área | Escolha |
|---|---|
| Backend | ASP.NET Core Web API, .NET 10 e Controllers |
| Frontend | Angular 22, conforme ambiente informado pelo autor; Node 24 |
| Interface | HTML e Tailwind CSS |
| Estado no frontend | Serviços com Signals e HttpClient/RxJS |
| Gráficos | Chart.js |
| Persistência | Entity Framework Core 10 com migrations |
| Banco | PostgreSQL |
| Autenticação | ASP.NET Core Identity e JWT |
| Testes do backend | MSTest |
| Desenvolvimento do banco | PostgreSQL local em Docker |
| Hospedagem planejada | Angular no Cloudflare Pages, API no Render e banco na Neon |

As versões exatas dos pacotes e sua compatibilidade serão verificadas no início da implementação. SQL Server e Angular Material foram substituídos pelas escolhas acima. O mapeamento entre entidades e DTOs será explícito em C#, sem AutoMapper no MVP.

## Arquitetura

Monólito modular com frontend e backend publicados separadamente, um único banco e um único repositório Git.

```text
Navegador
  ├── HTTPS → Cloudflare Pages → Angular
  └── HTTPS → Render → API ASP.NET Core
                         ├── PostgreSQL na Neon
                         └── API da Pluggy
```

O dashboard consulta os dados persistidos pelo backend. Abrir telas ou alterar filtros não dispara uma nova importação bancária.

### Organização prevista do código

```text
finora/
├── backend/
│   ├── Finora.Api/              # Controllers, autenticação e configuração
│   ├── Finora.Application/      # Casos de uso, DTOs e interfaces
│   ├── Finora.Domain/           # Entidades e regras de negócio
│   ├── Finora.Infrastructure/   # EF Core, PostgreSQL e cliente Pluggy
│   └── tests/                  # Testes com MSTest
├── frontend/                   # Angular e Tailwind CSS
├── docs/                       # PRD, TDD e backlog
└── openspec/                   # Mudanças e especificações
```
