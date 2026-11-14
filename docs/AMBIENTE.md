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
