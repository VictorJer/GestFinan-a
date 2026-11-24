# Tasks

## 1. Gate documental e contrato FNR-018

- [x] 1.1 Conferir dependências FNR-005 revisada/FNR-015/FNR-017 no histórico local e reconciliar em `docs/BACKLOG.md` a linha factual do PR #31/merge `b173e58`, preservando o registro anterior e verificando por Git/links que não há alegação de review, check ou publicação não comprovados.
- [x] 1.2 Preparar revisão própria de `docs/TDD.md` §§11/13 e BACKLOG para o contrato `GET /api/dashboard`, filtros/universo, `dateBasis`, grupos e cobertura da delta; apresentar diffs e obter aprovação explícita dos artefatos OpenSpec e das revisões canônicas antes de código dependente, verificando registro do aceite sem transferir aprovações históricas.

## 2. Universo filtrado compartilhado

- [x] 2.1 Reutilizar na Application a validação da FNR-015 e resolução de conexão/cartão próprios para resumo sem paginação externa; verificar testes de filtros inválidos, datas ISO, busca literal, conexão ausente/ambígua e cartão alheio/inexistente.
- [x] 2.2 Extrair predicado parametrizado comum de `TransactionMovementReader` para lista e resumo, inclusive anotação por titular, categoria, situação, período e busca; verificar no PostgreSQL descartável que o `filteredCount` existente da lista não muda e que filtros combinados/categoria alheia produzem o mesmo universo.

## 3. Agregação e cobertura

- [x] 3.1 Implementar leitor PostgreSQL de contagens por `Nature` e `Situation` mais `filteredCount` do conjunto filtrado inteiro, inclusive grupos sem ocorrência; verificar cenários sintéticos com `PAGAMENTO` desconhecido, `PAGAMENTO_FATURA`, pendência, parcela futura, tarifa/ajuste, moedas e sinais mistos, somando cada eixo ao total sem ler `Amount` para agregação.
- [x] 3.2 Reutilizar a cobertura correlata da FNR-015 e retornar contagens/cobertura sob um snapshot curto de leitura; verificar testes PostgreSQL com sucesso anterior, falha/parcial posterior, ausência de importação, janela concluída e escrita concorrente durante o resumo sem publicar estado híbrido.
- [x] 3.3 Testar após PATCH de categoria e com data técnica nula/intervalo que os filtros do resumo refletem o estado confirmado e conciliam com `GET /api/transactions` sob dados estáveis, sem modificar natureza, situação ou fatos de origem.

## 4. Exposição protegida da API

- [x] 4.1 Adicionar o endpoint `GET /api/dashboard` com sessão ativa, DTO allowlist (`filteredCount`, `countsByNature`, `countsBySituation`, `coverage`, `dateBasis`) e códigos permitidos; verificar por testes HTTP que não há valores individuais/agregados monetários, IDs externos, classificação de compra presumida ou payload bruto.
- [x] 4.2 Exercitar 400/401 com Bearer challenge/404 indistinguível/409/429/500/503 sanitizados e indisponibilidade do provedor/broker sem nova `SyncRun`; verificar testes HTTP de propriedade cruzada e inspeção de corpos/logs sem vazamento de texto da busca ou finanças.
- [x] 4.3 Documentar o contrato HTTP e seus limites no README/guia de API e atualizar rastreabilidade de RF05/RF07/M-03 e fronteira FNR-022/023/038 nos registros aplicáveis; verificar exemplos com dados inteiramente sintéticos, links e ausência de afirmação de frontend/publicação concluídos.

## 5. Validação integrada do incremento

- [x] 5.1 Executar restore bloqueado, build, regressões focais de FNR-015/017 e testes novos sem/com PostgreSQL descartável; registrar quantidades e eventuais falhas da suíte ampla sem declará-la verde se as fixtures legadas ainda falharem.
- [x] 5.2 Medir consulta de resumo com até 5.000 movimentos sintéticos no PostgreSQL descartável, registrando ambiente, filtros, duração e plano, confrontando a meta local RNF-08 sem inferir SLA hospedado; se índice ou migration se tornar necessário, interromper e revisar o plano antes de adicioná-lo.
- [x] 5.3 Validar a mudança OpenSpec em modo estrito, diff/links e matriz de cenários do RF07/M-03; apresentar resultados e pendências para aceite humano, sem marcar requisitos financeiros adiados, tarefas do explorador ou publicação como concluídos.

## Workflow follow-up

- Após validação e aceite explícito, encaminhar commits locais pelo subagente committer somente com autorização, abrir PR da feature branch e conferir checks/revisões realmente aplicáveis antes de integrar; não fazer push/merge/publicação automaticamente.
- Sincronizar a delta `card-movement-summary` e arquivar esta mudança após integração/aceite conforme o fluxo do repositório; manter as tarefas da mudança separada do explorador em seu estado factual.
