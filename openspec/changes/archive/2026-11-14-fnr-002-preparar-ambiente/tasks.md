# Tasks

O estado de execução é indicado pelas caixas abaixo; a existência deste plano, por si só, não comprova aprovação. A aplicação prossegue após a aprovação explícita registrada em [design.md](design.md) e a solicitação de apply. Referência: [design.md](design.md); specs dispensadas por `skip_specs: true`.

## 1. Registrar o recorte e a baseline aprovada

- [x] 1.1 Conferir aprovação explícita do SDD e das propostas de versões/runners, registrando fonte e data na mudança; verificar que a aprovação cobre npm, EF local, MSTest.Sdk/MTP e Vitest, sem presumir consentimento a partir da geração dos artefatos.
- [x] 1.2 Ajustar `docs/BACKLOG.md` nos critérios de FNR-002/003/019 e no registro de execução, deixando locks NuGet/npm e testes executáveis nos projetos correspondentes; verificar por diff que IDs, dependências e status dos demais itens permanecem preservados e FNR-002 não foi declarada concluída.
- [x] 1.3 Atualizar pontualmente `docs/TDD.md` com a baseline aprovada e a divisão das validações, preservando decisões herdadas/IDs e aprovação histórica, marcando a revisão de conteúdo como pendente; verificar coerência com os §§7/14, DT-04–07, DT-12, DT-14 e DT-46, sem alterar o PRD.

## 2. Preparar ferramentas reproduzíveis e seu guia

- [x] 2.1 Criar `global.json` para SDK 10.0.402 exato, sem prerelease, e `.node-version` com 24.22.0; criar a seção correspondente de `docs/AMBIENTE.md`, explicando JSON estrito e seleção manual de Node. Verificar parse JSON e executar `dotnet --version`, `node --version` e `npm --version`, comparando com 10.0.402/24.22.0/11.19.1 sem instalar ferramentas globais automaticamente.
- [x] 2.2 Criar manifesto local `.config/dotnet-tools.json` com `dotnet-ef` 10.0.15 e documentar restore/invocação local em `docs/AMBIENTE.md`; verificar `dotnet tool restore` e `dotnet tool run dotnet-ef -- --version` no repositório, distinguindo a versão local da global e sem criar projeto/migration. Se houver falha, registrar erro sanitizado sem trocar a versão silenciosamente.
- [x] 2.3 Completar em `docs/AMBIENTE.md` a matriz e as fontes/datadas do desenho, incluindo TypeScript 6.0.3 em vez do latest 7, CLI/build 22.2.7, framework 22.2.6, runners e bibliotecas propostas; verificar metadados declarados e distinguir disponibilidade, instalação e testes adiados. Incluir a política de atualização explícita e atribuir restore bloqueado NuGet a FNR-003, `npm ci` a FNR-019 e validação EF/Npgsql/PostgreSQL a FNR-006.
- [x] 2.4 Adicionar link pontual ao guia no `README.md`; verificar existência dos destinos e execução dos comandos de inventário documentados, sem criar `package.json`, locks fictícios ou corrigir conteúdo histórico não relacionado.

## 3. Diagnosticar a disponibilidade do Docker

- [x] 3.1 Executar consultas de contexto, cliente/servidor, `docker info` e `docker compose version` conforme o desenho, registrando somente evidências sanitizadas e instruções no guia; verificar se o engine Linux está acessível sem criar containers, baixar imagens ou trocar configurações.
- [x] 3.2 Se o engine continuar inacessível, consultar estado do Desktop/WSL por leitura e orientar início manual pelo autor quando cabível; repetir as consultas e registrar sucesso ou causa/bloqueio concreto em `docs/AMBIENTE.md` e backlog. Verificar que reparos sistêmicos dependem de autorização específica e que FNR-002 não é concluída enquanto o gate de engine/Compose permanecer falho.

## 4. Validar a entrega de preparação

- [x] 4.1 Executar `git diff --check`, validar os JSONs criados e links locais dos documentos alterados, e `openspec validate fnr-002-preparar-ambiente --strict`; registrar comandos/resultados reais e verificar por inventário/diff que não há aplicação, locks vazios, dados bancários, segredos, CI, banco ou publicação introduzidos.
- [x] 4.2 Apresentar o diff do TDD/backlog/guia e configurações para revisão, registrando aceite ou pendências sem transferir aprovações anteriores; verificar critérios do desenho e manter explícita a distinção entre validação local, checks GitHub não consultados e testes futuros de FNR-003/006/019.

Registro: o autor aprovou explicitamente o diff e a revisão documental 0.2 do TDD nesta conversa em 14/11/2026. Checks/revisões do GitHub não foram consultados; não houve autorização de commit, PR, merge ou publicação.

## Workflow follow-up

- Após validação e aceite, e somente com as autorizações pertinentes, preparar commit/PR vinculado a FNR-002, PRD, TDD e este SDD; não enviar diretamente à main.
- Verificar revisões/checks realmente aplicáveis antes da integração por PR; registrar evidência no backlog sem declarar conclusão pelo simples fato de haver artefatos.
- Planejar arquivamento no mesmo PR após aceite quando possível. Não há delta specs a sincronizar; arquivar não comprova merge e a mudança não exige deploy de aplicação.
- Não avançar para FNR-003 ou FNR-019 sem a solicitação e a mudança correspondentes.
