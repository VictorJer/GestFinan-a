# Design

**SDD da FNR-001 — aprovado explicitamente pelo autor em 13/11/2026 nesta conversa.** FNR-001 concluída documentalmente com aprovação, revisão confirmada pelo autor, validações locais e PR #1 integrado no commit `075af73`. Nenhuma exigência adicional não atendida foi identificada; não há revisão formal ou checks reportados no GitHub. Evidências e limites constam em `tasks.md`, seção Verificação das exigências de integração. O PR desta branch apenas consolida o registro, sem novo merge, arquivamento ou implementação de funcionalidades futuras nesta etapa.

## Context

Ver `proposal.md`, seção Why. O projeto possui documentos e configuração OpenSpec local, sem specs principais ou aplicação implementada. O schema instalado é `spec-driven`. A mudança é exclusivamente documental e usa `skip_specs: true`; este desenho registra a solução documental, não o desenho aprovado de futuras funcionalidades.

## Goals / Non-Goals

**Goals:**

- Separar requisitos observáveis do produto (PRD), mecanismos técnicos (TDD), resumo (README) e execução/rastreabilidade (backlog).
- Conciliar as decisões confirmadas sem promover propostas técnicas ou declarações de ambiente informado a validações concluídas.
- Oferecer critérios verificáveis de revisão e manter aprovação humana separada do estado dos artefatos no CLI.

**Non-Goals:**

- Não revisar a escolha das tecnologias nem validar compatibilidade, acesso à Pluggy ou condições dos provedores.
- Não implementar autenticação, projetos, testes de aplicação, banco, CI ou deploy.
- A abertura de PR documental foi autorizada após aprovação deste SDD. Não integrar, arquivar a mudança ou iniciar FNR-002 nesta etapa.

## Decisions

### 1. PRD descreve comportamento; TDD conserva mecanismos

O PRD incorpora acesso do titular, categorias pessoais, observações, histórico e atualização já confirmados no TDD. RF01–RF13 mantêm seus IDs; RF14–RF16 tornam acesso, anotações e atualização rastreáveis sem renumerar os requisitos existentes. A antiga direção técnica do PRD é substituída por fronteiras do produto e referência ao TDD.

Alternativa rejeitada: copiar versões, JWT, tabelas e configuração de hospedagem para o PRD. Isso duplicaria contratos técnicos e manteria duas fontes sujeitas a divergência. PostgreSQL local em Docker, Neon, .NET 10, EF Core 10, Angular 22/Node 24 informados, Identity/JWT, MSTest e demais escolhas permanecem no TDD; sua compatibilidade continua pendente de FNR-002.

### 2. Revisão documental não aprova propostas de implementação

O TDD mantém como propostas o modelo relacional, contratos HTTP, processamento da sincronização e regras técnicas de categorias. A prova de conceito local antecede dashboard e hospedagem autenticada, mas não autoriza publicar endpoints desprotegidos.

Alternativa rejeitada: fechar BackgroundService, intervalo de cinco minutos, política de nomes e contratos reais da Pluggy nesta mudança. Essas decisões dependem das validações e SDDs das tarefas futuras.

### 3. Fluxo de integração e publicação com gates explícitos

O TDD §9 passa a exigir branch, PRD → TDD → SDD revisados antes de implementar, testes, PR e integração condicionada às aprovações e checks. Migrations compatíveis continuam administrativas e anteriores à integração que dispara o deploy dependente. Após integrar, o deploy usa somente o commit aprovado pelos checks; uma falha impede publicação. Não há alteração de pipeline nesta tarefa.

Alternativa rejeitada: push direto na main ou deploy nativo paralelo aos checks. Ambos contornariam as condições de revisão e publicação já confirmadas.

### 4. Artefatos proporcionais ao escopo

Usar proposta, este desenho/SDD e tarefas, dispensando deltas de comportamento via `skip_specs: true`. Atualizar apenas status e rastreabilidade afetados no backlog existente, sem recriar seus itens. O README recebe links, status de revisão e prioridade da prova de conceito; o bloco de árvore previsto é fechado para renderização correta.

Alternativa rejeitada: criar uma capability genérica de documentação ou especificar antecipadamente todo o MVP. Não há comportamento novo nesta mudança e os próximos itens precisam de revisão própria.

### Rastreabilidade e critérios de revisão

| Critério | PRD | TDD | Evidência documental esperada |
|---|---|---|---|
| C1 — Contextos local e hospedado sem contradição | §§3, 6, 9; RF14 | §§2, 4–5 | Prova de conceito local privada e MVP hospedado autenticado distintos |
| C2 — Organização pessoal preservada | RF06, RF15; §8 | §§6, 10, 15 | Categorias pessoais e observações sem edição dos dados da origem |
| C3 — Histórico e atualização confirmados | RF02, RF16; §8 | §§1, 6, 12 | Até 12 meses disponíveis, parcelas futuras separadas, prazo de 24h desde sucesso completo |
| C4 — Separação produto/técnica | §§10, 15 | §§1, 3, 7, 9 | Sem SQLite como decisão pendente; escolhas técnicas preservadas no TDD e compatibilidade não declarada validada |
| C5 — Integração e deploy protegidos | §13 | §9 | PR obrigatório, revisão documental independente, migrations antes do deploy dependente e checks do commit publicado |
| C6 — Limites e evidência de conclusão | §§4, 12–15 | §§4, 7–8, 14 | README/backlog coerentes, sem código ou tarefa futura iniciada; aprovação documental, revisão confirmada pelo autor, validações, exigências aplicáveis e integração registradas separadamente, sem inventar evidências no GitHub |

## Risks / Trade-offs

- [Confundir aprovação documental ou integração isoladas com conclusão] → Fundamentar a conclusão no conjunto de aprovação explícita, revisão confirmada pelo autor, validações locais, verificação das exigências aplicáveis e integração, sem inventar revisão formal/checks no GitHub; registrar a limitação histórica da consulta às proteções.
- [Tratar ambiente informado como verificado] → Preservar a validação em FNR-002, sem executar instalação ou fixar pacotes aqui.
- [Confundir resumo com contrato] → README aponta para PRD/TDD; requisitos e mecanismos não são redefinidos no resumo.
- [Backlog previamente não versionado] → Preservar o conteúdo preexistente, alterando apenas coerência e rastreabilidade; o Git mostrará o arquivo inteiro como novo até seu primeiro versionamento.
- [Migration antecede integração que pode não ocorrer] → Manter exigência de compatibilidade com a API ativa; estratégia operacional concreta será revisada na mudança de publicação, não executada agora.

## Migration Plan

Não há migração de banco ou deploy. As alterações ficam na branch `docs/fnr-001-alinhar-documentacao`, com diff e validação OpenSpec apresentados e aprovação documental explícita registrada. O autor autorizou commit e abertura de PR; correções solicitadas na revisão do PR devem manter PRD, TDD, README, backlog e artefatos coerentes. Não fazer merge, arquivar ou iniciar FNR-002 nesta etapa; não contornar checks ou proteções.

Reversão, se solicitada: ajustar somente os trechos desta mudança, preservando o backlog preexistente e qualquer trabalho posterior do usuário; não usar reset ou descarte amplo.
