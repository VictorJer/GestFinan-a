# Design

**SDD documental — aprovado documentalmente pelo autor em 13/11/2026, com pendências técnicas preservadas.** Autor/decisor conhecido: autor do TCC; elaboração assistida. Após a apresentação do diff, o autor confirmou no harness “Registrar minha aprovação” para PRD 0.3, TDD 0.1 e SDD e confirmou os registros correspondentes. Não autoriza código, PR, merge, publicação ou arquivamento.

**Acompanhamento da aplicação:** após a apresentação destes artefatos, o autor solicitou aplicar as tarefas nesta conversa. Essa autorização permite preparar os documentos para revisão, sem aprovar este SDD ou as novas revisões. O plano de migração abaixo descreve a sequência entre preparação e aplicação, não uma proibição de prosseguir após essa nova solicitação. O estado efetivo é acompanhado em [tasks.md](tasks.md).

A aprovação registrada no cabeçalho é posterior à aplicação e independente dela. O autor aprovou o SDD como desenho da mudança documental, mantendo os desconhecidos, métodos propostos e validações nos estados registrados; não aprovou políticas operacionais inexistentes nem declarou a aplicação implementada.

## Context

Ver [motivação e escopo](proposal.md). Fontes lidas: [PRD 0.2](../../../docs/PRD.md), [TDD v0/FNR-001](../../../docs/TDD.md), [README](../../../README.md), [backlog](../../../docs/BACKLOG.md), [AGENTS.md](../../../AGENTS.md) e as skills [generate-prd](../../../.opencode/skills/generate-prd/SKILL.md) e [generate-tdd](../../../.opencode/skills/generate-tdd/SKILL.md). Não foi encontrado `docs/entrevista.md` no inventário de documentos; não há nova entrevista a inventar.

O workspace estava limpo na `main` antes da criação de `docs/alinhar-prd-tdd-skills`. A FNR-001 já foi aprovada e arquivada; sua conclusão não será reaberta. A nova mudança não recebe um ID FNR arbitrário e não ocupa FNR-002. O SDD é necessário para resolver a organização transversal e o tratamento dos estados documentais antes de editar os documentos canônicos.

## Goals / Non-Goals

**Goals:**
- Complementar os documentos canônicos sem criar versões concorrentes, mantendo a numeração das seções existentes para preservar referências do backlog.
- Separar aprovação histórica, decisões herdadas, propostas técnicas e desconhecidos; permitir revisão de todas as adições.
- Tornar verificável a cobertura das skills, RF01–RF16, RNFs e marcos do backlog, sem afirmar validações funcionais inexistentes.

**Non-Goals:**
- Escolher novamente tecnologias, validar APIs/versões, implementar componentes ou executar PoC, provisionamento, migrations, deploy, PR ou merge nesta preparação.
- Criar metas numéricas, níveis de serviço, políticas operacionais ou aprovação tácita. Desenho pronto não é autorização de produção.

## Decisions

### D1 — Revisões e preservação

Desenho aplicado: PRD 0.3 e TDD v0 revisão documental 0.1 foram entregues `Em revisão — aprovação pendente`, com histórico contendo versão/data/origem/resumo/estado. Após a confirmação posterior do autor, os cabeçalhos/históricos registram aprovação documental própria de ambas as revisões e deste SDD. O TDD referencia PRD 0.3 aprovado documentalmente, mantendo a base anterior 0.2 no histórico. A aprovação FNR-001, PR #1 e commit `075af73` permanecem evidência da revisão anterior, não da nova. Estados de propostas e validações técnicas não são promovidos a confirmados.

Alternativa rejeitada: sobrescrever o estado aprovado ou criar novos arquivos PRD/TDD; isso confundiria aprovação e referências. Manter §§1–16 do PRD e §§1–15 do TDD; usar subseções e seções adicionais. Preservar literalmente a tabela RF01–RF16, as decisões confirmadas e o relato histórico, contextualizando-o como histórico quando necessário.

### D2 — PRD: RNFs e medição

Acrescentar IDs, sem mudar o requisito correspondente:

| ID proposto | Área existente |
|---|---|
| RNF-01 | Credenciais |
| RNF-02 | Privacidade |
| RNF-03 | Acesso |
| RNF-04 | Persistência |
| RNF-05 | Integridade |
| RNF-06 | Uso |
| RNF-07 | Responsividade |
| RNF-08 | Desempenho |
| RNF-09 | Falhas externas |
| RNF-10 | Operação |

Em §5, adicionar tabela de indicadores com requisito/objetivo, definição, método, baseline, meta/critério e estado. Propostas de medição, não resultados já observados:
- Integração: executar PoC reproduzível com evidência sanitizada e cobertura/paginação documentadas (RF01/RF02).
- Integridade: comparar identidades, contagens e anotações antes/depois de duas importações e atualização controlada (RF03/RF06/RF15).
- Conciliação: comparar total da listagem elegível e dashboard com conjunto independente, mesmos filtros e BRL (RF05/RF07).
- Sessão e recuperação de falhas: executar os cenários já exigidos por RF08/RF09/RF14/RF16, registrando resultado por cenário.
- Desempenho: preservar a meta local existente de até 2 segundos para até 5.000 transações; propor medição separada de consultas locais e chamada externa, com ambiente, amostra e estatística ainda a validar. Não inventar percentil ou SLA hospedado.
- Benefício ao titular: eventual redução do esforço de transcrição pode ser medida por comparação de uma tarefa equivalente com/sem Finora; baseline e meta `A definir`, método proposto para revisão, sem coleta de telemetria automática presumida.

Baselines permanecem `Não medido / A definir`; critérios existentes não viram resultados. Incluir stakeholders conhecidos (titular/autor e professor como interessado acadêmico, sem presumir aprovação deste). Acrescentar encaminhamento de jornadas alternativas/erro com referências aos RFs existentes, sem novo comportamento.

Em §15, manter as pendências atuais e acrescentar colunas ID, impacto, responsável sugerido, ação/evidência, condição/marco e referência ao backlog. Responsáveis são sugestões ao autor, não atribuições aceitas; prazo acadêmico depende de confirmação com o professor. Novas lacunas documentais de métricas e operação remetem às pendências do TDD.

Alternativa rejeitada: criar métricas ou requisitos novos para preencher a skill. A abordagem usa critérios já aprovados e marca o restante como proposta/pendência.

### D3 — TDD: registro central DT-xx e rastreabilidade

Converter a tabela de §1 em registro rastreável: ID estável `DT-01` em diante, tema, decisão, estado, origem/decisor, justificativa, alternativas e consequências. Cobrir cada linha original, sem perda de conteúdo; linhas que só descrevem contexto (conhecimento de Docker) devem ser identificadas como restrição/contexto, não nova decisão arquitetural. A evolução de sessão é intenção futura confirmada, com mecanismo pendente, não decisão de implementar no MVP.

As decisões herdadas referenciam TDD v0/FNR-001 e autor conforme registro; justificativas históricas ou alternativas não registradas ficam explicitamente `Não registradas`. Uma análise nova de trade-offs pode ser apresentada como análise desta revisão, não como fala ou decisão histórica do autor. SQL Server, xUnit e Angular Material só são tratados como alternativas anteriores onde o documento já registra sua substituição.

Propostas relevantes de §§3, 5, 10–12 e 15 (dependências entre camadas, detalhes de assinatura, modelo/índices, contratos, executor/retomada, intervalo manual e normalização de categorias) também recebem DTs com estado proposto/pendente, conforme a fonte. Referenciar DTs nas seções de detalhe; a tabela central não deve promover uma proposta a confirmada. Não afirmar compatibilidade nova de bibliotecas; isso continua em FNR-002.

Adicionar matriz com RF/RNF, DTs correspondentes, componentes, verificação e FNR, cobrindo os 16 RFs e os dez RNFs. Não mapear todos os requisitos genericamente ao sistema inteiro: distinguir autenticação, integração/importação, transações/anotações, dashboard, interface, investimentos/simulador/CSV e operação.

Alternativa rejeitada: usar apenas o backlog como rastreabilidade; a skill exige que o próprio TDD permita revisar decisões, requisitos e verificações.

### D4 — Resumo e roadmap incremental no TDD

Adicionar resumo técnico com objetivo, restrições (autor único, prazo a confirmar, planos escolhidos sujeitos a limites), atributos de qualidade e fronteiras P0/P1/P2. Manter os detalhes existentes como fonte do desenho; incluir diagrama Mermaid de componentes com legenda distinguindo arquitetura planejada de implantação real, sem substituir nem contradizer a visão atual.

Roadmap explícito, sem datas novas; referência primária de execução é a sequência do backlog:

| Fase | Itens existentes | Entrega/critério de passagem planejado |
|---|---|---|
| Preparação | FNR-001–003 | Histórico 001 preservado; ambiente validado e backend mínimo compilando antes da PoC |
| Viabilidade | FNR-004–005 | Leitura real local e contrato/normalização documentados; dados fictícios não fecham o marco |
| Persistência e acesso | FNR-006–011 | Banco local/migrations, comandos administrativos e sessão/revogação verificados |
| Dados utilizáveis | FNR-012–018 | Importação repetível, falhas parciais e reconexão tratadas, anotações preservadas e totais conciliados |
| MVP integrado local | FNR-019–023 | Login, sincronização, lista e dashboard integrados; sem autorização automática de publicação |
| Entrega hospedada P0 | FNR-024–028 | Checks, operação, migrations/publicação compatíveis e demonstração verificados, sujeitos às pendências operacionais |
| Complementos P1 | FNR-029–031 | Carteira, simulador e CSV validados após estabilidade P0; não compensam núcleo incompleto |

Cada fase deve explicitar pré-requisitos, RF/RNF/DTs, entregáveis, evidência de saída, validações bloqueantes e responsável sugerido. Os grupos não alteram dependências individuais: por exemplo FNR-006 depende de 003, sem inventar dependência obrigatória de toda a fase anterior. Preservar as janelas do PRD como sugestões históricas, não calendário renovado; explicitar que o backlog detalha os marcos resumidos do PRD. Produção comercial e P2 não se tornam novas fases aprovadas.

Alternativa rejeitada: transformar §9 (processo de PR/deploy) em roadmap ou apenas inserir um link para o backlog. O roadmap deve existir no TDD e distinguir MVP funcional de publicação autorizada.

### D5 — Operação, recuperação e riscos sem políticas fictícias

Acrescentar seção com situação conhecida, proposta quando útil, desconhecido, impacto, responsável sugerido, condição de validação e FNR relacionado:
- Segredos/configuração por ambiente, chaves e revogação: herdar decisões existentes; geração/rotação e proteção operacional a validar em FNR-025/026.
- Logs e monitoramento: herdar logs sanitizados e health mínimo; ferramenta, alertas, retenção e rotina de acompanhamento `A definir` pelo autor antes da validação operacional FNR-028.
- Backup/restore: capacidades e limites do plano Neon não foram validados; não prometer PITR, frequência, retenção, RPO ou RTO. Registrar risco de perda e necessidade de decidir/testar restauração isolada antes de considerar a entrega operacional validada; sugestão de encaminhamento para FNR-026/028, sem alterar silenciosamente seus critérios no backlog.
- Retenção: duração de histórico, logs, sessões revogadas e execuções `A definir`; não supor exclusão automática ou retenção indefinida. Documentar impacto de privacidade e necessidade de decisão antes de implementar descarte ou declarar política concluída.
- Rollout: manter checks do commit, migrations administrativas compatíveis e publicações separadas já descritos no §9.
- Rollback: propor avaliar retorno ao último artefato compatível e eventual correção à frente; procedimento concreto depende do esquema/contratos e aprovação da mudança. Não prometer downgrade automático de banco. Restore de backup não equivale a rollback seguro; perdas possíveis devem ser avaliadas antes de execução.
- Disponibilidade/recuperação: manter tolerância à retomada e retomada idempotente como desenho proposto; limites reais de planos, timeout, retries e intervalo continuam pendentes de validação, sem inventar disponibilidade garantida.

Consolidar riscos técnicos, alternativas e pendências com impactos/mitigações e vínculos às fases/DTs; distinguir requisito existente de recomendação desta revisão. Perguntar somente se surgir conflito que impeça preservar o comportamento aprovado. As lacunas acima não bloqueiam a produção de uma revisão em revisão.

## Risks / Trade-offs

- [Novas tabelas parecerem novas aprovações] → marcar estado da revisão e de cada decisão; não transferir a aprovação FNR-001.
- [Perda de RFs ou significado ao reorganizar] → comparação literal da tabela RF01–RF16 contra a base e revisão das decisões/regras, além de diff completo.
- [Links e referências numéricas quebrarem] → preservar seções existentes; validar caminhos relativos e referências ao backlog e aos artefatos históricos.
- [Propostas virarem políticas de operação] → registrar desconhecidos, responsável sugerido e condição de validação; não inventar compromissos ou alterar o backlog implicitamente.
- [Roadmap parecer nova autorização ou prazo] → fases com critérios, sem datas novas; 001 continua concluída, 002 não iniciada e publicação depende dos gates aplicáveis.
- [Volume documental] → tabelas rastreáveis e referências aos detalhes existentes, sem duplicar contratos e regras extensamente.

## Migration Plan

1. Apresentar os artefatos desta mudança para revisão, sem editar os documentos canônicos nesta execução.
2. Após nova solicitação para aplicar e as revisões necessárias, atualizar primeiro PRD e depois TDD, mantendo coerência PRD → TDD → SDD e histórico; se a revisão alterar o desenho, atualizar os artefatos antes de prosseguir.
3. Validar RFs, RNFs, DTs, roadmap, matriz, links, coerência com backlog e `git diff --check`; executar validação OpenSpec estrita. Evidência documental não substitui testes futuros da aplicação.
4. Apresentar diff integral e resumo de pendências, mantendo a nova revisão pendente até manifestação explícita. Essa apresentação foi realizada; a aprovação documental posterior do autor está registrada no cabeçalho. Nenhum código ou merge; PR somente em etapa autorizada e sem contornar gates.
5. Não há migração de banco ou rollout de aplicação nesta mudança. Para desfazer ajustes documentais próprios, propor patch reverso delimitado, preservando trabalho de terceiros e a aprovação histórica; não usar reset destrutivo.

## Open Questions

Não há lacuna bloqueante para a revisão documental. Metas/baselines desconhecidos, prazo acadêmico e políticas operacionais permanecem registrados como pendências, não resolvidos por suposição. Responsável sugerido: autor; condições e encaminhamentos conforme D2/D5 e FNR-002, 004, 005, 025, 026 e 028. A aprovação documental foi confirmada posteriormente pelo autor; não substitui essas validações ou autoriza código, integração ou publicação.
