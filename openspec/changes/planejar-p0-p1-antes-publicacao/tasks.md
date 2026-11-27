# Tasks

## 1. Alinhar prioridade de produto

- [x] 1.1 Revisar `docs/PRD.md` §12 e histórico da revisão: registrar carteira, simulador e CSV como pacote P1 anterior à primeira publicação neste recorte, preservando RF10–RF13, P0 e a aprovação de 0.5; conferir texto e diff para garantir que nenhum critério funcional foi alterado nem a revisão nova foi declarada aprovada.

## 2. Reordenar o backlog e definir a passagem local

- [x] 2.1 Atualizar `docs/BACKLOG.md` §§3–5 com checkpoint P0 local rastreado ao PRD §13 e evidência/aceite exigidos; conferir que casos reais autorizados e casos sintéticos estão diferenciados e que FNR-021/M-03 e MQ não são encerrados sem prova.
- [x] 2.2 Ajustar em `docs/BACKLOG.md` dependências e ordem de FNR-025–031: P1 antes da primeira publicação, FNR-028 após FNR-029–031 e FNR-024 já integrada; conferir grafo sem ciclo, IDs preservados e migrations/checks/recuperação ainda como gates do destino.
- [x] 2.3 Acrescentar registro factual prospectivo em `docs/BACKLOG.md` §7 para #47/#48 e para a revisão do plano, com prova por `git log`/grafo local e bloqueios/aceites ainda reais; conferir que históricos anteriores não foram sobrescritos e que merge não virou aprovação ou deploy.

## 3. Reconciliar o desenho e as referências técnicas

- [x] 3.1 Atualizar `docs/TDD.md` §16 (marcos, saídas, MR-3 e referências correlatas) para P0 local → P1 → hospedagem/FNR-028, distinguindo critérios locais e publicados; conferir vínculos com BACKLOG §§3–5 e PRD §§12–13, sem inventar validação de provedor ou liberar risco MQ de produção.
- [x] 3.2 Conferir e ajustar no `docs/TDD.md` as demais afirmações atuais de P1 “após FNR-028”, sem reescrever revisões históricas; registrar revisão documental própria como pendente e verificar que procedimentos de migration administrativa, checks e recuperação permanecem intactos.

## 4. Verificação integrada do plano

- [x] 4.1 Revisar a matriz PRD/BACKLOG/TDD e os destinos dos links Markdown alterados; comprovar que P1 exige aceite P0 local, FNR-028 continua hospedada e FNR-029–031 não foram declaradas executadas nem receberam specs de comportamento artificiais.
- [x] 4.2 Executar `openspec validate planejar-p0-p1-antes-publicacao --strict` e `git diff --check`, incluindo conferência dos arquivos não rastreados; registrar resultados e limitações documentais sem afirmar testes de aplicação, publicação ou aceite do titular.

### Evidências da verificação final — 27/11/2026

- `openspec validate planejar-p0-p1-antes-publicacao --strict`: válido; `skip_specs: true` reconhecido, sem deltas de comportamento.
- `git diff --check`: sem erros de whitespace; somente avisos de conversão LF→CRLF nos três documentos alterados. Os quatro arquivos OpenSpec não rastreados foram inspecionados e passaram nas verificações de whitespace e newline final.
- Comparação UTF-8 com normalização de finais de linha confirmou as quatro linhas RF10–RF13 idênticas ao baseline; os três destinos locais de links Markdown alterados existem.
- Limite: validação exclusivamente documental. Não foram executados testes da aplicação, validação hospedada ou publicação. O titular aprovou posteriormente o conjunto documental no harness em 27/11/2026; isso não aceita implementação nem declara executado/aceito o checkpoint P0.

## Workflow follow-up

- Em 27/11/2026, o titular aprovou explicitamente no harness PRD 0.6, BACKLOG 0.95, TDD 0.46 e os artefatos desta mudança OpenSpec; aprovações anteriores permanecem restritas a seus escopos.
- O titular autorizou commits locais pelo subagente committer, push da branch e abertura de PR contra `main`, limitados a este conjunto documental; merge e publicação não foram autorizados.
- Sincronizar/arquivar a mudança documental após validação/aceite quando cabível; `skip_specs: true` significa que não há delta de comportamento a sincronizar.
