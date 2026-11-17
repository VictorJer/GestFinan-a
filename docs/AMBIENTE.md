# Ambiente de desenvolvimento

Este guia registra a baseline aprovada para preparar o ambiente e diferencia ferramentas disponíveis de projetos instalados e validações executadas. Não cria nem substitui os manifestos dos projetos backend/frontend, que serão introduzidos nas respectivas mudanças.

## SDK .NET e Node.js

O `global.json` fixa o SDK .NET 10.0.402, sem prerelease e sem roll-forward. O arquivo contém JSON estrito; comentários nele invalidariam o formato. `dotnet --version` deve retornar exatamente `10.0.402` ao executar no repositório.

O `.node-version` registra Node.js 24.22.0, mas não troca a versão automaticamente. Se necessário, selecione essa versão manualmente com o gerenciador de versões já utilizado na máquina e confirme com `node --version`. A baseline npm é 11.19.1, conferida com `npm --version`. Não instale nem atualize ferramentas globais automaticamente por este guia.

As versões observadas nesta execução devem ser registradas abaixo após a conferência. A existência de uma versão no ambiente não comprova build ou testes de projetos ainda inexistentes.

## Ferramenta local do Entity Framework

O manifesto `.config/dotnet-tools.json` fixa `dotnet-ef` 10.0.15 para este repositório. Restaure as ferramentas locais com `dotnet tool restore` e invoque explicitamente a versão local com `dotnet tool run dotnet-ef -- --version`. Não use `dotnet-ef` global como evidência da versão local: a instalação global observada durante a exploração era 10.0.13. A validação não cria projeto nem migration; EF Core/Npgsql/PostgreSQL será validado em FNR-006.

## Comandos de inventário

Execute os comandos abaixo na raiz para consultar o ambiente sem criar projeto, container ou migration:

```powershell
dotnet --version
node --version
npm --version
dotnet tool run dotnet-ef -- --version
docker context show
docker context ls
docker version
docker info
docker compose version
```

Os comandos Docker consultam cliente, contexto, servidor e Compose; não executam `pull`, `run` ou `compose up`. A disponibilidade do daemon deve ser confirmada pela saída do servidor/`docker info`, não inferida pela instalação do cliente.

### Evidência Docker — 14/11/2026

Consultas executadas: `docker context show`, `docker context ls`, `docker version`, `docker info` e `docker compose version`. Resultado sanitizado: cliente Docker 29.9.1 disponível; contexto selecionado `desktop-linux`; contexto `default` também listado; servidor Linux inacessível, pois o endpoint local `dockerDesktopLinuxEngine` não foi encontrado; `docker info` não obteve resposta do daemon; Docker Compose 5.6.0 disponível. Nenhum container foi criado, imagem baixada ou configuração alterada. Cliente/Compose disponíveis não satisfazem o gate de engine Linux.

Na consulta de estado, os processos do Docker Desktop não estavam ativos e a distribuição WSL `docker-desktop` aparecia parada. A pedido, o autor iniciou o Desktop manualmente. Na repetição das consultas, os processos estavam ativos, `docker-desktop` estava `Running` (WSL 2), `docker version` confirmou servidor Linux/amd64 no Docker Desktop 4.96.0 com engine 29.9.1, `docker info` respondeu e Compose permaneceu em 5.6.0. O gate de engine/Compose foi então confirmado; nenhuma reparação ou alteração sistêmica foi executada pelo agente.

## Matriz aprovada e validações adiadas

As versões abaixo foram aprovadas pelo autor em 14/11/2026 e consultadas nos metadados públicos npm/NuGet na mesma data. “Metadados disponíveis” significa que a versão consta no registro e seus contratos declarados foram consultados; não significa pacote instalado, projeto restaurado ou teste executado.

| Área/pacote | Versão aprovada | Registro/compatibilidade consultada | Estado e validação atribuída |
|---|---:|---|---|
| .NET SDK | 10.0.402 | `global.json`, roll-forward desabilitado | Selecionado localmente; conferir em clones com `dotnet --version`. |
| Node.js / npm | 24.22.0 / 11.19.1 | `.node-version`; npm informado pela execução local | Ambos conferidos neste ambiente. `.node-version` não seleciona Node automaticamente. |
| dotnet-ef | 10.0.15 | NuGet; manifesto local | Restore e invocação local conferidos; distinto do EF global 10.0.13 observado na exploração. |
| EF Core / MSTest.Sdk | 10.0.15 / 4.5.3 | NuGet; MSTest.Sdk com Microsoft.Testing.Platform | Metadados disponíveis; pacotes e testes reais pertencem a FNR-003. |
| Npgsql EF | 10.0.3 | NuGet; dependência declarada aceita EF Core `>=10.0.4 <11.0.0` | Metadados disponíveis; restore integrado e migration/PostgreSQL pertencem a FNR-006. |
| Angular CLI / build | 22.2.7 | npm; build aceita TypeScript `>=6.0 <6.1`, Vitest `^4.0.8 || ^5.0.0` e Tailwind 4 | Metadados disponíveis; instalação e build do workspace pertencem a FNR-019. |
| Angular framework/compiler | 22.2.6 | npm; compiler-cli aceita TypeScript `>=6.0 <6.1` | Metadados disponíveis; instalar os pacotes alinhados no workspace FNR-019. |
| TypeScript | 6.0.3 | npm; atende à faixa declarada pelo Angular 22.2.7 | Metadados disponíveis; `latest` 7.x não foi escolhido por estar fora da faixa declarada. |
| Vitest | 5.1.0 | npm; faixa declarada pelo Angular build 22.2.7 | Metadados disponíveis; teste executável e configuração pertencem a FNR-019. |
| Tailwind CSS / Chart.js | 4.3.5 / 4.6.1 | npm | Metadados disponíveis; instalação/integração no frontend pertencem às mudanças de frontend, começando em FNR-019. |

Fontes consultadas em 14/11/2026: [npm Registry](https://registry.npmjs.org/) para versões e metadados de compatibilidade; [NuGet flat container](https://api.nuget.org/v3-flatcontainer/) para MSTest.Sdk, Microsoft.EntityFrameworkCore e Npgsql.EntityFrameworkCore.PostgreSQL. Os requisitos `engines` consultados para Angular 22.2.7 e Vitest 5.1.0 incluem Node 24; os peers de Angular build/compiler-cli confirmam TypeScript 6.0.x, e Angular build declara as faixas de Vitest e Tailwind acima. Isso não substitui restore/build/test com o grafo transitivo real.

### Atualizações e responsabilidade dos gates

Não trocar versões por `latest` nem atualizar silenciosamente. Uma atualização exige revisar a compatibilidade declarada e a baseline no TDD/guia, registrar a decisão e sua fonte/data, alterar os arquivos de fixação do projeto correspondente e executar os restores, builds e testes daquele item. FNR-003 é responsável por lock NuGet, restore bloqueado e testes backend; FNR-019 por `package-lock.json`, `npm ci`, build e Vitest; FNR-006 por compatibilidade EF/Npgsql/PostgreSQL e migrations. FNR-002 não cria manifestos de aplicação nem locks sem dependências reais.

## Validação local desta mudança — 14/11/2026

- `dotnet --version`, `node --version`, `npm --version`: `10.0.402`, `v24.22.0` e `11.19.1`.
- `dotnet tool restore` e `dotnet tool run dotnet-ef -- --version`: restore bem-sucedido; ferramenta local `10.0.15`.
- JSON: `global.json` e `.config/dotnet-tools.json` analisados com sucesso.
- `git diff --check`: sem erros de whitespace; Git reportou somente avisos informativos de conversão LF/CRLF em arquivos Markdown.
- Links Markdown locais em README e documentos alterados: destinos existentes, após atualizar dois links históricos do TDD para os artefatos arquivados equivalentes.
- `openspec validate fnr-002-preparar-ambiente --strict`: válido; `skip_specs` aceito, sem deltas de comportamento.
- Inventário/diff: nenhuma aplicação, solução, projeto backend/frontend, `package.json`, lock de aplicação, migration, banco, container, segredo, CI ou publicação foi introduzido. O manifesto local de ferramenta .NET é o único manifesto de ferramenta adicionado.
- Docker: engine Linux e Compose acessíveis após o autor iniciar manualmente o Desktop, conforme evidência acima.

Estas verificações são locais. Checks/revisões do GitHub não foram consultados; build/teste de projetos continuam atribuídos a FNR-003, FNR-006 e FNR-019.

## Backend FNR-003 — versões verificadas em 14/11/2026

SDK selecionado: `10.0.402`. Os nuspec oficiais em `https://api.nuget.org/v3-flatcontainer/<id>/<versão>/<id>.nuspec` responderam HTTP 200 para Microsoft.AspNetCore.OpenApi e Microsoft.AspNetCore.Mvc.Testing `10.0.15`, Swashbuckle.AspNetCore.SwaggerUI `10.2.3` e MSTest.Sdk `4.5.3`. Os três primeiros declaram `net10.0`; o grafo foi confirmado por restore real dos cinco projetos e build sem restore (zero avisos/erros). OpenAPI resolveu Microsoft.OpenApi `2.13.0`; MSTest resolveu framework/adapter `4.5.3` e Microsoft.Testing.Platform `2.5.3`. Não foram adicionados EF Core, Npgsql ou cliente Pluggy.

`global.json` continua com o SDK original e acrescenta somente `test.runner = Microsoft.Testing.Platform`. O JSON estrito não aceita comentários. O SDK de testes fica fixado explicitamente em `MSTest.Sdk/4.5.3` no projeto; o lock de PackageReference não fixa o resolvedor de SDK e não substitui esse pin. As propriedades comuns e a política de locks estão em `backend/Directory.Build.props`, com comentários XML. As bibliotecas de fronteira permanecem sem classes enquanto não houver casos de uso reais.

### Configuração e execução local

`backend/Finora.Api/appsettings.json` é o exemplo executável não sensível: define somente níveis de log e nomes de host locais aceitos. Não contém strings de conexão, credenciais ou JWT. `Properties/launchSettings.json` fixa o perfil `http` em Development e `http://127.0.0.1:5080`, sem abrir navegador automaticamente. Ambos são JSON estrito; suas finalidades são explicadas aqui em vez de inserir comentários inválidos. O perfil é local, não configuração de hospedagem.

Depois do build, execute `dotnet run --project backend/Finora.Api --launch-profile http --no-build --no-restore`. Verifique `/health` no endereço acima e encerre com `Ctrl+C`. Para experimentar via Swagger, abra `/swagger`, expanda `GET /health`, selecione **Try it out** e **Execute**: espere HTTP 200 e `{"status":"ok"}`. Documento em `/openapi/v1.json`, UI e assets são locais e existem somente em Development. Fora dele, as rotas de documentação retornam 404; `/health` permanece disponível. A porta deve estar livre; não altere o binding para rede pública por conveniência.

O diagnóstico mede somente liveness do processo. Não comprova banco, autenticação, autorização ou Pluggy. Não há botão Authorize, endpoints financeiros ou segredos necessários. Configurações sensíveis futuras devem usar User Secrets no desenvolvimento ou variáveis de ambiente protegidas, nunca valores versionados neste exemplo. Os ignores existentes já excluem `.env`, `secrets.json`, `bin`, `obj` e TestResults; locks são gerados pelo NuGet e não recebem comentários manuais.

### PostgreSQL local — FNR-006

O `compose.yaml` executa PostgreSQL 18 para desenvolvimento, publica a porta somente em `127.0.0.1` e mantém os dados no volume nomeado `finora-dev_finora-dev-data`. O `compose.test.yaml` usa projeto Compose e porta separados, sem volume persistente: os dados de teste ficam em `tmpfs` e desaparecem quando o container descartável é removido. Nenhuma configuração usa a Neon.

Na primeira execução, copie `.env.example` para `.env` e substitua as duas senhas de exemplo por valores locais fortes e diferentes. `.env` é ignorado pelo Git; não versione, registre em logs nem compartilhe credenciais. `.env.example` contém somente marcadores, nunca senhas utilizáveis. Verifique o ignore local com `git check-ignore -v .env`.

Comandos PowerShell a partir da raiz do repositório:

```powershell
# Cria .env somente quando ainda não existe; se existir, preserve e atualize manualmente.
if (-not (Test-Path -LiteralPath '.env')) { Copy-Item .env.example .env }
# Substitua os marcadores das duas senhas no .env por valores fortes e diferentes antes de subir serviços.

# Valida e inicia o banco persistente de desenvolvimento, aguardando o healthcheck.
docker compose --env-file .env -f compose.yaml config --quiet
docker compose --env-file .env -f compose.yaml up -d --wait

# Inspeciona prontidão e encerra somente o projeto de desenvolvimento sem remover seu volume.
docker compose --env-file .env -f compose.yaml ps
docker compose --env-file .env -f compose.yaml exec -T postgres pg_isready -U finora -d finora_local
docker compose --env-file .env -f compose.yaml stop
docker compose --env-file .env -f compose.yaml down

# Valida e inicia o projeto descartável explicitamente nomeado para testes de integração.
docker compose --env-file .env --project-name finora-fnr006-test -f compose.test.yaml config --quiet
docker compose --env-file .env --project-name finora-fnr006-test -f compose.test.yaml up -d --wait

# Lê somente as chaves de teste do .env local sem imprimir seus valores.
$testSettings = @{}
foreach ($line in Get-Content -LiteralPath '.env') {
    if ($line -match '^\s*([A-Za-z_][A-Za-z0-9_]*)\s*=\s*(.*?)\s*$') {
        $key = $matches[1]
        $value = $matches[2].Trim()
        if ($value.Length -ge 2 -and (($value[0] -eq '"' -and $value[$value.Length - 1] -eq '"') -or ($value[0] -eq "'" -and $value[$value.Length - 1] -eq "'"))) {
            $value = $value.Substring(1, $value.Length - 2)
        }
        $testSettings[$key] = $value
    }
}

# Recusa configuração de outro database/usuário e garante que a porta publicada é loopback.
if ($testSettings['POSTGRES_TEST_DB'] -cne 'finora_integration_test' -or $testSettings['POSTGRES_TEST_USER'] -cne 'finora_test') {
    throw 'A configuração não corresponde ao database e usuário esperados do serviço descartável.'
}
$testPublishedPort = docker compose --env-file .env --project-name finora-fnr006-test -f compose.test.yaml port postgres 5432
if ($LASTEXITCODE -ne 0 -or $testPublishedPort -notmatch '^127\.0\.0\.1:(\d+)$') {
    throw 'A porta do serviço descartável não está vinculada ao loopback.'
}

# Monta a connection string em memória com credenciais exclusivas do banco descartável.
$testConnection = [System.Data.Common.DbConnectionStringBuilder]::new()
$testConnection['Host'] = '127.0.0.1'
$testConnection['Port'] = [int]$matches[1]
$testConnection['Database'] = $testSettings['POSTGRES_TEST_DB']
$testConnection['Username'] = $testSettings['POSTGRES_TEST_USER']
$testConnection['Password'] = $testSettings['POSTGRES_TEST_PASSWORD']

# A suíte recebe somente a variável dedicada a testes, removida mesmo se o comando falhar.
$env:FINORA_TEST_CONNECTION_STRING = $testConnection.ConnectionString
try {
    dotnet test backend/tests/Finora.Api.Tests/Finora.Api.Tests.csproj
    if ($LASTEXITCODE -ne 0) { throw 'A suíte backend falhou.' }
} finally {
    Remove-Item Env:FINORA_TEST_CONNECTION_STRING -ErrorAction SilentlyContinue
}

# Inspeciona e remove somente o container/rede do projeto de teste; não há volume para preservar.
docker compose --env-file .env --project-name finora-fnr006-test -f compose.test.yaml ps
docker compose --env-file .env --project-name finora-fnr006-test -f compose.test.yaml exec -T postgres pg_isready -U finora_test -d finora_integration_test
docker compose --env-file .env --project-name finora-fnr006-test -f compose.test.yaml down --remove-orphans
```

Os nomes `finora`, `finora_local`, `finora_test` e `finora_integration_test` correspondem aos valores do exemplo; se forem alterados em `.env`, ajuste também os comandos de inspeção. `down` no Compose de desenvolvimento mantém o volume nomeado; não acrescente `--volumes`/`-v`, pois isso apaga os dados locais. Para testes, use sempre o arquivo e o `--project-name` indicados: o banco descartável não compartilha credenciais, porta ou armazenamento persistente com o de desenvolvimento. Não use nenhum deles para dados financeiros reais.

#### Evidências técnicas — FNR-006 — 16/11/2026

A migration `20261116175137_Fnr006PersistenceSmoke` foi gerada em `Finora.Infrastructure` com `Up`/`Down` vazios, sem tabelas de domínio. O comando explícito `dotnet-ef database update`, apontado ao projeto Infrastructure, conectou somente a `finora_integration_test` no loopback do projeto Compose descartável. A consulta confirmou o registro da migration com EF Core 10.0.15 e que a única tabela pública era `__EFMigrationsHistory`; não havia schema de aplicação. A repetição do comando informou que o banco já estava atualizado. A verificação não usou o volume persistente nem a Neon.

Os testes de integração leem somente `FINORA_TEST_CONNECTION_STRING`; não usam fallback de `ConnectionStrings__Finora`. Host não-loopback e database diferente de `finora_integration_test` são rejeitados antes da conexão. Uma compilação inicial dos novos testes falhou por usar `Assert.ThrowsException`, API indisponível na baseline MSTest; foi corrigida para `Assert.Throws` antes da validação final. Em 16/11/2026, restore bloqueado e ambos os `docker compose config --quiet` passaram; o build da solução passou com zero avisos/erros e a suíte backend aprovou 132/132 testes usando o serviço descartável. Com as duas variáveis de conexão ausentes, os testes HTTP de health aprovaram 4/4 e mantiveram HTTP 200 com corpo `{"status":"ok"}`. Não há chamada a `Migrate()`/`MigrateAsync()` nos projetos de produção. `openspec validate fnr-006-postgresql-efcore --strict` passou; `git diff --check` não encontrou erros, apenas avisos informativos LF/CRLF. `.env` permaneceu ignorado e não rastreado, e a inspeção automatizada não encontrou suas senhas nos arquivos alterados/não rastreados.

#### Regularização documental e revalidação — FNR-006 — 17/11/2026

**Registro inicial em 17/11/2026, antes do merge:** a regularização documental foi aprovada; naquele momento o PR #15 estava aberto, sem integração ou encerramento, e a FNR-007 não estava liberada. Ao solicitar a aplicação da FNR-007, o titular aprovou proposta/spec/SDD dessa mudança, escolheu tratar primeiro a dependência FNR-006 e autorizou “Preparar regularização” no harness. Após revisar o conteúdo, aprovou explicitamente estes registros e autorizou commits locais pelo committer e a abertura de PR. O commit `56e9569` foi criado. A autorização inicial não incluía merge, sync, arquivamento ou publicação. TDD 0.8 e aceites locais anteriores são preservados, não transferidos.

**Atualização após o merge:** o [PR #15](https://github.com/VictorJer/GestFinan-a/pull/15), que alterou somente `docs/BACKLOG.md` e este guia, foi integrado em 17/11/2026 pelo merge `d8db9f247be294168286f360910b438570a41188`. A consulta retornou `reviews: []` e `statusCheckRollup: []`; isso não comprova review formal nem checks aprovados. O PR documental não associa retroativamente o commit técnico `c03f0c7275e2725c08201d3f3e246647ed7ab045` a um PR. Em 17/11/2026, o titular aceitou no harness registrar a falta de PR desse commit como ressalva histórica, sem declará-la conforme ao fluxo de PR; a dependência FNR-006 foi considerada resolvida para prosseguir com a revisão do TDD. A mudança OpenSpec FNR-006 foi arquivada localmente em `openspec/changes/archive/2026-11-17-fnr-006-postgresql-efcore` após 11/11 tarefas completas; `specs` foi ignorado e não havia delta specs para sincronizar. O TDD 0.9 e os registros documentais da FNR-007 foram aprovados em 17/11/2026; a execução pode prosseguir na branch feature, sem presumir implementação ou testes ainda não realizados.

**Evidência remota consultada:** `gh api repos/VictorJer/GestFinan-a/branches/main` confirmou a `main` remota no commit [`c03f0c7275e2725c08201d3f3e246647ed7ab045`](https://github.com/VictorJer/GestFinan-a/commit/c03f0c7275e2725c08201d3f3e246647ed7ab045), mensagem `feat(persistence): preparar PostgreSQL local com EF Core`, com um pai (`67a03e2`). A consulta de PRs pela branch `feature/fnr-006-postgresql-efcore` e o endpoint de PRs associados ao commit retornaram listas vazias; os PRs recentes listados chegaram ao #14, sem FNR-006. A consulta do commit registrou data de committer `2026-11-16T21:18:39Z`, que não é evidência do horário de push. Não foi identificado o mecanismo ou responsável por colocar o commit na main. Presença remota é confirmada, mas integração pelo fluxo obrigatório de PR não foi comprovada; não criar PR retroativo como se tivesse precedido o código.

No SHA consultado, o GitHub reportou zero check runs e zero status contexts. O status agregado foi `pending` com `total_count: 0` e `statuses: []`, sem check concreto pendente identificado; isso não significa checks aprovados. A branch reportou `protected: false`; não se infere dessa propriedade ausência de todas as regras aplicáveis. Não há PR/review formal localizado para esta entrega. A revisão assistida abaixo e os testes são locais, posteriores à presença do conteúdo na main, não uma review formal da plataforma nem autorização retroativa da integração.

**Revisão de conteúdo:** conferidos o diff do commit, Compose, exemplo de ambiente, contexto/factory/configuração, migration/snapshot, testes, locks e alterações documentais contra os artefatos FNR-006 e o TDD 0.8. Nenhuma correção de código necessária foi identificada no escopo técnico da FNR-006. O contexto permanece vazio, a migration tem Up/Down vazios e o host HTTP não registra persistência nem chama migrations. Os três locks afetados mantêm EF Core/Relational 10.0.15 e Npgsql EF 10.0.3. A factory lê apenas `ConnectionStrings__Finora`; o teste exige configuração própria, host de loopback e database `finora_integration_test`. A asserção histórica de que somente a migration smoke existe continua válida nesta base; a evolução planejada FNR-007 deverá passar a aplicar alvo explícito antes de adicionar migrations de domínio. A referência oficial de [criação em design-time](https://learn.microsoft.com/ef/core/cli/dbcontext-creation), consultada via Context7 nesta revisão, confirma a descoberta da factory sem depender do host da API.

**Revalidação local desta execução**, sobre o backend idêntico ao commit `c03f0c7`:

- `dotnet restore backend/Finora.slnx --locked-mode`: saída 0, sem alteração dos locks.
- `dotnet build backend/Finora.slnx --no-restore`: saída 0, zero avisos/erros.
- As duas configurações `docker compose ... config --quiet` passaram. Foi iniciado somente o projeto descartável `finora-fnr006-test`, com `compose.test.yaml`, tmpfs e porta em loopback; o volume persistente de desenvolvimento e a Neon não foram utilizados.
- `dotnet test --solution backend/Finora.slnx --no-build`, com `FINORA_TEST_CONNECTION_STRING` montada em memória a partir das chaves de teste de `.env`: **132/132 aprovados, zero falhos/ignorados**. Inclui health, OpenAPI/Swagger, comandos Pluggy simulados e a migration em banco descartável novo; nenhuma coleta externa foi feita.
- Repetição filtrada de `DisposableDatabaseConnectsAndAppliesOnlyTheSmokeMigration`: **1/1 aprovado** no mesmo banco, validando reaplicação sem duplicar estruturas/histórico. Consulta de metadados confirmou somente `public.__EFMigrationsHistory`, com `20261116175137_Fnr006PersistenceSmoke` e ProductVersion `10.0.15`. Uma consulta inicial com identificador SQL entre aspas perdeu as aspas na passagem pelo PowerShell e falhou; a repetição por stdin preservou o identificador e passou, sem alteração de schema por essas consultas.
- Testes filtrados de `Finora.Api.Tests.HealthTests`, removendo temporariamente `FINORA_TEST_CONNECTION_STRING` e `ConnectionStrings__Finora`: **4/4 aprovados**. As variáveis foram restauradas após os comandos. Não foi repetido o ensaio manual de navegador, persistência após reinício do banco de desenvolvimento ou geração de migration; suas evidências de 16/11 continuam históricas.
- `openspec validate fnr-006-postgresql-efcore --strict --json`: aprovado, com `skip_specs: true` e zero delta specs, coerente com o escopo técnico. Os 11/11 checkboxes históricos foram preservados e não comprovam encerramento/integridade do fluxo Git.
- Verificação documental: 10 destinos de links locais existentes nos dois documentos alterados; `git diff --check` aprovado, somente avisos LF/CRLF. Inspeção das duas senhas PostgreSQL locais não encontrou seus valores no diff do commit FNR-006 nem nos registros; `.env` continua ignorado e não rastreado. Essa inspeção é limitada, não auditoria exaustiva. O projeto Compose descartável criado nesta execução foi removido ao final, sem tocar em volumes de desenvolvimento.

Os registros foram preparados em `docs/fnr-006-regularizar-registros`; somente este guia e o backlog foram alterados naquela regularização. Não houve revisão do PRD/TDD, nova especificação de comportamento ou modificação dos artefatos históricos FNR-006. A revisão 0.5 do backlog foi aprovada documentalmente pelo titular em 17/11/2026; o commit `56e9569` foi criado e o PR #15 estava aberto no momento daquele registro. O merge documental posterior, o arquivo FNR-006 e as revisões TDD/backlog posteriores estão descritos acima. A ressalva de ausência de PR para `c03f0c7` permanece explícita. O estado atualizado da FNR-007 e as evidências unitárias ficam no registro a seguir; aprovação documental não antecipa resultados técnicos.

### Modelo, migration e validação local — FNR-007 — 17/11/2026

TDD 0.9 foi aprovado para o modelo relacional; TDD 0.10 fixa a allowlist de SyncRun e TDD 0.11 fixa Identity Version2/128, ambos aprovados em seus escopos pelo titular. Registro de progresso inicial: 11/19 tarefas na branch `feature/fnr-007-modelar-persistencia-nucleo`, antes das validações integradas finais; sem commit ou PR naquele momento.

#### Comandos administrativos da migration FNR-007

O projeto da migration e startup do `dotnet ef` são `Finora.Infrastructure`, que contém a factory design-time e as opções do modelo; assim a API HTTP não é iniciada. A factory exige `ConnectionStrings__Finora` fornecida externamente. A geração e o script SQL abaixo não abriram conexão; `database update` aplica DDL e só pode ser executado depois de validar explicitamente o destino:

```powershell
# Gere um script de revisão da transição FNR-006 para FNR-007 sem aplicar a migration.
dotnet ef migrations script 20261116175137_Fnr006PersistenceSmoke 20261117193137_Fnr007CorePersistence --project backend/Finora.Infrastructure/Finora.Infrastructure.csproj --startup-project backend/Finora.Infrastructure/Finora.Infrastructure.csproj --context FinoraDbContext --output $env:TEMP\Fnr007CorePersistence.sql

# Só depois de confirmar a connection string externa e o destino descartável finora_integration_test.
dotnet ef database update 20261117193137_Fnr007CorePersistence --project backend/Finora.Infrastructure/Finora.Infrastructure.csproj --startup-project backend/Finora.Infrastructure/Finora.Infrastructure.csproj --context FinoraDbContext

# Downgrade destrói todas as tabelas/dados criados pela FNR-007; permitido apenas no banco descartável validado.
dotnet ef database update 20261116175137_Fnr006PersistenceSmoke --project backend/Finora.Infrastructure/Finora.Infrastructure.csproj --startup-project backend/Finora.Infrastructure/Finora.Infrastructure.csproj --context FinoraDbContext
```

Não executar downgrade em banco pessoal, Neon ou produção; para produção, usar somente evolução para frente após aprovação e validação do destino, conforme TDD §9. A aplicação não chama `Database.Migrate()`/`MigrateAsync()` no startup. O ciclo automatizado 4.2 aplicou/resetou DDL somente no Compose descartável descrito abaixo; nenhum DDL foi aplicado ao banco de desenvolvimento ou à Neon. A consulta administrativa anterior `dotnet ef migrations list` tentou buscar status em `127.0.0.1:5432`, mas a conexão foi recusada; aquele comando não determinou estado aplicado.

- Migration incremental gerada: `20261117193137_Fnr007CorePersistence`; `20261116175137_Fnr006PersistenceSmoke` e seu Up/Down vazios foram preservados. O novo snapshot representa as tabelas/constraints/índices Identity-financeiros e `dotnet ef migrations has-pending-model-changes` não apontou diferenças. Up cria as estruturas esperadas; Down remove essas tabelas e seus dados. Script SQL da transição foi revisado no caminho temporário `C:\Users\victo\AppData\Local\Temp\opencode\fnr-007-smoke-to-core.sql`; não foi executado contra banco.
- Tarefa 4.2 validada em PostgreSQL descartável. Antes da conexão, as quatro guardas passaram (ausência da variável, sem fallback para runtime, host remoto e database diferente). Projeto Compose `finora-fnr006-test`, `postgres:18`, porta publicada `127.0.0.1:55433` (desenvolvimento `55432`), tmpfs `/var/lib/postgresql` com limite de 536870912 bytes; inspeção mostrou nenhum volume montado. `FINORA_TEST_CONNECTION_STRING` foi montada em memória apenas com `POSTGRES_TEST_*` do `.env`, sem imprimir credenciais nem usar conexão da API. O filtro das duas classes PostgreSQL passou **6/6**, incluindo schema vazio, smoke explícita, upgrade, reaplicação idempotente e downgrade/reaplicação. A fixture revalidou o destino antes de reset e confirmou o database após abrir conexão, antes do DDL. O processo restaurou a variável ao estado inicial; somente `finora-fnr006-test` foi encerrado via `down --remove-orphans` (sem `--volumes`), e `ps --all` ficou vazio. Nenhum serviço/volume de desenvolvimento foi iniciado, parado ou alterado.

- Os tipos de origem (`Type`, `Status`, `OperationType`, IDs, descrição, valor, moeda, texto de data e metadados opcionais) são preservados separadamente da interpretação técnica (`Nature`, `Situation`, `TransactionDate` e motivos independentes). A referência temporal e a versão da regra acompanham o snapshot. Nenhuma coluna ou teste declara compra elegível ou competência financeira validada.
- Categoria de sistema usa `UserId = null` e `ScopeId = Guid.Empty`; categoria pessoal usa o titular nos dois campos. `NormalizedName` é fornecido pela fronteira, não gerado por política de produção. Anotação representa ausência de escolha com `CategoryId` e `CategoryScopeId` ambos nulos; nota é texto simples até 500 caracteres e sua limpeza mantém a categoria.
- IDs externos permanecem opacos e distinguem caixa/espaços; a camada Domain rejeita acima de 1.024 bytes UTF-8, sem truncar. Isso é um limite de armazenamento/indexação, não um limite contratual confirmado do provedor.
- Fronteiras preservadas: FNR-012 é responsável por reconciliação/reimportação e continuidade externa; FNR-013 por execução, entrega durável e políticas de posse/retry; FNR-016 por catálogo e normalização executável de nomes; FNR-017 por edição de categoria/anotação. Sem política de acentos, lista visual “Sem categoria”, importador, endpoint ou sincronização implementados nesta etapa.
- Dependência validada: `Microsoft.AspNetCore.Identity.EntityFrameworkCore` 10.0.15 está publicado para `net10.0`, licença MIT, com dependências diretas EF Relational e Identity Stores 10.0.15. Grafo transitivo real manteve EF/Extensions no patch 10.0.15; vulnerabilidade scan via NuGet sem achados. Foram atualizados apenas `packages.lock.json` de Infrastructure, Api e testes; Domain/Application não receberam dependências. Restore bloqueado passou; nenhuma autenticação HTTP foi adicionada.
- Identity mínimo: `ApplicationUser` mantém UUID não vazio; `FinoraDbContext` deriva de `IdentityUserContext<ApplicationUser, Guid>` sem roles e chama a configuração-base antes dos mapeamentos financeiros. Opções Version2/128 são compartilhadas pelo design-time/testes, com possibilidade de o runtime fornecer seu próprio service provider configurado pela mesma ação. Teste da factory validou o modelo sem abrir conexão: usuários/claims/logins/tokens, sem roles, passkeys, AuthSession ou seed; Domain/Application seguem sem referências de pacote EF/Identity.
- Integridade relacional (3.3): mapeamentos declarativos aplicam chaves alternativas e FKs compostas para titular/conta externa/escopo; a anotação usa `TransactionId` como PK; CHECKs impedem UUID vazio e incoerência entre categoria pessoal/sistema, inclusive par nulo/preenchido da categoria na anotação. Exclusões de titulares e pais financeiros usam `Restrict`. Testes inspecionam chaves, relações, nulabilidade e SQL das constraints no modelo design-time; isso não substitui testes de inserção PostgreSQL.
- Modelo escalar e índices (3.4): dinheiro usa `numeric(19,4)`; datas de calendário `date`; datas originais/metadados sem conversão `text`; instantes UTC `timestamp with time zone`; enums e motivos como texto estável. Identidades externas usam `text COLLATE C` e CHECK de até 1.024 bytes UTF-8; provider limita-se a 32 caracteres. Unicidades e índices D4/D5 foram configurados; o índice composto convencional da FK começa por `CategoryId` e cobre o filtro sem índice simples redundante, e a unicidade de conexão já cobre o prefixo `UserId`. `SaveChanges`/`SaveChangesAsync` compartilham validação pré-provider de entidades incluídas/alteradas para valores monetários, IDs, motivos e timestamps.
- A configuração de modelo continua centralizada em `FinoraIdentityModelOptions` (`Version2`, 128): factory/testes recebem o provider mínimo de design-time por `FinoraDbContextConfiguration`; a composição futura de FNR-008 deve aplicar `FinoraIdentityModelOptions.Configure` às mesmas opções Identity e fornecer o próprio `IServiceProvider` à configuração do contexto, sem registrar autenticação nesta etapa.
- Integridade/unicidade PostgreSQL (tarefas 4.3–4.4): `PostgreSqlFinancialIntegrityTests` aprovou 1/1 e `PostgreSqlFinancialUniquenessTests` 2/2 no serviço descartável. Foram comprovadas inserções inválidas via EF e SQL parametrizado, escopo/categoria, rejeição do UUID vazio, seis exclusões `RESTRICT` com SQLSTATE 23001, unicidade sequencial/concorrente com SQLSTATE 23505, um vencedor por chave e preservação de IDs externos distintos por caixa/espaços/conta. A verificação final da tarefa 4.2 (6/6) não foi repetida.
- Round-trips e atomicidade (tarefas 5.1–5.4): `PostgreSqlFinancialValueRoundTripTests` 2/2, `PostgreSqlFinancialDateMetadataRoundTripTests` 1/1, `PostgreSqlFinancialAnnotationUpdateTests` 1/1 e `PostgreSqlFinancialTransactionAtomicityTests` 1/1 passaram. Valores `numeric(19,4)`, datas/metadados originais, interpretações conservadoras, atualização de origem/anotação e rollback foram relidos em contextos independentes; a prova de rollback cobre apenas transação relacional curta, não importação, chamadas externas, retries ou sincronização ponta a ponta.
- Validação integrada FNR-007 (6.1): restore `--locked-mode` passou sem alteração de locks; build da solução passou com zero avisos/erros; testes MTP com PostgreSQL descartável aprovaram 166/166, excluindo apenas `PostgreSqlPersistenceTests` e `PostgreSqlMigrationLifecycleTests` para preservar a evidência 4.2 sem repeti-la. As novas classes 4.3–5.4 foram incluídas. Health/OpenAPI/Swagger aprovaram 17/17 com `FINORA_TEST_CONNECTION_STRING` e `ConnectionStrings__Finora` ausentes, incluindo `/health` HTTP 200 e corpo exato `{"status":"ok"}`; testes de comandos Pluggy usaram somente fixtures simuladas. Busca no host `backend/Finora.Api` não encontrou `Database.Migrate`/`MigrateAsync`. O serviço `finora-fnr006-test` foi encerrado sem `--volumes`, e `ps --all`/inventário de volumes ficaram vazios. Nenhuma chamada Pluggy, conexão ao banco de desenvolvimento/Neon ou check remoto foi feita.
- Validação documental FNR-007 (6.2, 17/11/2026): `openspec validate fnr-007-modelar-persistencia-nucleo --strict` passou; links relativos foram conferidos em `AMBIENTE.md`, `BACKLOG.md`, `TDD.md` e nos quatro artefatos OpenSpec da mudança, todos com destino existente. `git diff --check` não apontou erros, apenas avisos informativos de conversão LF/CRLF do Git. Inspeção de padrões de atribuição literal de segredos em nove arquivos-alvo não encontrou achados; `.env` permaneceu ignorado e fora do status, sem ser lido. BACKLOG 0.7 foi preparado para revisão, preservando a aprovação 0.6. Naquele momento, revisão/aceite da implementação ainda estavam pendentes e não havia commit, PR, integração, publicação, sync ou arquivamento.

#### Atualização após aceite e arquivamento — 17/11/2026

Após concluir 19/19 tarefas, o titular aceitou explicitamente a implementação FNR-007 nesta conversa do OpenCode. A delta spec `financial-data-persistence`, composta somente por requisitos ADDED, foi sincronizada para `openspec/specs/financial-data-persistence/spec.md`; `openspec validate --specs --strict` aprovou as seis specs. A mudança foi arquivada em `openspec/changes/archive/2026-11-17-fnr-007-modelar-persistencia-nucleo`. A evidência PostgreSQL da tarefa 4.2 foi preservada sem repetição. Naquele momento, ainda não havia commit, push ou PR; nenhum check/review remoto havia sido consultado, e não houve acesso a Pluggy, Neon ou banco de desenvolvimento.

O committer criou `7c0b941` para arquivar a mudança OpenSpec FNR-006, `25df020` para FNR-007 e `ec3c511` para registrar PR e estado documental. O [PR #16](https://github.com/VictorJer/GestFinan-a/pull/16) foi integrado em 17/11/2026 às 21:54:24 UTC por `VictorJer`, no merge `6aef0a0cac5f9c0f7a4591bb88f6ccaba6a72079`. O titular aprovou explicitamente o PR e autorizou o BACKLOG 0.8 na `main` nesta conversa; o GitHub registrou `reviews: []`, portanto esse aceite não é descrito como review formal da plataforma. `statusCheckRollup: []`, `gh pr checks` não reportou checks, a proteção da branch `main` retornou 404 (não protegida) e a consulta de rulesets retornou `[]`; ausência de checks/reviews não foi tratada como aprovação. A `main` local foi atualizada com `git pull --ff-only origin main` até o merge commit e ficou sincronizada com `origin/main`. O titular esclareceu que queria o pull, não um build; nenhum build foi executado depois da integração. Sem publicação/deploy ou aplicação de migration em ambiente de destino. O BACKLOG 0.9, com este registro pós-merge, aguarda revisão documental.

### Comandos da solução

Execute da raiz, com o SDK exato disponível:

```powershell
# Rejeita alterações no grafo dos cinco projetos sem atualizar os locks aceitos.
dotnet restore backend/Finora.slnx --locked-mode
# Compila somente o grafo já restaurado.
dotnet build backend/Finora.slnx --no-restore
# Usa MTP selecionado em global.json e exige testes executáveis, não só compilação.
dotnet test --solution backend/Finora.slnx --no-build
```

Os locks foram gerados pelo primeiro `dotnet restore backend/Finora.slnx`, inclusive o lock sem pacotes do Domain real. Com lock existente, `Directory.Build.props` ativa o modo bloqueado também no restore implícito do build. Atualização intencional exige revisão e `dotnet restore backend/Finora.slnx --force-evaluate -p:RestoreLockedMode=false`, seguida de diff dos locks e das validações acima; não é o comando habitual.

Evidência intermediária: restore bloqueado com saída 0, build sem restore com zero avisos/erros e MTP com 4/4 testes de referências aprovados. Em cópia descartável sob o diretório temporário aprovado do OpenCode, a inclusão de Microsoft.Extensions.Primitives 10.0.15 somente no projeto Domain da cópia causou a falha esperada `NU1004`: lock inconsistente. O grafo e os locks do checkout não foram alterados pelo ensaio. Nenhum banco, container ou segredo foi usado. Testes HTTP serão acrescentados nas tarefas seguintes; esta evidência não encerra o incremento nem representa checks do GitHub.

### Evidência HTTP e navegador — 14/11/2026

- Inicialização pelo comando documentado, perfil `http` em loopback. Consulta HTTP real a `http://127.0.0.1:5080/health`: 200, `application/json; charset=utf-8`, corpo exato `{"status":"ok"}`.
- No navegador controlado por Playwright, `/swagger` redirecionou para `/swagger/index.html`, carregou o documento OpenAPI 3.1 e mostrou somente `GET /health`. Às 19:21 UTC, a sequência expandir operação → **Try it out** → **Execute** apresentou **Server response 200**, URL local `/health` e corpo `{"status":"ok"}`. Consulta ao console: zero mensagens, erros ou avisos. Não se tratou apenas de resposta HTML; o JavaScript executou uma requisição real. Capturas geradas ficam locais/ignoradas; esta evidência sanitizada é o registro versionável.
- Testes HTTP e de referências: 20/20 aprovados em execução intermediária. Incluem contrato mínimo nos três ambientes, documento/esquema, HTML, inicializador e asset locais em Development, e 404 sem corpo para rotas de documentação em Production/Staging com `/health` preservado.
- Uma execução inicial falhou porque o teste procurava a URL do documento no HTML: SwaggerUI 10.2.3 a publica em `index.js`. Após autorização explícita do autor (“Corrigir e continuar”), a verificação passou a conferir o inicializador local, sem reduzir a cobertura de HTML/assets ou alterar o desenho. O teste foi repetido e passou.
- A configuração e o inventário Git não contêm segredos, conexão real ou saídas bin/obj versionadas. Não há autenticação fictícia, CORS permissivo, segundo gerador, dados financeiros ou chamadas externas na API. Não comprova integração Pluggy nem checks do GitHub.

### Validação final da solução — FNR-003

Da raiz, em 14/11/2026: `dotnet restore backend/Finora.slnx --locked-mode` (saída 0), `dotnet build backend/Finora.slnx --no-restore` (saída 0, zero avisos/erros), `dotnet test --solution backend/Finora.slnx --no-build` (saída 0, **20 aprovados, zero falhos/ignorados**). Nenhum banco, container, provedor ou segredo foi necessário. A API iniciada para o navegador foi encerrada pelo agente após a conferência; o término provocado do processo retornou saída 1 no comando de longa duração, não uma falha de inicialização, build ou teste. Não foi avaliado encerramento gracioso de produção (FNR-025).

Validação documental: `openspec validate fnr-003-criar-base-backend --strict` aprovado; `git diff --check` sem erros (somente avisos informativos LF/CRLF). Conferidos 36 links locais nos documentos afetados e artefatos da mudança: todos os destinos existem; o link histórico do TDD para FNR-002 foi corrigido editorialmente para o arquivo arquivado equivalente. JSONs de configuração/locks analisados; inventário dos arquivos novos/modificados revisado, inclusive os ainda não rastreados. Comentários dos blocos manuais novos em português; locks gerados não foram editados manualmente. Escopo limitado à base FNR-003 e seus registros, sem banco, provedor, autenticação, frontend, CI ou deploy. Entrega apresentada para revisão, com backlog em validação e sem commit/PR nesta execução.

### Revisão e aceite da entrega — 14/11/2026

A revisão posterior, somente por leitura, comparou todo o diff, incluindo arquivos novos, com PRD, TDD, SDD e specs aprovados. Não encontrou problemas que exigissem correção no escopo FNR-003. Conferiu DT-38, Controller sem regras financeiras, diagnóstico mínimo, isolamento Development, testes de comportamento e comentários em português. Os cinco locks eram coerentes com os assets locais; versões e contentHash de 47 pacotes coincidiram com o metadata normalizado do cache NuGet. A revisão confirmou que “restore bloqueado” se refere ao comando `--locked-mode` aprovado; NU1004 foi o ensaio negativo separado. OpenSpec estrito e diff check foram repetidos e passaram. Restore/build/test e navegador não foram repetidos nessa revisão para não gerar arquivos; seus resultados são os da implementação anterior, não checks do GitHub.

Após essa revisão, o autor declarou nesta conversa: “O autor aprovou a entrega da FNR-003 e autorizou registrar o aceite, criar commits locais pelo committer e abrir o PR.” O aceite da entrega é explícito e separado dos aceites prévios do SDD/TDD. A autorização cobre somente FNR-003 e o envio da branch necessário à abertura do PR, preservando trabalho não relacionado; não autoriza merge, arquivamento, publicação ou avanço para FNR-004. O backlog permanece em validação até a integração e demais gates aplicáveis.

### Integração e encerramento FNR-003 — 14/11/2026

O autor informou “ACABEI DE ACEITAR O PR” e depois solicitou explicitamente a conferência do PR #8/merge, registro final, sync, arquivamento, commits pelo committer e PR documental, sem merge nem FNR-004. Essa solicitação posterior autoriza o encerramento e preserva os limites históricos do aceite acima.

O GitHub confirmou [PR #8 MERGED](https://github.com/VictorJer/GestFinan-a/pull/8), integrado por `VictorJer` em 14/11/2026 às 20:22:31 UTC no commit `2bd63ae37bbbbf20d8ec90e9a96c08d49933075f`, com pais `bef8a30` e `e038393`. O diff entre a árvore do merge e o segundo pai/head revisado foi vazio: o código integrado corresponde à entrega validada e aceita. A revisão assistida e o aceite explícito são evidências registradas fora do GitHub; não há review formal APPROVED (`reviews: []`).

Na consulta de encerramento: `statusCheckRollup: []`; zero check runs e zero status contexts no head `e038393`; a API de status agregado retorna `pending` com `statuses: []` e `total_count: 0`, o que não identifica um check concreto pendente. Main sem proteção clássica (404 Branch not protected), regras aplicáveis ou rulesets; nenhum workflow presente no merge. Não foi encontrada exigência aplicável de check remoto ou review formal adicional para FNR-003. Isso não transforma ausência em aprovação, mas tampouco cria bloqueio artificial: os critérios locais, revisão, aceite e integração sustentam a conclusão do escopo. A consulta mostra a configuração atual, não prova que ela nunca tenha mudado historicamente.

As validações locais e o navegador são os resultados anteriores registrados acima, não novos checks do GitHub; o acompanhamento documental não altera código, projetos, configurações ou locks. O escopo não exige migration, infraestrutura ou publicação: a base local foi integrada, mas o produto financeiro/hospedado não está entregue. FNR-004 permanece não iniciada.

Sync efetuado para duas specs consolidadas (4 requisitos e 6 cenários), com Purpose e conteúdo normativo preservados e sem cabeçalhos delta. `openspec validate --specs --strict` e validação estrita da mudança antes da movimentação aprovados; comparação integral confirmou equivalência delta/main. Mudança com 14/14 tarefas e todos os artefatos completos movida para [arquivo FNR-003](../openspec/changes/archive/2026-11-14-fnr-003-criar-base-backend/design.md). Encerramento preparado em `docs/fnr-003-encerrar`, para um único PR documental sem merge nesta execução e sem cadeia posterior de registros.

Validação final do encerramento: `openspec validate --all --strict` aprovou as duas specs consolidadas (esse comando não revalida mudanças arquivadas); a mudança foi validada estritamente enquanto ativa, antes do movimento. Após mover, comparação integral delta/main, metadata `schema: spec-driven`/data de criação e 14 caixas completas preservados; `openspec list --json` retornou zero mudanças ativas. Conferidos 44 destinos locais em README, TDD, backlog, guia, arquivo FNR-003 e specs: todos existem. `git diff --check` sem erros; apenas avisos informativos LF/CRLF. O diff de backend/global.json contra o merge base permaneceu vazio, sem necessidade de repetir build/test/browser para esta alteração exclusivamente documental. Os commits de encerramento serão conferidos pelo agente principal e o PR vinculará estes resultados sem alegar checks remotos aprovados.

### Início e aprovação do SDD — FNR-004 — 15/11/2026

O autor aprovou explicitamente a proposta, a spec e o SDD de `fnr-004-consultar-pluggy-local` e autorizou separadamente a implementação nesta sessão. Naquele início, a autorização ainda não selecionava uma versão exata do MediatR nem aprovava instalação; o gate foi posteriormente fechado para 12.5.0, conforme registrado abaixo. A aprovação inicial não representa aceite funcional ou da prova real. O trabalho segue na feature branch `feature/fnr-004-consultar-pluggy-local`; a tarefa FNR-005 permanece não iniciada.

### Contrato Pluggy conferido — FNR-004 — 15/11/2026

Referências oficiais consultadas:

- [Autenticação](https://docs.pluggy.ai/en/reference/authentication) e [`POST /auth`](https://docs.pluggy.ai/en/reference/auth/auth-create): enviar `clientId` e `clientSecret` no corpo JSON; resposta contém `apiKey`, usada no header `X-API-KEY`. A chave dura duas horas e é somente server-side.
- [`GET /items/{id}`](https://docs.pluggy.ai/en/reference/items/items-retrieve): resposta é um objeto Item, com `status`, `executionStatus`, `error` e `consentExpiresAt` (este último pode ser nulo). A referência mostra `status: UPDATED` e `executionStatus: SUCCESS`; a guia de uso pessoal documenta `LOGIN_ERROR`/`USER_AUTHORIZATION_EXPIRED` e alerta que um item ainda pode exibir o último `UPDATED` mesmo após expiração. A API não enumera nessa referência todos os estados permitidos; implementação deve falhar de forma fechada para valores desconhecidos e avaliar `consentExpiresAt` separadamente do último status.
- [`GET /accounts`](https://docs.pluggy.ai/en/reference/account/accounts-list): requer `itemId`; resposta paginada por número de página, com `page`, `total`, `totalPages` e `results`. A referência classifica cartão com `type: CREDIT` e `subtype: CREDIT_CARD` (não `BANK`); cada conta inclui `id` e `itemId`. A convenção de listas não-v2 é `page`/`pageSize`, com paginação a percorrer até `totalPages` conforme a [referência de conceitos e paginação](https://docs.pluggy.ai/en/reference/basic-concepts).
- [`GET /v2/transactions`](https://docs.pluggy.ai/en/reference/transaction/transactions-list-by-cursor): requer `accountId`; envelope contém `results` e `next` obrigatório, que pode ser `null`; os registros incluem `accountId`. A primeira página pode ser consultada sem cursor e `next` é opaco/ready-made; esta prova só informa se há continuação e não o segue.
- [Guia oficial de uso pessoal Meu Pluggy](https://docs.pluggy.ai/en/docs/guides/meu-pluggy-personal-use): o `consentExpiresAt` é a validade da autorização do aplicativo; reads podem retornar vazios após expiração antes de o último status mudar. Renovação ocorre pelo Meu Pluggy/Dashboard. O exemplo antigo da guia usa `/transactions`, mas a [referência de conceitos](https://docs.pluggy.ai/en/reference/basic-concepts) declara esse endpoint deprecated, indisponível após 31/12/2026 e já indisponível para aplicações criadas a partir de 02/06/2026. O SDD usa exclusivamente `/v2/transactions`; não há fallback ou mistura de parâmetros v1/v2.

Não foi encontrada incompatibilidade material entre os endpoints documentados e o fluxo delimitado. A lista exaustiva de estados do Item não está publicada na referência consultada; isso não autoriza inferir aptidão de estados desconhecidos, que serão rejeitados. A prova real e a confirmação do acesso da conta continuam pendentes.

### Gate DT-49 — candidato avaliado, aguardando seleção — 15/11/2026

O candidato recomendado para manter a linha aprovada 12.x é `MediatR` **12.5.0**, o patch mais recente da linha indicado pela lista de versões do NuGet. Esta versão não está selecionada nem instalada. O pacote NuGet declara licença `Apache-2.0`; seus assets são `net6.0` e `.NETStandard2.0`, e a página NuGet calcula compatibilidade com `net10.0`, alvo atual do backend. Para o asset `net6.0`, o nuspec declara `MediatR.Contracts [2.0.1, 3.0.0)` e `Microsoft.Extensions.DependencyInjection.Abstractions 8.0.0`; a superfície `AddMediatR`/registro de handlers está no próprio pacote, sem pacote separado de extensões. O Application atual é `net10.0` e não tem referências NuGet, portanto nenhuma versão transitiva efetivamente resolvida ou compatibilidade de DI foi validada ainda; isso exige restore/build e atualização dos locks após autorização.

Fontes consultadas: [NuGet MediatR 12.5.0](https://www.nuget.org/packages/MediatR/12.5.0) (publicado em 01/04/2025, Apache-2.0, frameworks, dependências e histórico de versões), [nuspec 12.5.0](https://api.nuget.org/v3-flatcontainer/mediatr/12.5.0/mediatr.nuspec), [README da tag v12.5.0](https://github.com/LuckyPennySoftware/MediatR/blob/v12.5.0/README.md) (registro DI transitivo como transient), e [GitHub Advisory Database para NuGet/MediatR](https://api.github.com/advisories?ecosystem=nuget&affects=MediatR), consultada em 15/11/2026, que retornou lista vazia. Esse resultado não é auditoria completa nem substitui a verificação de vulnerabilidades durante restore.

O NuGet já lista as linhas 13, 14 e 15, com 15.0.0 publicada em 12/11/2026; 12.5.0 é, portanto, uma linha antiga, cuja manutenção futura não está garantida pelas fontes consultadas. O TDD e o SDD limitam a escolha à linha 12.x e exigem aprovação exata antes de instalar; não se muda silenciosamente para outra linha. Em 15/11/2026, o autor selecionou e aprovou explicitamente `MediatR 12.5.0` nesta sessão. A autorização cobre essa versão exata, não outra linha nem atualização automática. Na avaliação inicial, instalação/restore e resolução efetiva das transitivas ainda estavam pendentes; foram executadas após a aprovação, conforme evidência abaixo.

Após a aprovação, a tarefa 2.1 instalou `MediatR 12.5.0` apenas em `Finora.Application` e regenerou locks por restore normal com modo bloqueado desativado para atualizar os arquivos existentes. O lock do Application resolveu `MediatR.Contracts 2.0.1` e `Microsoft.Extensions.DependencyInjection.Abstractions 8.0.0`; o lock dos testes resolveu a mesma dependência de MediatR e DI abstractions `10.0.15` pela composição da API/host .NET 10. Não foi adicionado pacote ao Domain. O handler delega ao contrato `IPluggyLocalReader`, encaminha cancelamento e devolve somente resultado categorizado/contadores; não usa HTTP, console ou segredos. O teste envia a query por `ISender`, descobre handlers no assembly Application e resolve leitor scoped com validação de escopos ativada. `dotnet test backend/Finora.slnx` passou: 21/21, sem falhas. Isso valida composição automatizada, não a integração real Pluggy.

Na tarefa 2.2, `pluggy-read` é identificado antes da composição Web; o comando cria somente um host genérico, resolve `ISender` em escopo e nunca chama `Run`/`Start`. Argumentos adicionais e comandos desconhecidos retornam código 2; resultados mapeiam os códigos D5, cancelamento retorna 130 e falhas inesperadas são sanitizadas. A configuração HTTP normal não registra MediatR nem resolve configuração Pluggy. Os testes cobrem comando conhecido com leitor substituído, desconhecido/argumentos extras, códigos 0–4 e os testes existentes de health/OpenAPI/Swagger. `dotnet test backend/Finora.slnx` passou: 29/29. O vínculo do leitor real e sua configuração no host de comando pertencem às tarefas 3.1 em diante; não foi feita chamada externa.

Na tarefa 3.1, `Finora.Api` recebeu `UserSecretsId` não secreto para habilitar User Secrets locais; opções `Pluggy:ClientId`, `Pluggy:ClientSecret`, `Pluggy:ItemId` e `Pluggy:AccountId` são lidas de configuração externa, sendo AccountId opcional. O host do modo de comando registra o cliente; o modo HTTP normal não registra nem valida essa configuração. `Finora.Infrastructure` adicionou `Microsoft.Extensions.Http 10.0.15`, alinhado à baseline ASP.NET Core existente, e locks efetivos atualizados. O cliente usa `https://api.pluggy.ai` fixo, rejeita URLs de outro host e cria handler com redirect/cookies automáticos desativados. A autenticação POST `/auth` envia as credenciais no corpo, valida campos antes da chamada, mantém `apiKey` em campo privado apenas em memória, aplica-a somente no header `X-API-KEY` das requisições autenticadas e limpa o campo ao descartar o cliente; nenhuma resposta financeira é persistida.

Fontes técnicas: [HttpClientFactory e configuração do handler](https://learn.microsoft.com/dotnet/core/extensions/httpclient-factory#configure-the-%60httpmessagehandler%60) e [User Secrets no ASP.NET Core](https://learn.microsoft.com/aspnet/core/security/app-secrets?view=aspnetcore-10.0). Testes simulados cobrem ausência de configuração sem request, contrato de autenticação, destino/redirect, rejeição de destino externo, limpeza da key, argumentos sensíveis e composição normal sem segredos. `dotnet test backend/Finora.slnx` passou: 34/34. Sem configuração privada nem acesso real usados. A consulta do item, seleção do cartão e transações permanecem pendentes nas tarefas seguintes.

Na tarefa 3.2, o cliente consulta somente `status`, `executionStatus`, `consentExpiresAt` e presença de `error` do Item; a implementação aceita exclusivamente `UPDATED` + `SUCCESS` sem erro e falha de forma fechada para os demais estados porque a referência oficial não publica enumeração exaustiva. Expiração passada bloqueia mesmo com o último status exitoso; validade ausente é registrada como `Unavailable` e não é interpretada como eterna. As contas são lidas página a página até `totalPages` (500 por página), com validação de envelope, `itemId`, IDs repetidos e vínculo. Cartão exige `type=CREDIT` e `subtype=CREDIT_CARD`; `AccountId` externo só é aceito quando corresponde a exatamente uma conta do item e é cartão; sem seleção externa exige exatamente um cartão.

Testes simulados cobrem consentimento expirado, validade não informada, estado desconhecido/erro do item, cartão posterior em páginas de contas, conta corrente, cartão ausente/ambíguo, AccountId externo e vínculo com outro item. `dotnet test backend/Finora.slnx` passou: 41/41. Sem acesso Pluggy real ou conteúdo financeiro usado; a inspeção ainda é etapa de preparação para a leitura de transações da tarefa 3.3.

Na tarefa 3.3, `PluggyLocalReader` solicita uma única vez `GET /v2/transactions?accountId=...` depois de validar item e cartão. Exige envelope `results` e `next`; `next: null` é página final, cursor string não vazio significa somente `has_more=true`, e campo ausente ou tipo incompatível falha como resposta inválida. Cada registro precisa de ID opaco e `accountId` igual ao cartão selecionado. A saída contém contagem e indicador de continuação, sem cursor, IDs, descrições, valores ou cálculo de indicadores; página vazia é `NoTransactions`/inconclusiva, não prova de leitura com lançamentos. Nenhuma página seguinte é seguida.

Os testes com HTTP simulado cobrem página válida com cursor, `next: null`, página vazia, envelope/cursor/JSON inválidos, ID ausente e vínculo incorreto. Durante a validação, corrigiu-se o DTO para distinguir `next: null` de campo ausente e o teste de composição DI para resolver cliente scoped dentro de scope. `dotnet test backend/Finora.slnx` passou: 45/45, zero falhas. Nenhum segredo real, request externo ou payload financeiro foi usado. Isso é evidência apenas simulada; prova real Pluggy permanece pendente.

Para concluir a tarefa de documentação 2.3, o README registra invocação sem launch profile (`dotnet run ... --no-launch-profile -- pluggy-read`), finalidade local e limite da primeira página, sem valores sensíveis e sem alegar importação. O entry point foi executado com configuração ausente: emitiu somente resumo `invalid_configuration`, enumerou nomes das chaves ausentes e terminou sem chamada externa ou listener; o harness reportou término não zero (1), enquanto o mapeamento interno 0–4 é coberto por teste. A execução sem credenciais não comprova integração real.

Na tarefa 3.4, cada chamada HTTP recebe timeout de 30 segundos e o fluxo local tem limite total de 120 segundos; ambos não executam retries. Falhas HTTP 401/403/429/5xx, transporte e timeout são reduzidas a categorias sem corpo ou mensagem externa. Cancelamento do titular propaga `CancellationToken` e retorna 130; timeout global retorna falha operacional (1), sem imprimir sucesso parcial. Testes usam tempos curtos injetados somente nos fixtures para cobrir timeout por chamada, limite total, cancelamento externo, mapeamento HTTP e ausência de retries (contagem de requests). `dotnet test backend/Finora.slnx` passou: 53/53, zero falhas. Os limites de produção permanecem 30s/chamada e 120s total.

Na tarefa 3.5, a composição explícita do cliente Pluggy filtra em `LogLevel.None` toda a categoria `System.Net.Http.HttpClient`, que inclui os logs `LogicalHandler` e `ClientHandler` com URIs/escopos. A saída própria continua uma lista permitida: resultado, etapa, contagem, continuação, categoria, chaves ausentes e estado informativo da expiração; não renderiza URLs, headers, corpos, IDs ou exceções. Referência Microsoft: [logs do IHttpClientFactory e categorias por cliente](https://learn.microsoft.com/aspnet/core/fundamentals/http-requests?view=aspnetcore-10.0#httpclient-and-lifetime-management).

Testes de privacidade usam logger capturador em Trace e handler HTTP simulado com sentinelas para ClientId/Secret, API key, item/account/transaction IDs, query/cursor, descrição, valor, corpo de falha e mensagem de `HttpRequestException`. Caminhos de sucesso, 401 e falha de transporte foram exercitados pelo comando/handler real; saída e logs não contêm sentinelas. Testes anteriores também cobrem stdout, stderr (argumentos sensíveis rejeitados), cancellation e timeout sem imprimir conteúdo parcial. `dotnet test backend/Finora.slnx` passou: 58/58, zero falhas, incluindo a validação de que argumentos de configuração não contornam o roteador do comando local. Nenhum dado real foi usado em fixtures; a prova externa privada está registrada adiante.

Na validação 4.1, repetida após a revisão de segurança, `dotnet restore backend/Finora.slnx --locked-mode` concluiu sem alterações; `dotnet build backend/Finora.slnx --no-restore` compilou os cinco projetos com 0 avisos/0 erros; `dotnet test --solution backend/Finora.slnx --no-build` passou 58/58 (0 falhas, 0 ignorados). Os testes incluem arquitetura, handler/DI, ausência de listener no comando/health, contratos simulados, cancelamento/timeouts e privacidade. Nenhum teste automatizado usa credenciais Pluggy reais.

### Bloqueio da prova externa — FNR-004 — 15/11/2026

A tarefa 4.2 não foi executada contra a Pluggy: a execução segura do entry point sem launch profile informou ausentes `Pluggy:ClientId`, `Pluggy:ClientSecret` e `Pluggy:ItemId`; assim o fluxo terminou localmente antes de autenticar ou abrir request. Não foram lidos, solicitados ou exibidos valores secretos. A prova de autenticação, item, cartão e pelo menos um lançamento permanece bloqueada até o titular configurar essas opções em armazenamento privado local e solicitar/autorizar a nova execução. Nenhuma alegação de leitura real, aceite RF01 ou importação completa é feita. FNR-005 não iniciada.

#### Execução real autorizada — 15/11/2026

Após o titular confirmar a configuração privada e autorizar a execução, o comando real terminou com código `0` em `2,3 s`. O resumo sanitizado foi: `status=transactions_read`, etapa `transactions`, `321` transações, `has_more=false`, falha `none`, consentimento `valid`. O fluxo chegou à etapa de transações após autenticação, validação do item e seleção de cartão; nenhum ID, credencial, header, URL, corpo, descrição ou valor financeiro foi capturado ou registrado. Essa execução comprova leitura positiva de uma página na ocasião, não importação completa, persistência, sincronização futura nem aceite final do escopo. A data acima é local da execução; nenhuma evidência GitHub/PR foi produzida.

### Revisão técnica e de privacidade — FNR-004 — 15/11/2026

Revisão do diff e evidências contra RF01/RNF-01/02/03/09/10: RF01 tem prova real positiva limitada à página observada; não demonstra cobertura histórica, importação ou persistência. RNF-01 mantém Client Secret/API key fora da resposta e do repositório; a API key é memória scoped. RNF-02 não há persistência nem dados reais em fixtures/documentos, além da contagem agregada sanitizada da prova. RNF-03 o comando é roteado antes da composição Web, sem listener; API/health não registra integração Pluggy. RNF-09 timeout de 30s por request, 120s total, zero retries e códigos de cancelamento/falhas cobertos por mocks. RNF-10 saída permitida, filtros HTTP e instruções locais foram testados com sentinelas.

Durante a revisão foi encontrado e corrigido um desvio: argumentos iniciados por `--` poderiam escapar do roteador local e ser interpretados pela configuração padrão do host Web. Agora argumentos não reconhecidos são rejeitados sem eco; só a assinatura exata de host usada pelo `WebApplicationFactory` é aceita para testes HTTP. Regressão verificada pela solução: 58/58 testes aprovados. Inspeção do inventário/diff não encontrou segredo nem dado financeiro individual versionado; somente os artefatos da mudança FNR-004 e os arquivos de código/documentação/locks delimitados foram alterados. Specs consolidadas não foram editadas nem sincronizadas.

O TDD canônico recebeu revisão 0.6 para registrar o fechamento do gate DT-49 em `MediatR 12.5.0`. Em 15/11/2026, após apresentação do diff, o autor aprovou explicitamente “Aprovo ambos”, cobrindo a revisão documental 0.6 e o conteúdo de implementação FNR-004. A aprovação está registrada no histórico do TDD. Não há commit, PR, merge ou publicação nesta execução, e FNR-005 não foi iniciada.

Validação final documental: `openspec validate fnr-004-consultar-pluggy-local --strict` aprovou a mudança; `git diff --check` não encontrou erros (somente avisos informativos LF/CRLF); foram verificados 37 links Markdown locais nos documentos afetados, todos com destino existente. Esses checks validam estrutura e consistência documental, não aprovam a revisão TDD 0.6 nem substituem o aceite humano da implementação.

### Guia de configuração local — FNR-004 — 15/11/2026

O comando `pluggy-read` é exclusivamente local e não inicia listener. Use fontes externas ao repositório: User Secrets no ambiente Development (`UserSecretsId` está no projeto `backend/Finora.Api`) ou variáveis de ambiente protegidas. As opções são `Pluggy:ClientId`, `Pluggy:ClientSecret`, `Pluggy:ItemId` e `Pluggy:AccountId` opcional; nomes equivalentes para ambiente são `Pluggy__ClientId`, `Pluggy__ClientSecret`, `Pluggy__ItemId` e `Pluggy__AccountId`. O backend não lê valores Pluggy nem faz chamadas no modo HTTP normal.

Configure os valores diretamente no armazenamento privado da sua máquina conforme o [guia oficial de User Secrets](https://learn.microsoft.com/aspnet/core/security/app-secrets?view=aspnetcore-10.0) ou o mecanismo protegido do ambiente. Não inclua valores reais em argumentos digitados, histórico de shell, repositório, tickets, logs ou conversa; não compartilhe segredos aqui. User Secrets evita versionamento, mas não é um cofre criptografado de produção. A API key de sessão é obtida em memória e descartada ao terminar o comando.

`Pluggy:ItemId` identifica a conexão particular. Se `Pluggy:AccountId` não for definido, o comando exige exatamente uma conta com `type=CREDIT` e `subtype=CREDIT_CARD`; zero ou múltiplas contas bloqueiam a seleção. Se definido, o identificador precisa corresponder a uma conta de cartão retornada para aquele item. Obtenha/associe os identificadores no painel privado da Pluggy; não os use como argumento nem os exponha na saída. A leitura é de uma única página de `/v2/transactions`, não armazena compras e não representa importação no Finora.

Cada request tem timeout de 30 segundos, o comando tem teto total de 120 segundos e não há retries automáticos. Exit codes: `0` ao validar ao menos uma transação; `1` falha operacional, contrato ou timeout; `2` configuração/argumentos/seleção inválidos; `3` leitura vazia e inconclusiva; `4` conexão ou consentimento bloqueado; `130` cancelamento. As categorias e resumo não incluem payload/IDs. Um vazio pode refletir indisponibilidade/consentimento, não é prova de histórico sem compras.

Quando o consentimento precisar de renovação, faça-o pelo Meu Pluggy/Dashboard conforme a [orientação oficial de uso pessoal](https://docs.pluggy.ai/en/docs/guides/meu-pluggy-personal-use); o comando não atualiza a conexão nem solicita sincronização à instituição. Atualização na origem pelo titular, leitura diagnóstica local e futura importação persistente no Finora são ações distintas. Nenhuma sincronização bancária ou importação persistida faz parte de FNR-004.

### Análise real do contrato — FNR-005 — 15/11/2026

Após autorização específica do titular no harness, `pluggy-analyze` foi executado localmente no Windows, com .NET SDK `10.0.402`, usando configuração protegida sem ler, exibir ou registrar seus valores. A consulta à API v2 solicitou `dateFrom=2025-11-15`, sem `dateTo`, percorreu uma página e terminou normalmente (`next=null`): `319` registros recebidos, `319` IDs únicos, zero duplicatas, duração monotônica de `503 ms`, coleta completa, `39` registros com data futura pela regra proposta e `24` meses distintos com registros. A contagem agregada de meses não representa cobertura contínua; não foi afirmado que doze meses completos estão disponíveis.

Disponibilidade dos campos na resposta (registros em que o campo/valor opcional estava presente): `id`, `accountId`, `description`, `amount`, `currencyCode`, `date`, `type`, `status` e `operationType`: `319/319`; `billId`: `257/319`; `installmentNumber`, `totalInstallments` e `purchaseDate`: `78/319`; `billPostDate`, `billForecastDate` e `paymentType`: `319/319`. Esta evidência é uma observação agregada de uma única página; não registra IDs, credenciais, cursores, URLs, descrições, datas individuais, valores, respostas brutas ou logs HTTP. A observação não valida por si só o significado financeiro de enumerações, a regra positiva de compra, a semântica institucional de datas ou a cobertura histórica.

O código `0` significa apenas que a coleta terminou com registros. A regra positiva de compra e a competência continuam sujeitas ao confronto conservador da tarefa 5.3; fixtures continuam sintéticas e não substituem esta evidência limitada. Nenhuma transação foi persistida ou importada.

A primeira execução não incluiu distribuições agregadas e, isoladamente, não fornecia evidência suficiente para confrontar a tarefa 5.3. Após autorização específica adicional do titular no harness, foi feita uma única nova coleta local com os mesmos filtros e limites e somente buckets internos fixos/sanitizados:

- Windows local, .NET SDK `10.0.402`; `dateFrom=2025-11-15`, sem `dateTo`; uma página concluída (`next=null`), `319` recebidos/únicos, zero duplicatas, duração monotônica de `597 ms`, `24` meses distintos e `39` registros posteriores à referência temporal proposta.
- Direção/sinal: `DEBIT` 245, `CREDIT` 74, outros 0; sinais positivo 245, negativo 74, zero 0; pares `DEBIT`/positivo 245 e `CREDIT`/negativo 74.
- Status: `POSTED` 257, `PENDING` 62, outros 0. Operação em grupos internos: pagamento 202, ajuste 110, tarifa 7; estorno, operação ausente e outras operações 0. Natureza produzida pelo classificador: pagamento 37, ajuste 37, tarifa 7, desconhecida 238; compra e estorno 0. Formato de data: instante 319; calendário, ambígua, inválida ou timezone indisponível 0.
- Os agregados sustentam somente que os pares direção/sinal observados coincidem com a hipótese de sinal, que há rótulos `POSTED`/`PENDING` e que os formatos de data foram interpretados como instantes. Não demonstram que esses pares sejam compras, não validam semântica institucional da data e não sustentam uma regra positiva de compra: o classificador conservador reconheceu zero compras e retornou 238 naturezas desconhecidas. A tarefa 5.3 permanece bloqueada; não se inferem causas nem regras novas a partir das contagens.
- Consulta de uma página não exercitou paginação real múltipla. `next=null`, os 24 meses distintos e a janela de 12 meses solicitada não demonstram cobertura contínua ou histórico completo; os 39 registros futuros foram preservados conforme o filtro sem `dateTo`.
- Para esta análise restrita, 319 registros em 597 ms não indicam necessidade de staging; processamento transitório página a página é proporcional. Isso não dimensiona importação nem SyncRun: FNR-012/013 devem reavaliar com carga representativa. Nenhuma decisão do TDD foi alterada; sem promessa de cobertura, abatimento de estorno sem vínculo ou retry derivado desta amostra.

Nenhum valor individual, enum externo, ID, descrição, data específica, URL, cursor, payload ou log HTTP foi exibido ou registrado. A consulta adicional consumiu a autorização única concedida; nova execução exigirá outra autorização específica.

### Diagnóstico expandido D7 — execução autorizada — 15/11/2026

Após aprovação explícita do desenho D7 e aprovação dos testes locais, foi consumida uma única execução lógica adicional de `pluggy-analyze`, sem retry: Windows local, .NET SDK `10.0.402`, `dateFrom=2025-11-15`, sem `dateTo`, uma página concluída (`next=null`), `319` recebidos, `319` únicos, zero duplicatas e `527 ms`. A saída e este registro contêm somente buckets internos allowlisted e contagens agregadas; não contêm credenciais, IDs, descrições, valores ou datas individuais.

- Operações recebidas/únicas: `PAGAMENTO` 165/165, `PAGAMENTO_FATURA` 37/37, `TARIFA` 7/7 e `OPERACOES_CREDITO_CONTRATADAS_CARTAO` 110/110. Operações externas não reconhecidas, ausentes e demais códigos sem ocorrência não foram impressos.
- Cruzamentos operação × direção × sinal × status: pagamento, `DEBIT`/positivo, `POSTED` 115/115 e `PENDING` 50/50; pagamento de fatura, `CREDIT`/negativo, `POSTED` 35/35 e `PENDING` 2/2; tarifa, `DEBIT`/positivo, `POSTED` 7/7; operações de crédito contratadas, `DEBIT`/positivo, `POSTED` 66/66 e `PENDING` 7/7, e `CREDIT`/negativo, `POSTED` 34/34 e `PENDING` 3/3. As bases recebidos/únicos coincidem porque não houve duplicatas.
- Status × presença de `billId`: `POSTED`/presente 257/257; `PENDING`/ausente ou nulo 62/62. O identificador nunca foi registrado.
- `paymentType`: `SINGLE` 241/241 e `INSTALLMENT` 78/78; os demais buckets não tiveram ocorrência publicada. Metadados de parcelas: ambos ausentes 241/241, completos e estruturalmente válidos 78/78; parcial/inválido não foi publicado. A categoria não prova semântica institucional nem resolve a diferença documental entre os dois pares de enums.
- Datas: `date` e `billPostDate` válidos em 319/319; `purchaseDate` válido em 78/78 e ausente/inválido em 241/241; `billForecastDate` presente na resposta em 319/319, mas não interpretável pelo parser diagnóstico em 319/319. Relações `date` × `purchaseDate`: mesma data 9/9, diferente 69/69, ausente/inválida 241/241; `date` × `billPostDate`: mesma data 153/153, diferente 166/166; `purchaseDate` × `billPostDate`: mesma data 1/1, diferente 77/77 e ausente/inválida 241/241. Os três pares que envolvem `billForecastDate` ficaram ausentes/inválidos em 319/319. Não se registraram os valores de data nem se atribuiu competência.

Esta execução não altera o diagnóstico anterior nem permite inferir o conteúdo dos buckets a partir de transações individuais. Reforça somente que o contrato diagnóstico atual não conseguiu interpretar `billForecastDate` e que as categorias `paymentType` observadas não resolvem por si sós a semântica institucional. Natureza desconhecida, compra desligada e competência não confirmada permanecem; tarefa 5.3 aguarda conferência local do titular.

### Roteiro de conferência local D7 — sem compartilhar transações

Faça a conferência diretamente no Nubank e/ou Meu Pluggy, sem copiar nomes, valores, datas, IDs, capturas de tela ou linhas de extrato para chat, repositório, ticket ou fixtures. Use somente seu histórico privado e registre localmente as conclusões agregadas:

1. Compare, em exemplos que você reconheça no app, se os grupos de operações e os status agregados podem representar mais de uma natureza; em particular, não trate `PAGAMENTO` ou `OPERACOES_CREDITO_CONTRATADAS_CARTAO` como compra só pelo rótulo, direção ou sinal.
2. Verifique se lançamentos `POSTED` e `PENDING` que você reconheça como pagamento/fatura aparecem em contextos diferentes e se a presença/ausência de vínculo de fatura é coerente. Não anote nem compartilhe o identificador da fatura.
3. Confira localmente se `SINGLE`/`INSTALLMENT` parecem corresponder a compra à vista/parcelada na sua conexão. Não converta os rótulos para `A_VISTA`/`A_PRAZO` nem conclua equivalência sem evidência visível na interface/documentação da conexão.
4. Para os casos parcelados que a interface identifica, confira apenas se os metadados de parcela parecem completos e coerentes (parcela atual positiva e não maior que o total); não reproduza números ou datas individuais fora do ambiente privado.
5. Compare os papéis exibidos para data da transação, data de compra e data/postagem de fatura. A previsão de fatura não foi interpretável no diagnóstico; não tente inferir sua semântica a partir dos buckets ausentes/inválidos e não a trate como competência confirmada.
6. Ao concluir, informe apenas uma síntese sem conteúdo de compra, por exemplo: “conferido localmente; consistente / inconsistente / inconclusivo por dimensão”, e eventuais observações sem datas, valores, descrições ou identificadores. Não envie linhas, prints ou exemplos de compras.

A conferência é evidência local do titular, não um teste automatizado nem aprovação formal do TDD. Até o titular apresentar uma síntese sanitizada, 5.8 e 5.3 permanecem abertas; não alterar classificações nem regra positiva de compra.

### Revisão do incremento parcial — FNR-005 — 15/11/2026

Após a apresentação do estado da branch, do resultado agregado e das limitações, o autor respondeu pelo harness **“Aceito parcial”**. O aceite é somente do incremento parcial documentado e validado: não fecha a tarefa 5.3, não declara a FNR-005 concluída, não aprova revisão nova do TDD e não autoriza commit, PR, merge ou publicação. O gate da regra positiva de compra/competência permanece bloqueado; esta revisão não representa aprovação formal no GitHub nem check remoto.
