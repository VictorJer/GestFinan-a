# Tasks

## 1. Aprovação e alinhamento canônico

- [ ] 1.1 Conferir FNR-013 no merge local #27 contra a revisão 0.47 do backlog, sem inferir deploy ou checks GitHub; registrar no backlog o estado factual verificado e os gates de publicação pendentes, conferindo hashes e links.
- [ ] 1.2 Preparar revisão do TDD §§6/12/DT-43 e do BACKLOG FNR-014/§7 com o intervalo configurável de cinco minutos compartilhado entre entrada e botão, limiar de 24 horas e fronteira FNR-021; conferir rastreabilidade a RF08/RF16 e preservar aprovações históricas.
- [ ] 1.3 Apresentar proposta, duas deltas, desenho e revisões canônicas para revisão e obter aprovação explícita do escopo e do conteúdo afetado antes de código dependente; registrar a evidência real da aprovação sem tratá-la como review formal GitHub.

## 2. Política persistente de elegibilidade

- [ ] 2.1 Criar opções backend validadas para limiar padrão de 24 horas e intervalo padrão de cinco minutos por conexão; testes com configuração inválida, limiar exato e relógio controlado comprovam o comportamento.
- [ ] 2.2 Integrar modo manual/entrada à aceitação FNR-013 sem quebrar o fingerprint manual v1, com teste de chave antiga, replay terminal/ativo e conflito entre modos, inclusive após resposta perdida.
- [ ] 2.3 Avaliar execução ativa, último sucesso e última intenção aceita sob o lock persistente da conexão antes de inserir `SyncRun`/outbox; testes PostgreSQL concorrentes e após reinício provam que entrada recente não aceita, entrada antiga/manual respeitam intervalo e nunca há duas execuções novas por corrida.
- [ ] 2.4 Bloquear entrada automática repetitiva após `AUTH_REQUIRED`, mantendo pedido manual condicionado ao intervalo e sem afirmar renovação concluída; testes unitários e PostgreSQL comprovam ausência de retry automático e preservação do histórico.
- [ ] 2.5 Documentar a ordem de replay, bloqueio e aceite no contrato de sincronização e atualizar testes da aceitação para comprovar `202` somente após commit e ausência de outbox/SyncRun no bloqueio; conferir que os retries internos da FNR-013 continuam inalterados.

## 3. Contratos HTTP e estado da conexão

- [ ] 3.1 Expor `POST /api/connections/current/sync/on-entry` protegido, mantendo a rota manual; testes HTTP verificam `200 not_required`, `202`, `409` ativo, `429` com `nextAllowedAtUtc`/`Retry-After`, chave inválida e falha de broker/banco sem aceitar falsamente.
- [ ] 3.2 Expor `GET /api/connections/current/sync-state` protegido, derivando último sucesso, tentativa e execução da persistência e mantendo `SourceUpdatedAtUtc` ausente enquanto não houver origem confiável; testes de HTTP/banco exercitam falha após sucesso, cobertura desconhecida, `AUTH_REQUIRED`, dados recentes e ausência de sucesso.
- [ ] 3.3 Testar isolamento de titular, ausência/ambiguidade de conexão, logs/DTOs sanitizados, leitura mesmo com broker indisponível e garantia de que GET, filtros e navegação não criam execuções; documentar códigos e exemplos sintéticos para integração futura da FNR-021.

## 4. Validação integrada da mudança

- [ ] 4.1 Executar restore bloqueado, build e suítes MSTest afetadas (unitárias, HTTP e PostgreSQL descartável) com relógios controlados; registrar resultados reais e limites, sem banco pessoal, Neon ou Pluggy real.
- [ ] 4.2 Validar `openspec validate fnr-014-politica-atualizacao --strict`, links e `git diff --check`, revisar compatibilidade FNR-013 e rastreabilidade PRD/TDD/backlog/deltas, mantendo pendências de limites reais do provedor e MQ-P02/MQ-P04 explícitas.
- [ ] 4.3 Apresentar evidências e critérios para aceite humano e revisão dos artefatos/código da FNR-014; conferir que não há alegação de publicação, migration em destino ou integração FNR-021 sem prova correspondente.

## Workflow follow-up

- Conferir limites efetivos/`Retry-After` do provedor com evidência autorizada e revisar a política se incompatível antes de publicar; sem coleta real por inferência desta proposta.
- Encaminhar commits locais, PR, revisão/integração, sync de specs e arquivamento apenas mediante as autorizações aplicáveis; migration no destino e gates de hospedagem MQ-P02/MQ-P04 continuam antes de deploy dependente.
