# Design

## Context

Motivação e rastreabilidade em [proposal.md](proposal.md). O autor aprovou explicitamente este SDD e as baselines técnicas propostas em 14/11/2026, por confirmação nesta conversa do OpenCode. A aprovação inclui npm 11.19.1, dotnet-ef local 10.0.15, MSTest.Sdk 4.5.3 com Microsoft.Testing.Platform e Vitest 5.1.0, além das demais versões listadas. Ela não representa aceite do diff, integração ou publicação. O desenho é necessário pela ambiguidade de fixação/compatibilidade e pela redistribuição das entregas entre três itens do backlog.

Observações locais na exploração, sem build ou restore de projetos:

| Ferramenta | Evidência | Limite |
|---|---|---|
| .NET | SDK 10.0.402; ASP.NET Core 10.0.13 | Sem `global.json` ou solução |
| Node / npm | 24.22.0 / 11.19.1 | Sem projeto npm |
| Angular CLI | Ausente do PATH e lista global npm | Não foi executado CLI remoto |
| EF CLI | 10.0.13 global | Não garante reprodução por clone |
| Docker | Cliente 29.9.1, contexto desktop-linux | Pipe dockerDesktopLinuxEngine ausente; servidor não consultável |
| Repositório | Apenas documentos e OpenSpec; nenhuma spec consolidada | Sem aplicação ou testes para validar |

## Goals / Non-Goals

**Goals:** separar inventário, versões propostas, verificações estáticas e validações executáveis futuras; preparar ferramentas locais reproduzíveis e documentar pré-requisitos sem criar aplicações.

**Non-Goals:** gerar dependências/locks sem projeto, validar integração bancária, configurar PostgreSQL/Neon, criar containers, CI, testes de aplicação ou automatizar instalação do sistema. A indisponibilidade do Docker não deve ser escondida por um teste fictício.

## Decisions

### 1. Preservar o limite confirmado entre preparação e projetos

FNR-002 prepara ferramentas e matriz; FNR-003 cria backend/MSTest e seu lock NuGet; FNR-019 cria Angular/Vitest e `package-lock.json`. O runner Angular pode ser escolhido agora, mas sua execução só será comprovada em FNR-019. Alternativa rejeitada pelo recorte confirmado: criar scaffolds antecipados para produzir locks. Nenhuma API ou comportamento de produto muda, justificando `skip_specs: true`.

### 2. Baseline técnica proposta para revisão

| Item | Baseline proposta | Forma de reprodução / validação |
|---|---|---|
| SDK .NET | 10.0.402, sem prerelease | `global.json`, `rollForward: disable`; `dotnet --version` deve corresponder |
| Node | 24.22.0 | `.node-version`; documento explica instalação/seleção, pois o arquivo não troca o Node sozinho |
| npm | 11.19.1 | Versão documentada e verificada; futuro `packageManager` em FNR-019, sem `package.json` vazio agora |
| EF CLI | 10.0.15 | `.config/dotnet-tools.json` com versão exata; restore local e versão conferida no repositório |
| EF Core | 10.0.15 | Baseline documentada para FNR-003/006; não adicionar pacotes agora |
| MSTest | MSTest.Sdk 4.5.3 + Microsoft.Testing.Platform | Documentar agora; projeto e seleção efetiva do runner em FNR-003 |
| Angular CLI / build | 22.2.7 | Comando futuro versionado; instalação no workspace em FNR-019, sem CLI global obrigatório |
| Angular framework/compiler | 22.2.6 | Pacotes do framework alinhados entre si; CLI e framework podem ter patches diferentes |
| TypeScript | 6.0.3 | Atende `>=6.0 <6.1`; não usar o `latest` 7.0.2 |
| Runner Angular | Vitest 5.1.0 | Compatível com peer `^4.0.8 || ^5.0.0` do build 22.2.7; validar no projeto em FNR-019 |
| Tailwind / Chart.js | 4.3.5 / 4.6.1 | Baselines documentadas; integração e locks nas mudanças frontend pertinentes |
| Npgsql EF | 10.0.3 | Candidato documentado; conferir dependências transitivas com EF 10 no restore de FNR-006 |
| Docker | Cliente observado 29.9.1 | Documentar versão de servidor/Compose se acessíveis; não obrigar atualização global |

Versões de pacotes consultadas em 14/11/2026 nos metadados públicos npm e NuGet. São propostas baseadas em disponibilidade e contratos declarados, **não prova de instalação, restore, build ou testes**. Dependências transitivas pertencem aos locks reais futuros. A versão do PostgreSQL e a imagem/digest serão fechadas em FNR-006, junto à compatibilidade do ambiente de destino; nenhum banco será provisionado aqui.

Alternativas: instalar sempre `latest` produz incompatibilidade conhecida de TypeScript e poderia trocar EF 10 por EF 11; instalar CLI/EF apenas globalmente esconde dependências da máquina; migrar para pnpm/yarn não traz benefício demonstrado ao projeto individual. Proposta: npm já disponível, versões explícitas e ferramenta EF local, preservando majors confirmadas pelo TDD.

### 3. Arquivos e responsabilidades na aplicação futura

- `global.json`: somente SDK nesta etapa; runner e SDK MSTest entram junto ao projeto de FNR-003.
- `.node-version`: versão exata do Node. Não adotar gerenciador de versões obrigatório sem necessidade.
- `.config/dotnet-tools.json`: ferramenta local EF; usar invocação local explícita para não confundir com a instalação global 10.0.13.
- `docs/AMBIENTE.md`: guia operacional subordinado ao TDD, com baseline, fontes, comandos, evidências sanitizadas, políticas de locks/atualização, diagnóstico Docker e pendências por item. Não é TDD concorrente. JSON estrito terá sua intenção explicada aqui, sem comentários inválidos.
- `docs/TDD.md`: registrar decisões aprovadas e redistribuição de validações nos §§7/14, preservando IDs e histórico. Alteração de conteúdo fica em revisão pendente até aprovação explícita; não transferir aprovação de 13/11.
- `docs/BACKLOG.md`: ajustar critérios FNR-002/003/019 e registrar estado verdadeiro de FNR-002, mantendo dependências e sem iniciar outros itens.
- `README.md`: apenas link/comandos pertinentes à preparação, sem reescrever status não relacionado.

Alternativa descartada: criar manifestos npm e locks vazios para aparentar reprodução. O lock NuGet será produzido com dependências reais em FNR-003 e validado por restore bloqueado; o lock npm será produzido com o frontend em FNR-019 e validado por `npm ci`.

### 4. Diagnóstico Docker delimitado

Na aplicação, conferir `docker context show`, `docker context ls`, `docker version`, `docker info` e `docker compose version`. Se necessário, consultar estado do Docker Desktop/processos e WSL por comandos de leitura, sem despejar configuração/variáveis de ambiente que possam conter segredos.

Pipe ausente não prova causa única. Se o Desktop estiver parado, orientar o autor a iniciá-lo e repetir a verificação; não mudar engine/contexto, instalar WSL ou alterar virtualização automaticamente. Se houver reparo sistêmico necessário, registrar causa/evidência e pedir autorização específica. Engine inacessível mantém a preparação parcialmente bloqueada; não concluir FNR-002 como ambiente integralmente pronto. Não executar pull/run/Compose up nesta mudança.

### 5. Critérios de aceite proporcionais

1. SDK selecionado e Node/npm conferidos contra a baseline aprovada; ferramenta EF local restaurável e invocada com versão correta.
2. Matriz identifica explicitamente compatibilidades declaradas, versões instaladas e validações diferidas, sem afirmar testes de projetos inexistentes.
3. Docker engine Linux e Compose acessíveis, com evidência sanitizada; caso contrário, registrar bloqueio e não concluir o item.
4. Locks NuGet/npm e runners executáveis atribuídos expressamente a FNR-003/FNR-019 no backlog/TDD, sem arquivos fictícios.
5. Documentação/links, configurações e OpenSpec validados; nenhum segredo, dado bancário, scaffold, container, banco ou deploy criado.
6. Revisões documentais e do SDD registradas honestamente; encerramento apenas após aceite e integração aplicáveis.

## Risks / Trade-offs

- SDK exato reduz flexibilidade -> atualizar intencionalmente com revisão e repetir validações; não fazer roll-forward silencioso.
- Versões disponíveis hoje podem mudar -> manter números explícitos, fontes/data e nunca substituir silenciosamente por `latest`; falha de disponibilidade deve ser reportada.
- Compatibilidade por metadados não garante execução -> manter restore/build/test dos projetos como gates de FNR-003/006/019.
- Docker depende do estado da máquina -> não tentar reparar o sistema fora do escopo; registrar bloqueio se persistir.
- Baseline EF local difere da global -> usar comando local explícito e documentar a diferença; não atualizar/remover a global.
- Redistribuição altera texto aprovado do TDD -> preservar aprovação/histórico anteriores e submeter a revisão nova ao autor.

## Migration Plan

Não há migration de banco nem publicação da aplicação. Após aprovação do SDD e solicitação explícita de apply, preparar arquivos e documentação na feature branch, validar e apresentar diff para revisão documental. Para desfazer antes da integração, remover apenas arquivos introduzidos por esta mudança e restaurar somente suas alterações documentais mediante ação autorizada, preservando trabalho alheio; não remover SDKs/ferramentas globais ou dados Docker.

Depois do aceite e verificações aplicáveis, encaminhar PR com rastreabilidade. Planejar arquivamento no mesmo PR quando houver aceite; não há delta specs a sincronizar. Nenhum PR, commit, merge ou arquivamento é autorizado pela geração deste plano.

## Open Questions

- Estado do engine Docker após início manual do Desktop e versão de Compose: verificação operacional específica na aplicação; não muda o recorte nem autoriza reparos sistêmicos.
- Restore/build/test das baselines frontend/backend e versão efetiva das dependências transitivas: evidências reservadas aos projetos correspondentes, não pendências a preencher com resultados presumidos nesta mudança.

## Referências técnicas

- [Compatibilidade Angular](https://angular.dev/reference/versions) e [testes padrão](https://angular.dev/guide/testing): Node 24.22.0 atende Angular 22; TypeScript 6.0.x e Vitest.
- [Seleção de SDK .NET](https://learn.microsoft.com/dotnet/core/tools/global-json) e [MSTest.Sdk](https://learn.microsoft.com/dotnet/core/testing/unit-testing-mstest-getting-started).
- Metadados npm: `https://registry.npmjs.org/` para os pacotes da tabela; NuGet: `https://api.nuget.org/v3-flatcontainer/` para `mstest.sdk`, `microsoft.entityframeworkcore` e `npgsql.entityframeworkcore.postgresql`. Consulta de disponibilidade não substitui teste integrado.
