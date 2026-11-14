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
