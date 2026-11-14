# Design

## Context

Ver [proposal.md](proposal.md) para motivação e rastreabilidade. O inventário Git consultado em 14/11/2026 contém documentação, ferramentas e OpenSpec, mas não `backend/`, projetos C# ou testes. Não existem padrões de código legado a migrar nem specs consolidadas a modificar. Este desenho é necessário por estabelecer fronteiras entre quatro projetos, novas dependências e isolamento da documentação por ambiente.

As escolhas DT-02/03/04/12 e baseline SDK 10.0.402/MSTest.Sdk 4.5.3 são herdadas. DT-38 ainda é proposta no TDD; a aprovação deste SDD deverá cobrir sua concretização, sem promover silenciosamente a decisão no documento canônico. O pedido de Swagger foi explícito; as decisões de implementação abaixo são propostas para revisão.

Dependências: FNR-001 concluída documentalmente; FNR-002 possui evidências locais em `docs/AMBIENTE.md` e PR [#7](https://github.com/VictorJer/GestFinan-a/pull/7) integrado em 14/11/2026 às 18:44:56 UTC (`a3f8a99cff3fdd51d3af97381cc640377bba7927`), conforme consulta read-only ao GitHub nesta preparação. O backlog ainda indica Em validação. Merge não demonstra por si só aceite/checks: antes de implementar, reconciliar o registro com as evidências aplicáveis; não repetir seleção de ferramentas já aprovada.

## Goals / Non-Goals

**Goals:**
- Tornar as fronteiras de compilação explícitas e testáveis sem criar classes de negócio artificiais.
- Demonstrar inicialização HTTP e isolamento de Swagger/OpenAPI, além de restore reproduzível e runner real.
- Permitir experimentar a única chamada técnica disponível pelo navegador, sem banco, containers ou credenciais.

**Non-Goals:**
- Não entregar RF01/RF14: não há leitura Pluggy, dados bancários, Identity ou JWT neste incremento.
- Não implementar EF Core/Npgsql, migrations, worker, repositórios, dados financeiros fictícios, frontend, CI, Docker de produção ou deploy.
- Não implementar o botão Authorize agora: ele acompanhará autenticação em FNR-009/011 e não será um mecanismo de bypass. Swagger não substitui testes automatizados.

## Decisions

### D1 — Solução pequena com referências direcionadas

Usar `backend/Finora.slnx`, os quatro projetos diretamente sob `backend/` conforme README e um projeto `backend/tests/Finora.Api.Tests/`. Todos visam `net10.0`, com nullable e implicit usings consistentes.

Referências diretas permitidas entre projetos de produção:

| Projeto | ProjectReference permitido |
|---|---|
| Finora.Domain | Nenhum |
| Finora.Application | Finora.Domain |
| Finora.Infrastructure | Finora.Application e Finora.Domain |
| Finora.Api | Finora.Application e Finora.Infrastructure |

Api referencia Infrastructure somente como raiz de composição; Controllers não acessam implementações de banco/provedor. As bibliotecas podem permanecer sem tipos até surgir uma necessidade real. Não criar classes `Class1`, marcadores, DI vazia, interfaces genéricas ou entidades provisórias. A direção das referências será verificada lendo os arquivos de projeto, sem biblioteca adicional de arquitetura e sem tipos artificiais para reflexão.

Alternativas: um único projeto reduziria arquivos, mas contrariaria DT-02; uma estrutura adicional `src/` não é necessária ao tamanho atual; framework de arquitetura seria dependência extra sem benefício neste teste inicial. O teste de ProjectReference não detecta sozinho mau uso dentro de Controllers: isso permanece parte da revisão de código.

### D2 — Endpoint técnico por Controller

Criar um Controller de diagnóstico na Api para `GET /health`, com DTO próprio simples e resposta exata definida em [api-diagnostics](specs/api-diagnostics/spec.md). Sem serviço em Application só para encaminhar uma constante: não existe caso de uso financeiro aqui. O endpoint verifica apenas se o processo atende HTTP, não prontidão de banco ou provedor; a operação completa em produção será tratada em FNR-025.

Alternativas: usar o exemplo WeatherForecast produziria comportamento sem utilidade e dados fictícios; implementar health de dependências anteciparia itens de banco/integração. Usar Controller, em vez de Minimal API, exercita DT-03 e a descoberta real do endpoint no OpenAPI.

### D3 — OpenAPI nativo com Swagger UI, somente em Development

Gerar o documento via Microsoft.AspNetCore.OpenApi, registrando Controllers/ApiExplorer. Servir `/openapi/v1.json` e Swagger UI em `/swagger` dentro da mesma condição de ambiente Development; a UI aponta para o documento local. Usar apenas o pacote Swashbuckle.AspNetCore.SwaggerUI para a interface, sem um segundo gerador SwaggerGen. Assets são locais, não CDN. Documentar resposta, tipo e propósito do endpoint; o nome `v1` é o identificador do documento, não uma estratégia aprovada de versionamento financeiro.

Fora de Development não registrar rotas de documento nem middleware da UI. Validar Production e Staging sem exposição; não usar lógica do tipo “diferente de Production”. Perfis locais vinculam a loopback e identificam Development, sem expor em rede pública por padrão. Portas locais livres são detalhe operacional a registrar na implementação.

Alternativas: Swashbuckle completo também gera OpenAPI, mas duplicaria a responsabilidade escolhida; Scalar não atende ao pedido específico de Swagger UI. Documentação só em Development reduz exposição, mas não substitui autenticação dos endpoints financeiros futuros.

### D4 — Dependências exatas, locks reais e MTP

Preservar o SDK fixo de `global.json`; adicionar nele apenas a seleção do runner `Microsoft.Testing.Platform`. Usar `MSTest.Sdk/4.5.3` explicitamente no projeto de testes. Não misturar runners VSTest e MTP nem adicionar outro framework de testes.

Api recebe Microsoft.AspNetCore.OpenApi; testes recebem Microsoft.AspNetCore.Mvc.Testing para WebApplicationFactory. Para os dois pacotes Microsoft, verificar primeiro o patch estável 10.0.15 da linha 10, coerente com a baseline existente; para SwaggerUI verificar 10.2.3, encontrado na documentação consultada. Estes são candidatos, não versões declaradas disponíveis por este planejamento. Confirmar metadados oficiais e compatibilidade antes de fixar: se indisponíveis, escolher e registrar patch estável compatível da mesma linha; alteração de major, SDK ou baseline aprovada exige revisão antes de continuar. Não usar `latest`, faixas flutuantes ou adicionar EF/Npgsql só porque estão na matriz futura.

Ativar geração de `packages.lock.json` para os projetos backend, versionando o grafo real e verificando restore com `--locked-mode`. Versionar também locks sem dependências diretas quando gerados por projetos reais; não criar locks à mão. A versão do SDK de testes continua fixada no projeto: o lock PackageReference não substitui esse pin. Um build não pode atualizar silenciosamente o lock aceito.

Alternativas: fixação só das dependências diretas não controla o grafo transitivo; central package management não se justifica ainda. O SDK de testes evita configurar manualmente um conjunto redundante de adapters/runners.

### D5 — Testes úteis sem regras financeiras inventadas

Um projeto MSTest contém testes HTTP com WebApplicationFactory e testes do grafo de projetos. Tornar o entry point da Api acessível ao host de teste sem alterar seu comportamento de execução. Cada host usa ambiente explícito, sem banco, rede externa ou segredos.

| Verificação | Evidência esperada |
|---|---|
| GET /health sem autenticação/configuração financeira | 200, JSON exato e ausência de campos extras |
| Development: documento | 200, OpenAPI válido, operação GET /health e contrato 200 JSON |
| Development: UI e asset | HTML/configuração aponta ao documento local; asset JS servido localmente |
| Production: UI, index, asset e documento | 404; /health continua 200 |
| Staging: UI e documento | 404 |
| ProjectReference dos quatro projetos | Correspondem à tabela D1, incluindo Domain sem referências |
| Execução manual real pelo Swagger | Try it out retorna 200 e corpo esperado |

Os testes automatizados inspecionam JSON/HTML/assets; a execução JavaScript completa do Swagger é verificada no navegador, sem introduzir uma suíte frontend. Uma resposta 200 do HTML não comprova que Try it out funciona. O teste do grafo resolve caminhos a partir da localização do projeto/repositório, não de caminho absoluto da máquina.

Alternativas: teste `Assert.IsTrue(true)` comprovaria apenas que o runner iniciou; criar cálculo financeiro para testá-lo anteciparia FNR-005/018. Não exigir PostgreSQL para smoke tests desta etapa.

### D6 — Configuração e documentação proporcionais

Configurações versionadas contêm somente opções não sensíveis e exemplos claramente fictícios; não gerar JWT, credenciais Pluggy ou strings de conexão. Documentar User Secrets/variáveis de ambiente como mecanismo futuro sem solicitar valores agora. Não ligar CORS permissivo nem endpoints financeiros “temporários”.

Na implementação aprovada, atualizar pontualmente README e `docs/AMBIENTE.md` com comandos verificados; registrar FNR-003 no backlog. Atualização de conteúdo do TDD para DT-38/Swagger gera revisão pendente preservando a aprovação 0.2, e exige apresentação/aceite antes de tratar a nova revisão como aprovada. Não alterar requisitos de produto para fazer Swagger parecer funcionalidade financeira. Comentários em português explicam os blocos lógicos novos de produção, testes e configurações manuais; JSON estrito é explicado no guia associado.

## Risks / Trade-offs

- [Compatibilidade ainda não executada] → Consultar metadados, fixar candidatos compatíveis, executar restore/build/test real e não apresentar o planejamento como evidência de sucesso.
- [MTP pode descobrir zero testes ou receber opções VSTest] → Usar comandos MTP documentados, conferir contagem positiva e código de saída; não aceitar apenas build verde.
- [Swagger habilitado por engano fora de Development] → Condição única positiva de Development e testes negativos em Production/Staging, incluindo assets.
- [Health interpretado como integração pronta] → Corpo mínimo e documentação explícita de liveness, sem indicação de banco/Pluggy.
- [Arquitetura vazia gerar complexidade sem utilidade] → Limitar aos quatro projetos aprovados, sem abstrações ou módulos sem caso de uso.
- [Backlog divergente do PR #7] → Reconciliar evidências de FNR-002 antes de implementar, sem tomar merge/arquivamento como aceite automático.
- [SDK rígido impede execução em outro clone] → Documentar pré-requisito exato, não relaxar roll-forward silenciosamente.

## Migration Plan

Não há banco ou aplicação existente a migrar, nem publicação nesta mudança. Implementar somente após revisão/aceite do SDD e nova solicitação de apply, em feature branch. A revisão documental necessária pode preceder a criação dos projetos.

Validar da raiz: restore da solução com locks gerados, novo restore bloqueado, build sem restore e testes em modo MTP com contagem positiva. Candidatos de comandos a conferir na execução: `dotnet restore backend/Finora.slnx --locked-mode`, `dotnet build backend/Finora.slnx --no-restore` e `dotnet test --solution backend/Finora.slnx --no-build`. Demonstrar que divergência controlada de dependência em cópia descartável provoca falha de restore bloqueado, sem alterar o grafo aceito do checkout. Depois executar API local e testar pelo Swagger. Nenhum desses comandos de aplicação foi executado nesta preparação.

Registrar evidências sanitizadas, validar OpenSpec e diff; somente com autorizações pertinentes criar commits pelo committer e PR. Integração exclusivamente pelo PR. Não alegar checks remotos quando só há validação local; não fazer deploy fictício. Se necessário desfazer uma entrega já integrada, planejar reversão por PR das alterações desta mudança, preservando trabalho posterior; sem reset ou descarte de terceiros. Sincronizar specs e arquivar somente após validação e aceite aplicáveis, preferencialmente no mesmo PR.

## Referências técnicas consultadas

- [ASP.NET Core 10: OpenAPI e Swagger UI](https://learn.microsoft.com/aspnet/core/fundamentals/openapi/using-openapi-documents?view=aspnetcore-10.0): gerador nativo separado da interface, restrição a Development.
- [Swashbuckle.AspNetCore](https://github.com/domaindrivendev/Swashbuckle.AspNetCore): interface e configuração de endpoint local; documentação consultada via Context7.
- [MSTest / Microsoft.Testing.Platform](https://github.com/microsoft/testfx): MSTest.Sdk, pin explícito e seleção do runner em global.json; documentação consultada via Context7.

## Estado de revisão

SDD proposto em 14/11/2026 e aprovado explicitamente pelo autor no harness nesta execução, pela opção “Aprovar SDD e prosseguir”. A pergunta de aceite identificou as referências DT-38 (Domain sem referências; Application → Domain; Infrastructure → Application/Domain; Api → Application/Infrastructure), o diagnóstico mínimo GET /health e Swagger/OpenAPI exclusivamente em Development, abrangendo este desenho e as specs. O autor solicitou apply; a revisão canônica do TDD será apresentada separadamente na tarefa 1.3. Não há autorização de commit, PR, merge ou publicação nesta execução.

Conferência inicial: branch `docs/fnr-003-criar-base-backend`, fora da main; somente o diretório desta mudança estava não rastreado. Os artefatos existentes foram preservados. Nenhum código foi criado antes deste aceite.

Gates posteriores: FNR-002 reconciliada com validações locais e merge do PR #7; autor confirmou “Revisei e aceito” no harness quanto à entrega integrada, sem review formal ou checks reportados no GitHub. TDD 0.3 foi apresentado como pendente e aprovado separadamente por “Aprovo o diff” antes da criação do backend. Implementação e evidências locais em [AMBIENTE.md](../../../../docs/AMBIENTE.md): restore bloqueado/build, 20 testes MTP, divergência rejeitada em cópia e execução real no Swagger. O ajuste autorizado do teste para conferir o inicializador `index.js` não mudou decisões ou escopo. Entrega apresentada para revisão, sem integração/publicação nem avanço a FNR-004 nessa etapa.

Aceite posterior da entrega em 14/11/2026: após revisão somente por leitura do diff completo, sem problemas que exigissem correção no escopo, o autor aprovou explicitamente a FNR-003 e autorizou registro, commits locais pelo committer e abertura do PR. Evidências e limites da revisão em [AMBIENTE.md](../../../../docs/AMBIENTE.md#revisão-e-aceite-da-entrega--14112026). Essa autorização posterior não altera os limites históricos acima; não permitia merge, arquivamento, publicação ou avanço para FNR-004 naquela etapa. As 14 tarefas implementadas, sozinhas, não comprovavam integração.

## Encerramento confirmado — 14/11/2026

Após o autor informar o aceite do PR e solicitar explicitamente sync/arquivamento e PR documental, o GitHub confirmou [PR #8 integrado](https://github.com/VictorJer/GestFinan-a/pull/8) por VictorJer às 20:22:31 UTC, merge `2bd63ae37bbbbf20d8ec90e9a96c08d49933075f`. A árvore coincide com o head validado/revisado `e038393`. Revisão assistida sem achados que exigissem correção no escopo e aceite explícito do autor estão registrados separadamente das evidências de plataforma: reviews e statusCheckRollup vazios, zero check runs/status contexts, sem workflows e sem proteção clássica/regras/rulesets aplicáveis na consulta atual. Não há exigência adicional não atendida identificada; ausência de checks não equivale a aprovação nem cria bloqueio por si só. Limites históricos da consulta e validações locais em [AMBIENTE.md](../../../../docs/AMBIENTE.md#integração-e-encerramento-fnr-003--14112026).

Os critérios FNR-003 foram atendidos: solução/referências aprovadas, locks reais e restore com --locked-mode, build sem avisos/erros, 20 testes MTP, diagnóstico mínimo e Swagger verificado no navegador. Não há migration ou publicação aplicável; não se declara integração financeira ou produto hospedado pronto. Todos os artefatos completos e 14/14 tarefas. Delta specs sincronizadas para [api-diagnostics](../../../specs/api-diagnostics/spec.md) e [api-exploration](../../../specs/api-exploration/spec.md), preservando Purpose, 4 requisitos e 6 cenários. Validação estrita da mudança antes do movimento, specs estritas e comparação delta/main aprovadas. Arquivamento solicitado executado em `openspec/changes/archive/2026-11-14-fnr-003-criar-base-backend/`.

Registros preparados na branch `docs/fnr-003-encerrar` baseada no merge confirmado, para um único PR documental e commits pelo committer autorizados pelo autor. Não fazer merge desse PR, publicar ou iniciar FNR-004 nesta execução. O PR de encerramento é o registro final proporcional: não criar outra mudança apenas para registrar seu futuro merge.
