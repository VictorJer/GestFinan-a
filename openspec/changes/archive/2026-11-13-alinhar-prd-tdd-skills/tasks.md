# Tasks

**Estado:** 18/18 tarefas de aplicação documental concluídas; aprovação documental de PRD 0.3, TDD 0.1 e SDD confirmada posteriormente pelo autor em 13/11/2026. Ver [proposta](proposal.md) e [SDD](design.md); delta specs dispensados por `skip_specs: true`. O pedido de aplicação não foi tratado como aprovação: esta foi recebida em manifestação separada no harness. Pendências técnicas preservadas, sem autorização de código, PR, merge, publicação ou arquivamento.

## 1. Revisão e preparação documental

- [x] 1.1 Apresentar proposta/SDD ao autor, registrar apenas manifestações efetivamente recebidas e confirmar solicitação para aplicar; verificar evidência explícita antes de editar os documentos canônicos, sem inventar aprovação.
- [x] 1.2 Conferir estado da feature branch e versões atuais de PRD/TDD/backlog/skills; verificar que não há conflito com trabalho posterior e registrar base de comparação para RF01–RF16, decisões e histórico.

## 2. PRD — revisão pendente e requisitos rastreáveis

- [x] 2.1 Atualizar cabeçalho e histórico para revisão 0.3 em revisão, com origem/data e aprovação pendente; verificar que aprovação de 0.2/FNR-001 e suas evidências continuam claramente históricas.
- [x] 2.2 Acrescentar RNF-01–RNF-10 à tabela de §9 e encaminhamento de sua verificação; verificar correspondência com as dez áreas originais e ausência de mudança de significado.
- [x] 2.3 Acrescentar métricas com definição, método proposto, baseline, meta/critério e estado; verificar vínculos com requisitos existentes, preservar meta local de 2s/5.000 e manter valores desconhecidos a definir, sem resultados fabricados.
- [x] 2.4 Explicitar stakeholders e jornadas alternativas pertinentes e estruturar pendências com impacto, responsável sugerido, evidência/ação e condição de validação; verificar que não foram introduzidos comportamentos, atribuições aceitas ou prazos novos.
- [x] 2.5 Comparar literalmente as linhas RF01–RF16 com a base, revisar regras e prioridades e conferir referências PRD → TDD/backlog; verificar que RFs e critérios permanecem intactos e os links locais existem.

## 3. TDD — resumo e registro de decisões

- [x] 3.1 Identificar revisão documental 0.1 em revisão e base PRD 0.3 pendente, preservando histórico v0/FNR-001; verificar que nenhuma aprovação histórica foi transferida ao novo texto.
- [x] 3.2 Acrescentar resumo técnico e diagrama Mermaid de componentes com legenda de arquitetura planejada; verificar responsabilidades, fronteiras e restrições contra §§1–3 e PRD, sem afirmar implantação real.
- [x] 3.3 Centralizar DT-xx com estado, origem/decisor, justificativa, alternativas e consequências, cobrindo escolhas existentes e propostas relevantes; verificar cada linha da tabela original e distinguir justificativa não registrada de análise nova.
- [x] 3.4 Referenciar DTs nas seções de detalhe e conferir estados de modelos, contratos, assinatura, sincronização e categorias; verificar que escolhas aprovadas foram preservadas e propostas não viraram decisões confirmadas.

## 4. TDD — roadmap, operação e rastreabilidade

- [x] 4.1 Acrescentar roadmap incremental com fases, dependências, entregáveis, gates, responsáveis sugeridos e RF/RNF/DTs; verificar correspondência FNR-001–031 e dependências individuais do backlog, sem datas inventadas ou avanço de status.
- [x] 4.2 Registrar operação/recuperação incluindo segredos, observabilidade, backup/restore, retenção, rollout e rollback; verificar separação entre confirmado, proposto e desconhecido e impacto/responsável sugerido/condição para cada pendência, sem promessas de plano ou política nova.
- [x] 4.3 Consolidar riscos e alternativas com mitigação, vínculo às decisões/fases e condições de resolução; verificar que lacunas não bloqueantes permanecem explícitas e os gates de publicação existentes não foram contornados.
- [x] 4.4 Acrescentar matriz RF/RNF → DT → componente → verificação → FNR; verificar cobertura de RF01–RF16 e RNF-01–RNF-10, distinção P0/P1 e ausência de IDs ou referências órfãos.

## 5. Coerência integrada e entrega para revisão

- [x] 5.1 Revisar a cobertura de todas as seções obrigatórias das duas skills e a coerência PRD/TDD/SDD/backlog; verificar preservação das referências numéricas existentes e do histórico FNR-001, sem iniciar FNR-002.
- [x] 5.2 Validar links relativos, `git diff --check` e `openspec validate alinhar-prd-tdd-skills --strict`; registrar resultados reais e limitações, sem alegar testes funcionais ou validar automaticamente links externos não consultados.
- [x] 5.3 Apresentar diff integral das revisões e resumo de propostas/pendências ao autor; verificar que apenas documentação autorizada mudou, revisão segue pendente e não houve código, aprovação, merge ou publicação.

## Workflow follow-up

### Aprovação documental posterior à entrega

Em 13/11/2026, após a entrega das revisões e do diff, o autor escolheu no harness **“Registrar minha aprovação”**, confirmando a aprovação documental de PRD 0.3, TDD 0.1 e SDD com as pendências técnicas explícitas. Em seguida escolheu **“Confirmar registros”**, autorizando atualizar os estados/históricos de PRD/TDD e os registros de proposta, SDD e tarefas. Essa aprovação é do autor, não uma autoaprovação do assistente nem uma revisão formal no GitHub.

Somente metadados de estado, histórico e coerência da aprovação foram atualizados; RF01–RF16, RNFs, decisões, propostas, métodos, roadmap e pendências técnicas permanecem inalterados. O checklist abaixo/precedente e as evidências de entrega registram o momento anterior à aprovação: suas menções a revisão pendente/ausência de aprovação são históricas, não o estado atual. Não houve autorização de código, PR, merge, publicação ou arquivamento. FNR-002 continua não iniciada. A documentação aprovada ainda não foi integrada à main.

### Evidências da aplicação e entrega, anteriores à aprovação

Base de comparação desta aplicação: `9cbd5b5a9906c034931d2f3b67963eb50b45d418`, branch `docs/alinhar-prd-tdd-skills`. PRD/TDD/backlog/README estavam idênticos à base no início; os únicos arquivos não rastreados eram os artefatos desta mudança. Skills atuais, documentos e instruções do projeto relidos em 13/11/2026. Nenhum conflito posterior identificado.

Cobertura revisada das skills (13/11/2026):
- PRD: cabeçalho/histórico; resumo §1; contexto/problema §§2/4; objetivos/métricas §5; público/stakeholders §3; escopo §§5/14; jornadas §6; RFs/aceites §7/13; RNFs §9; dados/integrações §§8/10; riscos/dependências §11; questões/próximos passos §15. Ciclo de vida/recuperação desconhecidos remetidos às pendências operacionais do TDD, sem política inventada.
- TDD: cabeçalho/fontes/histórico; resumo técnico; DTs §1; arquitetura §§2/3; dados/ciclo de vida §10; interfaces/fluxos §§4–6/11–13; segurança/operação §§5/9/17; qualidade §14; roadmap §16; riscos/alternativas/pendências e matriz §§18/19.
- Mantidas as seções numeradas originais do PRD e TDD e dependências individuais FNR-001–031 do backlog. README/backlog continuam registrando a aprovação histórica 0.2/FNR-001, não a aprovação das novas revisões; não foi necessário alterá-los. FNR-002 não iniciada. Protocolo de desempenho e operação novos estão identificados como propostas/pendências, não critérios incorporados tacitamente ao backlog.

Validações documentais executadas em 13/11/2026:
- Comparação em UTF-8 com a base: RF01–RF16 e regras de negócio do PRD literalmente preservados (desconsiderando apenas terminação CRLF/LF); os dez textos originais dos RNFs e as 37 linhas de escolhas/contexto do TDD foram preservados.
- Registro DT-01–DT-47 sem duplicação/ID explícito órfão; matriz com 16 RFs e dez RNFs; referências FNR existentes no backlog. Tabelas com quantidade consistente de colunas e seções numeradas originais presentes. Dependências do roadmap conferidas com backlog §4, sem modificar esse arquivo.
- 31 links locais existentes nos documentos e artefatos da mudança. Links externos preservados, sem consulta HTTP ou alegação de disponibilidade/atualidade nesta revisão; referências numéricas revisadas, sem âncoras novas.
- `git diff --check` aprovado na configuração do repositório; artefatos ainda não rastreados verificados adicionalmente com `git diff --no-index --check` contra NUL. Git avisa conversão LF/CRLF; não foi alterada configuração de terminação de linha. A tentativa adicional com `core.autocrlf=false` mostrou CRLF como whitespace, não diferença de requisitos; a checagem aplicável foi repetida com a configuração efetiva e passou.
- `openspec validate alinhar-prd-tdd-skills --strict` aprovado, com `skip_specs: true`. Nenhum build/teste funcional, integração externa, validação de versões/compatibilidade ou procedimento operacional foi executado. Nenhuma aprovação inferida dessas verificações.
- Diff integral de PRD/TDD gerado e apresentado nesta conversa em `C:\Users\victo\AppData\Local\Temp\opencode\alinhar-prd-tdd-skills.diff` (arquivo temporário fora do repositório): 325 adições e 54 remoções. Resumo e pendências apresentados; o diff também pode ser reproduzido com `git diff -- docs/PRD.md docs/TDD.md` enquanto as alterações não forem commitadas. Apenas PRD, TDD e artefatos desta mudança foram alterados; nenhum código, commit, PR, merge, publicação ou aprovação realizado. Encerramento do checklist não equivale a aprovação, integração ou arquivamento.

- Código, PR, integração, publicação e arquivamento não estão autorizados nesta etapa. A aprovação documental posterior está registrada acima; os demais passos continuam sujeitos a solicitações e verificações próprias.
- Eventuais revisões solicitadas devem atualizar os artefatos afetados antes de avançar; preservar RFs, decisões herdadas e evidências históricas.
