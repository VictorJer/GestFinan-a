# Design

## Context

Ver [proposal.md](proposal.md). `docs/PRD.md` §12 admite complementos depois de P0 estável, mas ainda trata CSV como condicional; `docs/BACKLOG.md` §§3–5 e `docs/TDD.md` §16 colocam FNR-029–031 após FNR-028, cujo aceite exige publicação. O P0 já tem componentes e CI integrados (FNR-024), mas a evidência local end-to-end com provedor real, aceites FNR-020/021 e M-03 e os gates operacionais/hospedados não podem ser inferidos de testes sintéticos, de PRs mesclados ou de mudanças OpenSpec arquivadas. `main` local contém os merges #47 (`349b207`) e #48 (`9695f0b`), enquanto trechos do backlog/TDD ainda descrevem estados históricos de PR aberto.

## Goals / Non-Goals

**Goals:**

- Dar a P1 um pré-requisito verificável **local**, distinto do aceite da FNR-028 publicada, preservando prioridades, IDs e contratos existentes.
- Delimitar claramente três afirmações distintas: implementação testada localmente, núcleo aceito localmente, e entrega validada após publicação.
- Manter gates de segurança, migrations, checks, revisão humana e documentação coerentes ao mudar a sequência.

**Non-Goals:**

- Rodar a validação P0, conectar Pluggy/RabbitMQ/Neon, provisionar dados, executar migration, publicar ou implementar FNR-029–031 nesta mudança.
- Declarar pronto o núcleo, aprovar revisões históricas ou redefinir a semântica financeira do produto.

## Decisions

### D1 — Gate local documentado sem novo ID funcional

Registrar um checkpoint de passagem P0→P1 em BACKLOG §§3/5/7 e TDD §16, rastreado a PRD §13: verificar autenticação/revogação, provisionamento, importação paginada e repetida com persistência, falhas/retomada, lista/filtros/contagens, anotações após nova importação, cobertura e recorrência; usar dados reais apenas em ambiente autorizado e evidência sanitizada, mais cenários controlados onde a origem real não oferece casos de borda. Exigir aceite explícito do escopo local e manter pendências sem evidência como bloqueios. Uma validação sintética ou resultado CI não substitui integração real exigida pelo PRD; se não houver acesso autorizado/viável, registrar o bloqueio e discutir revisão específica, não presumir aprovação. Alternativa rejeitada: marcar FNR-028 concluída antes da hospedagem, o que tornaria seu critério falso.

### D2 — Reordenar dependências, não renumerar entregas

FNR-029, FNR-030 e FNR-031 passam a depender do checkpoint P0 local, e não de FNR-028; executá-las uma por vez, com OpenSpec e aceites próprios. Manter seus contratos independentes onde aplicável (o simulador não altera a carteira), sem inventar dependência funcional entre elas. FNR-024 continua como CI existente; FNR-025 prepara imagem/health quando chegar o gate operacional, sem deploy; FNR-026/027 publicam somente após P1 aceito e gates próprios; FNR-028 fica por último para verificar **P0 e P1 publicados**, conservando seus pré-requisitos anteriores e adicionando FNR-029–031. Alternativa rejeitada: criar novos IDs duplicados para validação/local e duplicar os critérios da FNR-028. Os rótulos M5/M6 e suas referências cruzadas serão reconciliados sem reescrever o histórico de execução.

### D3 — Alinhamento canônico e evidência factual

Revisar `docs/PRD.md` §12 somente para afirmar os três complementos P1 antes da primeira publicação escolhida pelo titular; não alterar RF10–RF13 nem transformar P1 em P0. Atualizar backlog/TDD nas referências que ainda dizem “P1 depois de FNR-028” (inclusive marcos MR-3/MQ-3 e matriz pertinente); preservar textos históricos de revisões e registrar a nova como pendente até aprovação. Registrar os merges #47/#48 com prova do grafo local, não como checks/reviews recentes ou publicação. Alternativa rejeitada: alterar apenas uma linha de dependência, deixando o roadmap e critérios contraditórios.

## Risks / Trade-offs

- [Prova local real não disponível ou dependências operacionais não resolvidas] → manter checkpoint bloqueado e identificar a evidência faltante; não abrir P1 apenas por contagem de tarefas.
- [P1 ampliado antes de estabilizar P0 atrasa a entrega] → executar FNR-029–031 separadamente após aceite local; reavaliar capacidade e preservar opção de nova decisão explícita de escopo, sem declarar completude antecipada.
- [P1 introduz migrations e dados próprios sem backup hospedado] → validar migrations localmente nas mudanças individuais; conservar preparação de backup, compatibilidade, aplicação administrativa em destino e recuperação para FNR-026/028, sem migration automática na API.
- [Correções documentais confundidas com aceite/integração] → diferenciar evidência local, aprovação humana, checks GitHub e deploy em cada marco; preservar revisões anteriores e seus estados.

## Migration Plan

1. Revisar e aprovar os ajustes canônicos de PRD/BACKLOG/TDD em branch de planejamento; conferir links, diff e OpenSpec estrito. Integrar documentação por PR após gates aplicáveis, sem publicar.
2. Em mudança(s) futura(s), executar e registrar o checkpoint P0 local com ambiente autorizado e evidências reais/sintéticas identificadas; obter aceite próprio. Esta etapa não é executada por este plano documental.
3. Preparar e aprovar separadamente FNR-029, FNR-030 e FNR-031, implementar/validar uma por vez. Só então tratar FNR-025–027 e, após a publicação autorizada, FNR-028.

Se o replanejamento documental não for aceito, descartar apenas esta revisão por decisão explícita e manter os gates canônicos atuais; nenhum rollback de aplicação ou esquema é aplicável a esta mudança de planejamento.
