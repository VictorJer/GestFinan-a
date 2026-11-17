# Finora — Backlog de desenvolvimento

**Versão:** 0.9 — **preparada em 17/11/2026; revisão e aceite documental pendentes**. Registra a aprovação do conteúdo do BACKLOG 0.8 pelo titular, o merge do PR #16 e o pull local da `main`. A versão 0.8 e as aprovações anteriores permanecem preservadas em seus escopos.\
**Criado em:** 13 de novembro de 2026\
**Base:** `docs/PRD.md`, `docs/TDD.md` e `AGENTS.md` do projeto `D:\financeiro`.\
**Estado registrado na preparação da versão 0.7:** FNR-001–004 concluídas nos respectivos escopos conforme §7. Incremento técnico FNR-005 integrado pelo PR #12 em `adee699`; objetivo original 5.3 substituído por revisão de escopo, não tecnicamente atendido. O alinhamento documental foi integrado pelo PR #13 (merge `2c62109`) em 16/11/2026; as duas delta specs da FNR-005 foram sincronizadas e a mudança foi arquivada em `openspec/changes/archive/2026-11-16-fnr-005-validar-contrato-pluggy`. Em 17/11/2026, o GitHub confirmou o commit técnico FNR-006 `c03f0c7275e2725c08201d3f3e246647ed7ab045` na `main`, sem PR associado. O PR #15, de regularização documental, foi integrado em 17/11/2026 pelo merge `d8db9f247be294168286f360910b438570a41188`; alterou somente `docs/AMBIENTE.md` e `docs/BACKLOG.md`. O GitHub não reportou reviews nem status checks no retorno consultado. O merge documental não associa retroativamente o commit técnico a um PR. O titular aceitou em 17/11/2026, no harness, tratar essa ausência como ressalva histórica, sem declará-la conforme ao fluxo de PR. A mudança OpenSpec FNR-006 foi arquivada em `openspec/changes/archive/2026-11-17-fnr-006-postgresql-efcore` após 11/11 tarefas; `specs` foi ignorado, sem delta specs. TDD 0.9, 0.10 e 0.11 foram aprovados explicitamente em 17/11/2026 em seus escopos; 0.10 fixa allowlist SyncRun e 0.11 fixa Identity Version2/128. A FNR-007 está em validação na branch feature com 19/19 tarefas OpenSpec executadas localmente. Integridade/unicidade 4.3–4.4, round-trips/atualizações 5.1–5.3 e rollback 5.4 passaram no PostgreSQL descartável. Restore bloqueado e build da solução passaram; suíte MTP integrada aprovou 166/166, com exclusão deliberada das classes de migration 4.2 para preservar a evidência anterior, e health/OpenAPI/Swagger sem connection strings aprovou 17/17. OpenSpec estrito, links/diff e inspeção de segredos passaram; o serviço temporário está encerrado, sem acesso a Pluggy, banco de desenvolvimento ou Neon. Revisão documental 0.7 e aceite explícito da implementação continuam pendentes; sem commit, PR, integração, publicação, sync ou arquivamento. O explorador continua na mudança existente; FNR-038 reserva recorrência assistida em mudança própria.

**Estado atual (versão 0.9):** FNR-007 foi aceita explicitamente pelo titular, teve `financial-data-persistence` sincronizada e foi arquivada após 19/19 tarefas. O PR #16 foi aprovado pelo titular nesta conversa e integrado em 17/11/2026 às 21:54:24 UTC pelo merge `6aef0a0cac5f9c0f7a4591bb88f6ccaba6a72079`; o titular também autorizou levar o BACKLOG 0.8 à `main`. O GitHub registrou `reviews: []` e `statusCheckRollup: []`; `gh pr checks` não reportou checks, sem tratar ausência como aprovação ou check passado. A main local foi atualizada por `git pull --ff-only origin main` e está no merge commit. Não houve publicação/deploy; nenhum build foi executado após o pull, conforme esclarecimento do titular. A revisão documental atual 0.9 aguarda revisão/aceite; a aprovação do BACKLOG 0.8 fica preservada.

### Histórico desta revisão

- **0.1 — 13/11/2026:** planejamento e registros anteriores preservados como referência histórica; as aprovações e evidências de cada entrega continuam restritas ao escopo registrado no §7.
- **0.2 — preparada em 15/11/2026; aprovada em 16/11/2026:** alinhamento de PRD 0.4/TDD 0.7, OpenSpec e dependências ao MVP descritivo com recorrência assistida. O titular aprovou o conteúdo final e aceitou o encerramento revisado da FNR-005; não renumerar IDs nem converter critérios financeiros antigos em resultados técnicos.
- **0.3 — preparada e aprovada em 16/11/2026:** atualização do registro de execução da FNR-006 com evidências de PostgreSQL/EF Core locais, sem alteração de requisitos ou IDs. O titular declarou explicitamente “revisei os documentos e aprovo eles” nesta conversa do OpenCode, aprovando documentalmente o backlog 0.3 e o TDD 0.8. A aprovação 0.2 permanece preservada.
- **0.4 — preparada e aprovada em 16/11/2026:** registra o aceite explícito do titular à implementação local da FNR-006, sem declarar PR, integração ou publicação. O titular declarou “Aprovo 0.4” nesta conversa do OpenCode. As aprovações das versões 0.3 e anteriores permanecem preservadas.
- **0.5 — preparada e aprovada em 17/11/2026:** registra a presença efetiva da FNR-006 na `main` remota, a ausência de PR associado ao commit técnico e a revalidação local, sem aprovação retroativa do fluxo de integração. Após revisar o conteúdo, o titular aprovou esta revisão documental e autorizou commits locais pelo committer e a abertura de PR. O commit de regularização `56e9569` foi criado e o PR #15 está aberto; merge e arquivamento não foram autorizados. Também aprovou proposta/spec/SDD da FNR-007, mas escolheu regularizar a FNR-006 primeiro. A aprovação 0.4 e os aceites anteriores permanecem restritos às versões e escopos históricos.
- **0.6 — preparada e aprovada em 17/11/2026:** registra o merge documental do PR #15 (`d8db9f2`) e o arquivamento local da mudança FNR-006. O PR #15 alterou somente registros documentais; seu merge não associa retroativamente o commit técnico `c03f0c7` a um PR. A ausência de reviews/status checks foi preservada sem tratá-la como aprovação. O titular aceitou tratar a falta de PR para o commit técnico como ressalva histórica, sem declarar conformidade. O titular aprovou esta revisão e o TDD 0.9 no harness; a revisão 0.5 e aprovações anteriores permanecem preservadas.
- **0.7 — preparada em 17/11/2026; pendente de revisão/aprovação:** registra evidências locais das tarefas 4.3–6.1 da FNR-007 e o estado de validação técnica, sem declarar aceite da implementação. A evidência da tarefa 4.2 foi preservada sem repetição; não há PR, commit, integração, publicação, sync ou arquivamento nesta revisão. A versão 0.6 e todas as aprovações anteriores permanecem preservadas.
- **0.8 — preparada em 17/11/2026; aprovada pelo titular nesta conversa:** registra o aceite explícito da implementação FNR-007, o sync da delta spec e o arquivamento local, preservando a evidência PostgreSQL da tarefa 4.2 sem repetição. O titular aprovou o PR #16 e autorizou levar este conteúdo à `main`; o PR foi integrado pelo merge `6aef0a0cac5f9c0f7a4591bb88f6ccaba6a72079`. A consulta GitHub retornou `reviews: []`/`statusCheckRollup: []`, e a CLI não reportou checks; o aceite ocorreu no harness, não como review formal GitHub. As aprovações anteriores permanecem preservadas.
- **0.9 — preparada em 17/11/2026; pendente de revisão/aprovação:** registra o merge do PR #16, o commit de integração, o pull local da `main` e os estados observados de reviews/checks. Não altera requisitos, decisões técnicas ou evidências de implementação. A aprovação do conteúdo 0.8 e as aprovações anteriores permanecem preservadas.

## 1. Resultado esperado

Entregar o núcleo revisado: autenticar → consultar/importar movimentos do cartão → listar/filtrar e organizar categorias/observações → visualizar cobertura e contagens descritivas sem somas monetárias → confirmar candidatos a recorrência na entrega própria FNR-038. Reconhecimento automático de compras e indicadores financeiros dependentes de compra/competência ficam adiados, não atendidos pelo explorador.

O foco é um único usuário e sua conexão Nubank via Meu Pluggy / API da Pluggy. Investimentos, simulador e CSV são complementos após a estabilidade desse núcleo.

A leitura pelo backend .NET foi demonstrada em escopo local limitado na FNR-004. A FNR-005 acrescentou análise sanitizada e contrato conservador, integrados pelo PR #12; não valida histórico completo, regra geral de compra ou competência. Evidências permanecem em AMBIENTE.md e PLUGGY-CONTRACT.md, sem novas coletas ou reclassificação nesta revisão.

A prova local FNR-004 foi a primeira meta funcional; o fluxo integrado permanece futuro. O encerramento revisado de FNR-005 exige critérios próprios abaixo e não depende de implementar o explorador ou a recorrência. A consulta descritiva pode ser planejada com desconhecidos preservados, respeitando banco, importação, acesso e demais dependências, sem resolver a investigação financeira adiada.

## 2. Como executar este backlog com SDD

O backlog organiza o trabalho; **não substitui as especificações nem aprova uma implementação**.

Para cada mudança, mantendo uma por vez:

1. Conferir os requisitos do PRD e as decisões do TDD; ajustar inconsistências antes de avançar.
2. Criar uma feature branch, conforme `AGENTS.md`. Não implementar diretamente na `main`.
3. Criar ou atualizar a mudança no OpenSpec usando o schema instalado, incluindo proposta, desenho, requisitos/cenários e tarefas aplicáveis.
4. Registrar as revisões e aprovações necessárias. Não presumir aprovação do SDD por existir um item neste backlog.
5. Implementar somente o escopo especificado, com os testes relevantes.
6. Validar o resultado, abrir PR e vincular PRD, TDD, mudança OpenSpec e IDs deste backlog.
7. Integrar por PR somente após revisão, aprovações e checks necessários; atualizar o status e a evidência de conclusão.

Não presumir comandos ou estrutura de artefatos do OpenSpec sem consultar a instalação do projeto. A publicação após atualização da `main` continua condicionada aos checks aprovados.

Na FNR-001 foi confirmado o schema local `spec-driven`. Por ser exclusivamente documental, a mudança declara `skip_specs: true`, sem criar requisitos artificiais da aplicação. O `design.md` registra seu SDD; a disponibilidade dos artefatos no CLI não aprova os documentos nem autoriza implementação.

### Prioridade e status

- **P0:** necessário para o núcleo e sua entrega segura.
- **P1:** complemento, iniciado após o marco de núcleo pronto.
- **P2:** evolução futura, sem compromisso na primeira entrega.
- **Status:** A fazer → Em especificação → Aguardando revisão → Em implementação → Em validação → Concluído. Usar Bloqueado com motivo explícito quando necessário. Um objetivo substituído por revisão aprovada de escopo deve manter critério e resultado históricos, identificado como não tecnicamente atendido; nunca contar a substituição como execução ou aceite automático da entrega.
- **Tamanho:** P = pequeno, M = médio, G = grande. Dividir itens G em tarefas menores no SDD; tamanho não é promessa de duração.
- Dependências indicam pré-requisitos de implementação. Testes, documentação e revisão acompanham cada tarefa, sem esperar a fase final.

## 3. Ordem de execução

| Marco | Itens | Evidência para avançar |
|---|---|---|
| M0 — Preparação | FNR-001 a FNR-003 | Documentos coerentes e backend mínimo compilando |
| M1 — Contrato técnico conservador | FNR-004 e FNR-005 | Prova local limitada, contrato/limitações preservados e aceite do encerramento revisado de FNR-005; objetivo original 5.3 substituído, não tecnicamente atendido |
| M2 — Persistência e acesso | FNR-006 a FNR-011 | Banco local, sessão JWT, revogação e comandos administrativos verificados |
| M3 — Dados utilizáveis | FNR-012 a FNR-018 | Importação consistente, consulta protegida, organização e contagens/cobertura coerentes; sem agregados monetários de movimentos |
| M4 — Aplicação integrada | FNR-019 a FNR-023 e FNR-038 | Login, conexão, lista/visão descritiva e recorrência assistida em entregas próprias; tolerâncias de FNR-038 aprovadas antes de implementar |
| M5 — Entrega do núcleo | FNR-024 a FNR-028 | Checks, publicação e demonstração verificados |
| M6 — Complementos | FNR-029 a FNR-031 | Módulos P1 validados sem regressão no núcleo |

O primeiro marco de risco é M1. Se não for possível consultar os dados reais, registrar a causa e revisar o plano; dados fictícios permitem desenvolver a interface, mas não encerram a tarefa de integração.

O prazo original era de duas semanas. Como a data final não foi confirmada, este documento não reinicia o prazo nem atribui novas datas. Reavaliar a capacidade disponível ao iniciar a execução e preservar P0 antes de P1.

## 4. Backlog principal

O status inicial dos itens é **A fazer**; atualizações ficam no registro de execução (§7). As dependências usam os IDs da primeira coluna.

| ID | Pri. | Tamanho | Entrega | Depende de | Rastreabilidade |
|---|---|---|---|---|---|
| FNR-001 | P0 | P | Alinhar documentação e fluxo OpenSpec | — | PRD §§3, 7–10, 13, 15; TDD §§1, 4, 7–9, 15; AGENTS |
| FNR-002 | P0 | P | Verificar ambiente e fixar versões | 001 | TDD §7 |
| FNR-003 | P0 | M | Criar solução backend e testes mínimos | 001, 002 | TDD §3 |
| FNR-004 | P0 | M | Consultar cartão e transações reais pelo .NET | 003 | RF01; TDD §4 |
| FNR-005 | P0 | M | Consolidar contrato técnico conservador e encerramento revisado; preservar objetivo financeiro original não atendido | 004 | RF02; PRD §§8/13, RF07 financeiro adiado; TDD §§7/10/12, DT-54 |
| FNR-006 | P0 | M | Preparar PostgreSQL local e EF Core | 003 | TDD §§9, 10 |
| FNR-007 | P0 | G | Modelar persistência do núcleo | 005, 006 | RF02, RF03; TDD §10 |
| FNR-008 | P0 | M | Criar e redefinir usuário por comando | 006 | RF14; TDD §5 |
| FNR-009 | P0 | G | Implementar login JWT e sessões persistentes | 008 | RF14; TDD §5 |
| FNR-010 | P0 | M | Revogar sessões no logout e reset de senha | 009 | RF14; TDD §5 |
| FNR-011 | P0 | M | Padronizar segurança e respostas da API | 009 | RF09; TDD §§2, 5, 11 |
| FNR-012 | P0 | G | Importar transações sem duplicação | 005, 007 | RF02, RF03; TDD §§10, 12 |
| FNR-013 | P0 | G | Controlar execução e retomada da sincronização | 011, 012 | RF08, RF09; TDD §12 |
| FNR-014 | P0 | M | Aplicar regra de atualização e estado da conexão | 013 | RF08, RF16; TDD §§6, 11 |
| FNR-015 | P0 | M | Criar API de cartões e consulta descritiva de movimentos, sem soma monetária | 011, 012 | RF04, RF05 revisados; TDD §11 |
| FNR-016 | P0 | M | Provisionar categorias e criar categorias pessoais | 007, 011 | RF06; TDD §15 |
| FNR-017 | P0 | M | Editar categoria e observação do movimento, sem mudar a natureza financeira | 015, 016 | RF06, RF15 revisados; TDD §§6, 15 |
| FNR-018 | P0 | G | Fornecer resumos descritivos não monetários; indicadores financeiros originais adiados | 005 revisada, 015, 017 | RF07 revisado; PRD §8; TDD §13, DT-54 |
| FNR-019 | P0 | M | Criar base Angular com Tailwind | 002, 003 | TDD §3 |
| FNR-020 | P0 | M | Integrar login, sessão e navegação protegida | 010, 011, 019 | RF14; TDD §§3, 5 |
| FNR-021 | P0 | M | Exibir cartão e atualização de dados | 014, 020 | RF08, RF09; TDD §§6, 13 |
| FNR-022 | P0 | G | Implementar lista descritiva, filtros e categorias na interface | 015, 016, 017, 020 | RF04, RF05, RF06, RF09 revisados |
| FNR-023 | P0 | G | Integrar visão descritiva reutilizando lista FNR-022, sem gráficos financeiros | 018, 020, 022 | RF07, RF09 revisados; TDD §13 |
| FNR-038 | P0 | G | Detectar candidatos a recorrência e permitir confirmação pelo titular, em mudança própria | 012, 015, 020, 022; tolerâncias aprovadas antes de implementar | RF17/VP-07; TDD §§10/11/13/14/19, DT-55 |
| FNR-024 | P0 | M | Consolidar testes críticos e CI do núcleo revisado, incluindo recorrência | 010, 013, 017, 018, 023, 038 | TDD §14; RF17 |
| FNR-025 | P0 | M | Preparar Docker de produção e verificação de saúde | 003, 011, 013 | TDD §§2, 9 |
| FNR-026 | P0 | G | Configurar Neon, migrations e publicação da API | 024, 025 | TDD §§2, 9 |
| FNR-027 | P0 | M | Publicar Angular no Cloudflare Pages com gates | 024, 026 | TDD §§2, 9 |
| FNR-028 | P0 | M | Validar núcleo revisado publicado e preparar entrega acadêmica | 021, 022, 023, 026, 027, 038 | PRD §13/RF17; TDD §14 |
| FNR-029 | P1 | G | Implementar carteira manual de investimentos | 028 | RF10, RF11 |
| FNR-030 | P1 | G | Implementar simulador de aportes | 028 | RF12; PRD §8; TDD §13 |
| FNR-031 | P1 | M | Exportar transações filtradas em CSV | 028 | RF13; TDD §11 |

## 5. Critérios de conclusão por tarefa

### Preparação e prova de conceito

**FNR-001 — Documentos e fluxo**

- Atualizar o PRD para refletir as decisões posteriores: ambiente hospedado com autenticação, categorias pessoais e observações.
- Trocar a orientação antiga de envio direto à main pelo fluxo feature branch → revisão → PR → integração, preservando migrations anteriores ao deploy dependente.
- Confirmar a configuração OpenSpec e definir as primeiras mudanças pequenas; registrar o que é proposta técnica e o que já foi aprovado. Não iniciar código com bloqueios de especificação.

**FNR-002 — Ambiente**

- Fixar/documentar versões aprovadas do SDK .NET, Node/npm e ferramenta EF local; registrar fontes, comandos e evidências reais de compatibilidade e disponibilidade.
- Conferir o acesso ao engine Docker Linux e Compose; registrar eventual bloqueio sem criar containers, baixar imagens ou alterar configurações do sistema.
- Não criar projetos nem locks vazios: o lock NuGet e testes executáveis pertencem ao projeto backend de FNR-003; o `package-lock.json`, `npm ci` e testes executáveis pertencem ao projeto frontend de FNR-019. Validação EF/Npgsql/PostgreSQL pertence a FNR-006.
- Documentar a matriz, política de atualização e validações adiadas; não declarar o ambiente concluído enquanto os gates desta tarefa estiverem pendentes.

**FNR-003 — Estrutura backend**

- Criar Api, Application, Domain e Infrastructure com referências na direção definida no TDD; Controllers sem regras financeiras.
- Preparar MSTest.Sdk/Microsoft.Testing.Platform conforme a baseline aprovada, gerar e versionar o lock NuGet com as dependências reais do backend e demonstrar restore bloqueado e teste executável; incluir configuração de exemplo sem segredos e regras de ignore adequadas.
- A solução compila e os comandos documentados de restore/build/test funcionam. Não expor dados bancários em endpoint desprotegido.

**FNR-004 — Leitura real**

- Executar prova de conceito local pelo .NET: autenticar na Pluggy, consultar a conexão configurada, identificar o cartão e recuperar lançamentos.
- Configurar os segredos fora do código e não imprimir credenciais, JWTs ou compras completas em logs/CI.
- Registrar evidência sanitizada de sucesso ou motivo concreto do bloqueio. O painel web do provedor, sozinho, não conclui esta tarefa.

**FNR-005 — Contrato técnico conservador e encerramento revisado**

**Critérios originais da versão 0.1 — preservados como histórico, sem alegar atendimento integral:**

> Conferir paginação real, histórico disponível, tipos, sinais, status, datas, moeda e campos opcionais de parcelas/fatura/limite.
>
> Definir como reconhecer compra, estorno, pagamento de fatura, pendência, parcela futura e natureza desconhecida; não inferir apenas pelo sinal.
>
> Medir volume e duração de leitura; criar exemplos fictícios representativos para MSTest e atualizar o SDD com limitações descobertas.

**Critérios revisados de encerramento — conteúdo final aprovado em 16/11/2026:**

- Identificar o incremento integrado pelo PR #12/`adee699`, preservando as tarefas técnicas concluídas, validações históricas, contratos e regras conservadoras; não ampliar o classificador nem alterar código para fechar o registro.
- Registrar o objetivo original da tarefa 5.3 como **substituído por revisão de escopo — não atendido tecnicamente**, preservando seu texto e evidências sem marcar execução bem-sucedida. Regra geral de compra e competência ficam adiadas para investigação/mudança próprias.
- Preservar as limitações: uma página por coleta real, paginação múltipla apenas simulada, cobertura indeterminada, caso individual confirmado sem generalização e buckets históricos de `billForecastDate` inconclusivos sem reclassificação.
- Alinhar e obter aprovação explícita do conteúdo final de PRD/TDD/backlog e dos artefatos afetados da FNR-005/explorador. Direção aprovada não aprova automaticamente as revisões completas nem o encerramento.
- Validar OpenSpec, links, diff e preservação de código/evidências de forma proporcional; distinguir resultados locais históricos, validação documental atual e checks/reviews efetivamente reportados pelo GitHub. Sem nova consulta real como requisito desta revisão.
- Apresentar o resultado e obter aceite explícito do encerramento revisado. A implementação do explorador FNR-015/018/022/023 e da recorrência FNR-038 não é pré-requisito para encerrar o incremento técnico; não são declaradas entregues por esse aceite. Sync/arquivamento não são executados nesta etapa.

### Banco e autenticação

**FNR-006 — Banco local**

- Disponibilizar PostgreSQL em Docker com persistência de desenvolvimento, parâmetros externos e instruções reproduzíveis.
- Configurar EF Core/Npgsql e executar uma migration de validação local. Não chamar `Database.Migrate()` ao iniciar a API.
- Testes de integração usam banco descartável, não o banco local pessoal nem a Neon hospedada.

**FNR-007 — Modelo relacional**

- Modelar conexão, cartão, movimento, categoria, anotação e SyncRun com FKs, precisão decimal, moeda e datas originais/normalização técnica explicitada; preservar natureza desconhecida e motivos, sem exigir competência financeira confirmada.
- Criar restrições de identidade externa e anotação única, além dos índices necessários às consultas; IDs externos são opacos.
- Migrations funcionam em base vazia e atualização; testar unicidade e rollback de operações que precisam ser atômicas. Investimentos podem esperar P1.

**FNR-008 — Usuário administrativo**

- Criar usuário pelo Identity e permitir redefinir senha via comando, com entrada protegida, sem senha no código/argumentos/logs.
- Repetir criação não duplica usuário nem redefine senha silenciosamente; não disponibilizar cadastro público.
- Integrar posteriormente o reset à revogação transacional de FNR-010 antes da publicação.

**FNR-009 — Login**

- `POST /api/auth/login` valida credenciais com Identity e controle de tentativas; grava a sessão antes de entregar JWT.
- JWT dura 30 minutos, sem refresh token e sem dados financeiros; validar algoritmo, assinatura, emissor, destinatário e expiração.
- Sessão ativa é consultada no banco em cada chamada protegida. Chave forte fica em segredo estável por ambiente; token completo não é persistido.

**FNR-010 — Revogação**

- Logout invalida a sessão atual imediatamente; reset de senha invalida todas as sessões do usuário e lida com login concorrente.
- Token expirado, adulterado, revogado ou de sessão ausente não acessa dados. Indisponibilidade do banco não libera acesso nem simula senha incorreta.
- Testes HTTP exercitam login, acesso, logout, reset e revogação. Se o logout remoto falhar, a interface não pode afirmar que foi confirmado.

**FNR-011 — Base segura da API**

- Exigir autenticação e propriedade dos recursos; UserId não pode ser escolhido livremente no corpo/filtro.
- Configurar CORS para origens específicas, validação, Problem Details, códigos HTTP e traceId sem vazamento de dados.
- Diferenciar 401, 403, conflitos e indisponibilidade. Health mínimo não expõe configuração interna; não abrir cadastro nem conexão bancária pública por conveniência.

### Integração e APIs financeiras

**FNR-012 — Importação consistente**

- Processar todas as páginas do histórico de até 12 meses disponível, preservando as parcelas futuras retornadas em situação própria.
- Repetir a mesma importação não cria duplicatas; alterações de pendências e estornos são atualizadas e anotações pessoais são preservadas.
- Não fundir contas só por nome/final do cartão; ausência em resposta vazia/parcial não exclui histórico. Tratar reconexão e limites de reconciliação.
- Falha no meio da coleta não apresenta o período como completo nem mistura silenciosamente dados parciais com o conjunto publicado.

**FNR-013 — Execução persistente**

- Fechar no SDD o processamento proposto no TDD após a medição de FNR-005; registrar execução, contadores e erro sanitizado.
- Impedir duas execuções simultâneas da mesma conexão; tratar reinício, posse expirada e retomada idempotente sem execução eternamente pendente.
- Se adotar processamento assíncrono: responder 202, consultar SyncRun e usar trabalhador com estado persistente; não usar tarefa solta apenas em memória.
- Testar falha em página intermediária, reinício e erro de autorização; não manter transação de banco aberta durante HTTP externo.

**FNR-014 — Quando atualizar**

- Entrada autenticada solicita atualização se não há sucesso anterior ou se ele tem mais de 24 horas; validar a regra no servidor.
- Botão manual usa o mesmo fluxo, com intervalo mínimo definido após conferir limites; filtros e navegação não disparam importação.
- Exibir separadamente importação no Finora, atualização na origem e cobertura disponível; dados antigos permanecem acessíveis quando a origem falha.
- Implementar orientação de renovação no provedor. Retry limitado para falhas transitórias, respeitando limites; não repetir autorização inválida indefinidamente.

**FNR-015 — Consulta de dados**

- Implementar listagem de cartões e transações com busca, filtros combináveis, ordenação estável e paginação limitada.
- Contagem filtrada de movimentos considera todas as páginas; valor/moeda são individuais. Filtros identificam a data de origem usada e não a chamam competência. Cobertura e completude da importação são explícitas; não retornar agregado monetário em nenhuma camada.
- Testar filtros combinados, conjunto vazio e tentativa de acesso a outro titular. Campos externos ausentes aparecem como indisponíveis, sem exemplos fictícios.

**Critério original adiado, não atendido:** o total monetário de todo conjunto filtrado. Não converter contagem de movimentos em evidência de conciliação financeira; esse objetivo depende de futura validação de compra/semântica temporal.

**FNR-016 — Categorias**

- Provisionar categorias iniciais idempotentes: Alimentação, Transporte, Compras, Lazer, Saúde, Educação, Outros e Sem categoria.
- Permitir listar e criar categorias pessoais, com nome validado e política de normalização documentada; impedir duplicidade e associação a outro usuário.
- Não criar renomeação, arquivamento ou exclusão neste MVP. Testar criação concorrente e reinicialização sem duplicação.

**FNR-017 — Anotações**

- Permitir editar categoria e observação de até 500 caracteres; observação é texto simples e pode ser limpa.
- Preservar data, valor e descrição originais; uma nova sincronização não apaga anotações manuais.
- A alteração aparece nos filtros/contagens descritivas, não muda natureza financeira ou situação e não gera soma. Testar propriedade do movimento e da categoria e o tratamento único de Sem categoria. Confirmação de recorrência pertence a FNR-038, separada da anotação.

**FNR-018 — Resumos descritivos não monetários**

- Fornecer no backend contagens de movimentos por natureza/situação e cobertura/completude, com universo e data de origem explicitados, sem chamar movimentos de compras presumidas.
- Preservar desconhecidos, pendências, parcelas futuras identificáveis, encargos e ajustes como eixos distintos; ausência não vira zero e conjunto parcial não aparece como completo.
- Não calcular ou retornar soma, subtotal, média, distribuição ou série monetária de movimentos, por BRL ou outra moeda, qualquer que seja o rótulo. Testes controlados verificam consistência com filtros/listagem e ausência desses campos.
- Não apresentar renda, saldo bancário ou patrimônio líquido derivados dos movimentos. Categoria pessoal e confirmação de recorrência não liberam métricas financeiras.

**Objetivo original adiado, não atendido:** compras efetivadas líquidas de estornos elegíveis, quantidade de compras, evolução mensal e distribuição monetária por categoria, com agregados BRL conciliados com a lista. Requer regra de compra/semântica temporal e vínculo de estorno para resultado líquido, em mudança financeira própria; não é satisfeito por contagens descritivas.

### Frontend

**FNR-019 — Base Angular**

- Criar Angular, HTML/Tailwind, rotas e componentes compartilhados, com organização por funcionalidades.
- Serviços com Signals e HttpClient/RxJS; configurar URL da API por ambiente, sem segredos de servidor. Fixar o gerenciador npm e as versões acordadas no manifesto do workspace.
- Gerar/versionar `package-lock.json` a partir do projeto real e validar com `npm ci`; build e runner Vitest executável funcionam. O visual usa o protótipo como referência, sem inventar funcionalidades prontas.

**FNR-020 — Sessão na interface**

- Implementar login, guard, interceptor restrito à origem da API e logout com token apenas em memória.
- Expiração ou reload exige novo login; 401 encerra a sessão local e 403 informa falta de permissão. Não usar localStorage, sessionStorage ou refresh token.
- Tratar falha de rede separadamente de credencial inválida e verificar os fluxos críticos.

**FNR-021 — Cartão e conexão**

- Exibir dados reais disponíveis, situação, cobertura e horários; não exibir credenciais nem pedir senha do banco.
- Integrar atualização automática elegível e botão manual, evitando cliques concorrentes e consultas sem término.
- Verificar sucesso, indisponibilidade, autorização expirada e retomada da instância; estados fictícios só em modo de demonstração identificado.

**FNR-022 — Lista descritiva de movimentos**

- Construir a lista paginada com busca, filtros e contagem de todo o conjunto filtrado, valor/moeda individuais, natureza/motivo e situação separados. Sem soma monetária; o critério original de total monetário fica adiado conforme FNR-015.
- Permitir criar categoria e aplicá-la à compra sem recarregar a página; editar observação com retorno de validação.
- Mostrar metadados de parcelamento quando disponíveis, data de origem explicitada, cobertura/completude e desconhecidos; estados vazio/carregando/erro e navegação acessível em desktop/celular.
- Testar filtros e preservação de anotações após atualização.

**FNR-023 — Visão descritiva integrada**

- Integrar os resumos não monetários de FNR-018 e reutilizar a lista/navegação de FNR-022, sem reconstruir uma segunda lista ou duplicar sua entrega. Filtros usam a data explicitada e respeitam a sessão/propriedade.
- Exibir cobertura, situação, natureza identificável/desconhecida e contagens do universo declarado, com resumos HTML acessíveis; refletir categorias sem mudar natureza financeira.
- Não calcular, retornar ou renderizar agregado monetário, inclusive parcial/informativo. Tratar incompletude, ausência e erro sem zeros enganosos; testes API/UI conferem consistência com o backend/lista e ausência de somas.

**Objetivo original adiado, não atendido:** dashboard com gráficos Chart.js de gastos, totais e evolução financeira conciliados. A escolha tecnológica de Chart.js é preservada para quando houver visualização aprovada; não é obrigação criar gráfico financeiro neste MVP.

**FNR-038 — Recorrência assistida — a especificar em mudança própria**

- Preparar proposta, desenho, requisitos/cenários e tarefas próprios após aceite dos documentos revisados. Não acrescentar detector à FNR-005 nem criar capability concorrente do explorador. Nenhuma mudança OpenSpec de recorrência é criada por esta revisão.
- Após histórico persistido/importação FNR-012, consulta protegida FNR-015, sessão FNR-020 e lista FNR-022, planejar detecção no backend de candidato após padrão em três meses consecutivos de histórico observado. Valor igual sozinho não basta; duplicatas e parcelas apenas previstas não contam como três cobranças mensais observadas.
- Definir/aprovar tolerâncias de valor, datas e descrição e critérios executáveis de correspondência antes de implementar o detector. Essa pendência bloqueia FNR-038, não o encerramento técnico FNR-005 nem a consulta/lista descritiva.
- Permitir ao titular confirmar assinatura, parcela ou compras independentes; preservar a decisão separadamente dos dados e metadados de parcelamento da origem nas sincronizações. Detalhar modelo, contratos e fluxo na mudança própria; não escolher rotas ou algoritmo implicitamente aqui.
- Testar correspondências/falsos positivos com fixtures sintéticas, três meses/meses ausentes, coincidência de valor, duplicatas, projeções, metadados, acesso por proprietário e preservação da confirmação. Não usar coletas históricas como fixtures nem inventar taxa de acerto.
- Confirmação não altera valor/data/descrição, natureza financeira, status ou competência e não libera somas de gastos. A mudança própria precisa de implementação, validação e aceite antes de satisfazer RF17/FNR-024/028; não é dependência de FNR-018/023.

### Qualidade, publicação e TCC

**FNR-024 — Checks**

- Consolidar testes MSTest e testes críticos Angular já criados com as funcionalidades, incluindo FNR-038 após sua entrega; integração usa PostgreSQL descartável e Pluggy simulada.
- Builds e testes relevantes rodam em CI e falham para regressões de importação, segurança, consistência descritiva, ausência de agregados monetários e recorrência/confirmação. Cálculos de gastos adiados não viram exigência do MVP; testes próprios de P1 acompanham suas entregas. Segredos/dados bancários reais não são necessários ao CI.
- Definir checks obrigatórios e impedir publicação anterior ao resultado. Não estabelecer percentual de cobertura arbitrário.

**FNR-025 — Imagem e operação**

- Criar Dockerfile da API, configuração de porta/ambiente e health mínimo, com passos reproduzíveis para o autor.
- Não depender de filesystem persistente no Render; segredos são externos. Validar inicialização, encerramento e tratamento da execução interrompida.
- Documentar geração/rotação de chaves e os mecanismos de proteção usados pelos comandos do Identity.

**FNR-026 — API hospedada**

- Configurar Neon e Render, TLS, segredos e conexão de banco apropriada; validar limites do plano e não registrar credenciais.
- Documentar/aplicar migrations compatíveis com a versão ativa antes do deploy dependente; falha impede a integração que dispararia a publicação.
- Publicar automaticamente apenas o commit aprovado pelos checks; impedir auto-deploy paralelo e versão antiga sobrescrevendo nova.
- Verificar saúde, login, autorização e leitura real no ambiente hospedado; registrar falhas e recuperação.

**FNR-027 — Frontend hospedado**

- Configurar Cloudflare Pages com build estático, rotas SPA e URL da API; ajustar CORS na API para a origem definitiva.
- Publicar após checks, a partir do commit validado; considerar que frontend/API não têm troca de versão atômica.
- Validar login, sessão, atualização, tabela e gráficos publicados, inclusive primeira abertura após inatividade da API.

**FNR-028 — Entrega do núcleo**

- Executar checklist do PRD revisado: integração real limitada conforme evidência, importação repetível sem duplicação, anotações persistidas, lista/contagens/cobertura coerentes, ausência de agregados monetários, sessão/revogação e falhas visíveis. Não declarar os indicadores financeiros antigos atendidos.
- Validar RF17/FNR-038 publicada: candidato após padrão aprovado em três meses consecutivos, confirmação pelo titular, tolerâncias registradas, metadados separados e decisão preservada sem alterar natureza/status/competência. FNR-038 permanece entrega própria, não implementação implícita nesta revisão.
- Confirmar ausência de segredos/dados pessoais versionados e preparar demonstração com dados fictícios ou anonimizados, sem alterar a origem real.
- Atualizar README com comandos verificados, ambiente, limitações e roteiro de apresentação. Registrar evidências sanitizadas úteis ao texto do TCC.
- Núcleo pronto somente quando há evidência funcional e revisões/checks concluídos; abrir P1 não compensa P0 incompleto.

### Complementos

**FNR-029 — Carteira manual**

- Criar migration, APIs e telas para cadastrar, editar, excluir e listar investimentos do titular.
- Exibir aplicado, atual, diferença e distribuição por classe com persistência; não apresentar variação simples como rentabilidade ajustada a aportes/datas.
- Testar propriedade, validação de valores e conciliação de totais. Não integrar cotações ou operações de compra/venda.

**FNR-030 — Simulador**

- Calcular no backend a projeção do PRD com taxa anual efetiva convertida em mensal e aportes ao fim do mês.
- Exibir série, total aportado e rendimento projetado; validar entradas e explicitar ausência de impostos, taxas e inflação.
- Testar taxa zero e resultado independente com juros. O cenário não altera a carteira real e não promete retorno.

**FNR-031 — CSV**

- Exportar todo o conjunto filtrado autorizado, não apenas a página visível, com datas, moedas e valores coerentes.
- Escapar separadores, aspas e quebras de linha e neutralizar células de texto interpretáveis como fórmulas por planilhas.
- Testar filtros e conteúdo exportado; não expor dados de outro titular ou credenciais.

## 6. Evolução futura — P2

| ID | Evolução | Condição antes de iniciar |
|---|---|---|
| FNR-032 | Permanecer conectado após recarregar | Redesenhar sessão, renovação e armazenamento, preservando revogação; não habilitar localStorage como atalho |
| FNR-033 | Renomear e arquivar categorias | Definir regras de histórico e filtros; não apagar vínculos existentes |
| FNR-034 | Conexão bancária iniciada na interface | Validar fluxo, condições de acesso e consentimentos do provedor |
| FNR-035 | Novos usuários ou instituições | Redefinir escopo, isolamento, condições comerciais e autorização |
| FNR-036 | Cotações e investimentos automáticos | Validar fontes, cobertura, licenças e precisão das métricas |
| FNR-037 | Rotina automática com aplicação fechada | Escolher executor compatível com hospedagem e recuperação; não depender de instância gratuita suspensa |

Todos os itens P2 ficam **fora da entrega inicial** e exigem nova especificação.

## 7. Registro de execução

Ao iniciar uma tarefa, preencher este quadro sem alterar silenciosamente o significado dos critérios de aceite. Revisão explícita de escopo deve preservar critério/resultado históricos e identificar o novo aceite como pendente até confirmação:

| Item | Status | Mudança OpenSpec | Branch / PR | Evidência ou bloqueio |
|---|---|---|---|---|
| FNR-001 | Concluído | [fnr-001-alinhar-documentacao — arquivada em 13/11/2026](../openspec/changes/archive/2026-11-13-fnr-001-alinhar-documentacao/proposal.md) | `docs/fnr-001-alinhar-documentacao`; [PR #1](https://github.com/VictorJer/GestFinan-a/pull/1); merge `075af734571a1a8aa096a718d5f9b133d1078db6` | Documentos e SDD aprovados explicitamente pelo autor nesta conversa em 13/11/2026; revisão do PR confirmada por ele: “ja revisei o PR”. OpenSpec estrito, diff check e oito links locais aprovados. Integração por `VictorJer` em 13/11/2026 às 18:14:07 UTC. Verificação das exigências: main sem proteção clássica, regras aplicáveis ou rulesets na consulta; sem workflows no commit integrado, sem check runs/status contexts no head do PR e sem exigência documental de revisão formal no GitHub. Nenhum bloqueio adicional identificado. Não houve evento formal APPROVED nem checks remotos aprovados; conclusão fundamentada nas evidências aplicáveis, não apenas no merge. Limite histórico da consulta registrado nas tarefas OpenSpec. Arquivada por solicitação do autor, com 11/11 tarefas completas e sem delta specs; sem código ou avanço para FNR-002. |
| FNR-002 | Concluído | [fnr-002-preparar-ambiente](../openspec/changes/archive/2026-11-14-fnr-002-preparar-ambiente/proposal.md) | `chore/fnr-002-preparar-ambiente`; [PR #7 integrado](https://github.com/VictorJer/GestFinan-a/pull/7); merge `a3f8a99cff3fdd51d3af97381cc640377bba7927` | SDD, baseline e diff previamente aprovados; versões locais, EF local, Docker/Compose, links e validações documentais registrados em AMBIENTE.md e tarefas históricas. Integração em 14/11/2026 às 18:44:56 UTC confirmada por consulta ao GitHub. Na execução de FNR-003, o autor confirmou no harness “Revisei e aceito” sobre o conteúdo integrado do PR #7: revisão explícita fora do GitHub, não review formal da plataforma. Consulta atual: reviews, comentários de revisão/discussão e statusCheckRollup vazios; main sem proteção clássica, regras aplicáveis ou rulesets. Nenhum bloqueio adicional identificado; ausência de checks não é check aprovado. Testes integrados continuam com FNR-003/006/019. |
| FNR-003 | Concluído | [fnr-003-criar-base-backend — arquivada em 14/11/2026](../openspec/changes/archive/2026-11-14-fnr-003-criar-base-backend/proposal.md) | Entrega `docs/fnr-003-criar-base-backend`; [PR #8 integrado](https://github.com/VictorJer/GestFinan-a/pull/8); merge `2bd63ae37bbbbf20d8ec90e9a96c08d49933075f`; encerramento em `docs/fnr-003-encerrar` | SDD e TDD 0.3 aprovados separadamente; revisão do diff completo sem problemas que exigissem correção no escopo e aceite explícito do autor registrados em AMBIENTE.md. Restore com --locked-mode aprovado, build sem avisos/erros, 20 testes MTP aprovados, divergência em cópia rejeitada com NU1004 e Swagger Try it out 200/corpo esperado sem erros de console. Cinco locks/assets e contentHash de 47 pacotes conferidos. GitHub confirmou integração por VictorJer em 14/11/2026 às 20:22:31 UTC; árvore do merge idêntica ao head revisado `e038393`. Reviews e statusCheckRollup vazios; zero check runs/status contexts; sem workflows no merge, proteção clássica, regras aplicáveis ou rulesets na consulta atual. Ausência de checks não é aprovação remota nem bloqueio não exigido; nenhum gate adicional não atendido identificado. Duas specs sincronizadas, 14/14 tarefas e mudança arquivada após validação/aceite por solicitação explícita; registros preparados para PR documental, não integrados nesta execução. Sem migration/deploy aplicável e sem avanço para FNR-004. |
| FNR-004 | Concluído — consulta local limitada | [fnr-004-consultar-pluggy-local — arquivada](../openspec/changes/archive/2026-11-15-fnr-004-consultar-pluggy-local/proposal.md) | `feature/fnr-004-consultar-pluggy-local`; [PR #10 integrado](https://github.com/VictorJer/GestFinan-a/pull/10); HEAD revisado `2b32176bdee463f389fc67930d6b2a1ec2aca4d4`; merge `79c15fe83665e3ea4aa1f9c1e427233ce911f479` | Implementação e TDD 0.6 aceitos explicitamente pelo autor em 15/11/2026 (“Aprovo ambos”). Restore locked, build local com 0 avisos/erros, testes locais 58/58, OpenSpec estrito, diff check e 37 links locais aprovados; estes não são checks do GitHub. Prova real sanitizada: 321 transações retornadas em uma página, `has_more=false`, exit 0; comprova somente a leitura observada, não histórico completo, importação persistente nem sincronização futura. No PR não havia review formal nem checks reportados; ausência não equivale a aprovação. Integração confirmada no GitHub em 15/11/2026; sem publicação de aplicação. |
| FNR-005 | Incremento técnico integrado; encerramento revisado aceito; 5.3 não atendida tecnicamente | [fnr-005-validar-contrato-pluggy — arquivada em 16/11/2026](../openspec/changes/archive/2026-11-16-fnr-005-validar-contrato-pluggy/proposal.md) | Entrega `feature/fnr-005-validar-contrato-pluggy`; [PR #12 integrado](https://github.com/VictorJer/GestFinan-a/pull/12), merge `adee6998342722e905a1db98ed1423620ce77ee4`. Alinhamento `docs/revisar-escopo-mvp`; [PR #13 integrado](https://github.com/VictorJer/GestFinan-a/pull/13), merge `2c62109b90ac705f248cc0c7fe9b176ba421d649` (head `ead587082271e347a23772cf31ba611585e458d9`). | Proposta, specs, SDD e aceites parciais históricos preservados. Coletas reais autorizadas de uma página cada: agregados de 319 recebidos/únicos, zero duplicatas, `next=null`; execução de distribuições 597 ms e D7 527 ms, conforme AMBIENTE.md/PLUGGY-CONTRACT.md. O classificador então reconheceu zero compras e 238 desconhecidos; caso individual confirmado não generaliza, formato não valida competência, e buckets históricos de forecast continuam inconclusivos. O GitHub confirmou PR #12 MERGED em 15/11/2026 às 23:47:10 UTC e PR #13 MERGED por `VictorJer` em 16/11/2026 às 05:27:07 UTC. A API não reportou review formal ou checks para o PR #13; a branch `main` não tinha proteção clássica ou rulesets na consulta. O titular aprovou os documentos e aceitou explicitamente o encerramento técnico conservador em 16/11/2026 no harness. As duas delta specs foram sincronizadas em `openspec/specs/pluggy-contract-analysis/spec.md` e `openspec/specs/transaction-normalization/spec.md`; a mudança foi arquivada após 34/34 tarefas completas e validação OpenSpec estrita. O objetivo original 5.3 permanece substituído por revisão de escopo, não tecnicamente atendido; compra/competência e indicadores financeiros ficam adiados. Sem nova coleta, implementação, migration ou publicação. |
| FNR-006 | Escopo técnico confirmado na main; arquivo OpenSpec integrado pelo PR #16 | [fnr-006-postgresql-efcore — arquivada](../openspec/changes/archive/2026-11-17-fnr-006-postgresql-efcore/proposal.md) | Entrega técnica `feature/fnr-006-postgresql-efcore`; [commit c03f0c7](https://github.com/VictorJer/GestFinan-a/commit/c03f0c7275e2725c08201d3f3e246647ed7ab045) confirmado na `main`, sem PR associado. Regularização documental integrada pelo [PR #15](https://github.com/VictorJer/GestFinan-a/pull/15), merge `d8db9f247be294168286f360910b438570a41188`. Arquivamento OpenSpec no commit `7c0b941`, como commit separado do [PR #16 integrado](https://github.com/VictorJer/GestFinan-a/pull/16), merge `6aef0a0` | Aceite local de 16/11/2026 preservado; PostgreSQL 18, EF Core/Relational/Design 10.0.15, Npgsql EF 10.0.3 e migration smoke vazia. Restore/build, 132/132 testes de banco descartável, 4/4 health, reaplicação e OpenSpec estrito passaram na validação registrada. PR #15 alterou somente documentação; a ausência de PR para `c03f0c7` permanece ressalva histórica, sem conformidade retroativa. Arquivada após 11/11 tarefas, sem delta specs. Sem Neon/publicação ou alteração técnica nesta regularização. |
| FNR-007 | Concluída — implementação aceita, spec sincronizada e integrada; 19/19 tarefas | [fnr-007-modelar-persistencia-nucleo — arquivada em 17/11/2026](../openspec/changes/archive/2026-11-17-fnr-007-modelar-persistencia-nucleo/proposal.md) | Branch `feature/fnr-007-modelar-persistencia-nucleo`; commits `7c0b941` (arquivo FNR-006), `25df020` (FNR-007) e `ec3c511` (registro do PR); [PR #16 integrado](https://github.com/VictorJer/GestFinan-a/pull/16), merge `6aef0a0cac5f9c0f7a4591bb88f6ccaba6a72079` | O titular aceitou explicitamente a implementação em 17/11/2026 e aprovou o PR/conteúdo do BACKLOG 0.8 nesta conversa. `financial-data-persistence` foi sincronizada para `openspec/specs/financial-data-persistence/spec.md`; 19/19 tarefas arquivadas. Evidência 4.2 preservada sem repetição. Restore `--locked-mode`, build, MTP 166/166 (classes de migration 4.2 excluídas), HTTP/OpenAPI/Swagger 17/17 e testes PostgreSQL 4.3–5.4 aprovados conforme evidência local; OpenSpec spec estrito 6/6. GitHub: PR integrado; reviews e status checks vazios, sem tratar ausência como aprovação formal; aceite fora da plataforma. `main` atualizada localmente por fast-forward. Sem publicação/deploy ou avanço para FNR-008/009/012. |
| FNR-038 | A especificar — direção aprovada; tolerâncias e mudança próprias pendentes | Ainda não criada; direção registrada no PRD RF17, TDD DT-55 e neste backlog | Sem branch/PR de implementação; somente planejamento em `docs/revisar-escopo-mvp` | Candidato após padrão em três meses consecutivos e confirmação pelo titular; valor igual sozinho não basta, metadados de parcelamento separados. Depende de 012/015/020/022; fechar tolerâncias de valor/datas/descrição antes de implementar. Não inicia nem encerra outra tarefa. |

Adicionar os demais itens conforme entrarem em execução. Um PR aberto não significa tarefa concluída. Manter apenas uma mudança principal em implementação por vez no trabalho individual.

**Registro histórico do encerramento FNR-003 — preservado, não estado atual:** entrega aceita e integrada pelo PR #8; specs consolidadas em `openspec/specs/api-diagnostics/spec.md` e `openspec/specs/api-exploration/spec.md`, com histórico preservado no arquivo acima. A branch `docs/fnr-003-encerrar` continha somente registros documentais, sync e arquivamento; sua integração dependia do PR documental e não seria feita pelo agente naquela execução. Esse registro encerrava o acompanhamento solicitado, sem criar nova mudança apenas para registrar seu futuro merge. Na ocasião, FNR-004 não estava iniciada; seu resultado posterior consta no §7. A escrita do TCC acompanha os marcos desde o início.
