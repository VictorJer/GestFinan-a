# Proposal

**Estado:** alinhamento documental aplicado; aprovação documental de PRD 0.3, TDD 0.1 e SDD confirmada pelo autor em 13/11/2026.\
**Data:** 13 de novembro de 2026.\
**Origem:** solicitação do autor para alinhar os documentos às skills atuais `generate-prd` e `generate-tdd`. Não autoriza código, merge ou aprovação da nova revisão.

**Registro posterior:** após apresentação dos artefatos, aplicação e entrega do diff, o autor confirmou “Registrar minha aprovação” no harness para PRD 0.3, TDD 0.1 e SDD; confirmou também atualizar proposta, SDD e tarefas para refletir essa aprovação. A proibição de presumir aprovação no pedido inicial foi respeitada: a evidência é essa manifestação posterior, não a geração dos arquivos. Pendências técnicas e propostas permanecem explícitas; não há autorização para código, PR, merge, publicação ou arquivamento.

## Why

O [PRD](../../../docs/PRD.md) e o [TDD](../../../docs/TDD.md) preservam o escopo aprovado da FNR-001, mas não atendem integralmente à estrutura das skills atuais. A complementação deve melhorar rastreabilidade e explicitar lacunas sem transformar propostas em decisões ou reabrir escolhas confirmadas.

## What Changes

- Preparar PRD revisão 0.3 e TDD revisão documental 0.1 inicialmente **Em revisão — aprovação pendente**, preservando a aprovação do PRD 0.2 e do TDD v0/FNR-001 exclusivamente para o conteúdo anterior. Após entrega e confirmação explícita do autor, registrar aprovação documental própria, sem fechar pendências técnicas.
- PRD: identificar os dez RNFs existentes como RNF-01–RNF-10, preservando seu significado; acrescentar métricas com método de medição e encaminhamento das pendências.
- Preservar literalmente RF01–RF16 e seus critérios, prioridades e regras de negócio confirmadas.
- TDD: acrescentar resumo técnico, registro DT-xx com estado/origem/justificativa/alternativas/consequências, roadmap incremental, matriz de rastreabilidade, riscos e operação/recuperação.
- Registrar desconhecidos como pendências, com impacto, responsável sugerido e condição de validação; não estabelecer novas metas, baselines, prazos, políticas de retenção/backup ou aprovações.
- Conferir links e coerência com o [backlog](../../../docs/BACKLOG.md), preservando IDs, dependências, evidências e conclusão histórica da FNR-001. Não iniciar FNR-002.

## Capabilities

### New Capabilities

Nenhuma. Mudança exclusivamente documental; `skip_specs: true`, sem criar requisitos artificiais do produto.

### Modified Capabilities

Nenhuma. RF01–RF16 e o comportamento do produto permanecem inalterados. O inventário `openspec list --specs` não contém specs principais nesta data.

## Impact

- Alvos da aplicação posterior: `docs/PRD.md` e `docs/TDD.md`; referências ao backlog sem alterar sua sequência. Ajustes externos somente se necessários para corrigir links/estado, após identificar sua necessidade na revisão.
- Artefatos desta preparação: metadados, proposta, [SDD](design.md) e [tarefas](tasks.md), sem delta specs.
- Nenhuma alteração de código, API, dependência, banco, CI/CD ou hospedagem. Não executar integrações reais nem consultar credenciais.
- Sequência realizada: planejamento e diff → nova solicitação de aplicação → revisão documental aplicada e diff apresentado → aprovação documental explícita do autor. Nenhuma dessas etapas substitui validação funcional ou autoriza implementação de código ou integração.
