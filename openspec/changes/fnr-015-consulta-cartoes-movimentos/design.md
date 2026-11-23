# Design

## Context

Ver `proposal.md` e `specs/card-movement-query/spec.md`. `FinoraDbContext` já persiste `FinancialConnection`, `CardAccount`, `FinancialTransaction`, `TransactionAnnotation` e `SyncRun`. O importador confirma dados, horário de sucesso da conexão e cobertura da janela em uma mesma transação; a execução posterior pode falhar sem invalidar o histórico anterior. `ConnectionSyncController` e `SyncRunsController` demonstram sessões validadas, titular derivado de claims, consultas MediatR e Problem Details sanitizado. Ainda não existem Controllers de cartões ou movimentos; `GET /api/cards` e `GET /api/transactions` em TDD §11 são propostas, não rotas publicadas.

## Goals / Non-Goals

**Goals:**
- Entregar a fatia de leitura Api → Application → Infrastructure sem duplicar política de sincronização ou normalização financeira.
- Projetar DTOs estáveis e consultas limitadas, separando contagem do filtro, página e metadados de importação confirmada/última execução.
- Demonstrar isolamento por titular, comportamento ante estado parcial e ausência de efeito externo nas leituras reais.

**Non-Goals:**
- Não criar endpoint de importação, alterar o classificador, editar anotações, construir UI ou computar resumos/valores agregados; não declarar cobertura bancária integral.
- Não migrar schema por presunção nem tratar aprovação da FNR-014 como aprovação desta mudança.

## Decisions

1. **Nova capability da fronteira de consulta, sem duplicar o explorador.** `card-movement-query` responde apenas pela API FNR-015; a mudança `explorador-movimentos-cartao-sem-total` descreve produto e UI e tem tarefas de API sobrepostas. Referenciá-la e reconciliar suas tarefas no planejamento da entrega visual antes de alegar conclusão daquela mudança; não editar nem marcar suas caixas nesta proposta. Alternativa rejeitada: executar todo o explorador como FNR-015, antecipando FNR-018/022/023 e confundindo aceites.

2. **Separação de responsabilidades e sessão.** `CardsController` e `TransactionsController` autorizados extraem `UserId` de `JwtTokenValidation.ReadSessionClaims` após validação persistente da sessão; não aceitam `UserId` do cliente. Casos de uso/queries MediatR recebem filtros validados e titular confiável; leitores EF Core projetam com `AsNoTracking`, impondo `UserId` na conexão, cartão, movimento e anotação. `cardId` alheio e inexistente convergem para 404, antes de paginar; `categoryId` sem anotação própria correspondente resulta no mesmo conjunto vazio de ID inexistente, sem consultar titular alheio. Alternativa rejeitada: filtrar somente no Controller ou confiar em FK para autorização.

3. **Contratos HTTP de leitura.** `GET /api/cards` retorna lista de `id`, `name?`, `lastFour?`, `currencyCode` da conexão própria única; não expõe IDs externos, credenciais ou número completo. `GET /api/transactions` aceita `cardId?`, `from?` inclusivo e `toExclusive?` exclusivo (`yyyy-MM-dd`), `categoryId?`, `situation?` de enum interno permitido, `search?` textual, `page` (1 por padrão), `pageSize` (25 por padrão, 1–100). Limites de data unilaterais são válidos; rejeitar página não positiva, tamanho excedido, data inválida, par invertido/igual quando ambos os limites são informados e busca excessiva (limite proposto 200 caracteres) com 400 sanitizado. Se não há conexão própria, 404; se há múltiplas conexões do mesmo titular, 409, sem selecionar uma arbitrariamente. Conexão sem cartões/movimentos retorna lista/itens vazios com estado de cobertura honesto. Alternativa rejeitada: rota por ID externo ou contagem ilimitada sem paginação.

4. **Filtro e projeção preservam semântica de origem.** Filtrar pelo `TransactionDate` normalizado tecnicamente, mantendo `OriginalDate` e `DateReason` no DTO para deixar clara a origem; data técnica nula permanece na lista sem filtro e não coincide com intervalo. Ordenar por `TransactionDate` decrescente, nulos depois, `Id` decrescente. `search` é substring case-insensitive de `Description`, com `%`, `_` e escape SQL tratados como texto literal; consultas parametrizadas, sem expressão de ordenação enviada pelo cliente. Projeção inclui `id`, `cardId`, `description`, `amount`, `currencyCode`, `originalDate`, `transactionDate?`, `dateReason?`, `nature`, `natureReason?`, `situation`, `situationReason?`, `installmentNumber?`, `totalInstallments?`, `categoryId?` e `note?`; não retorna `operationType` bruto, payload, identificador externo, previsão de fatura ou campo que sugira competência. Categoria/nota vêm somente de anotação do mesmo titular; nenhum default de categoria é inventado. Alternativa rejeitada: usar `billForecastDate` como competência ou deduzir natureza por sinal/categoria.

5. **Contagem e cobertura são dimensões distintas.** Contar o conjunto filtrado no SQL antes de `Skip/Take`; leitura paginada não materializa todos os movimentos. No mesmo snapshot de leitura, consultar `LastSuccessfulImportAtUtc`, a `SyncRun` de sucesso correspondente (`FinishedAtUtc` e conexão/titular compatíveis), e a execução mais recente do mesmo titular/conexão. Retornar `filteredCount` inteiro da base persistida, `lastSuccessfulImportAtUtc?`, janela solicitada/limites observados e `coverageState` do sucesso apenas se correspondência comprovada; caso contrário, cobertura `Unknown`/campos nulos. `latestExecution` resume estado, cobertura e falha allowlist independentemente do sucesso anterior. `CompleteForRequestedWindow` significa apenas todas as páginas da janela solicitada, nunca todos os meses ou todos os movimentos bancários. Usar leitura consistente da contagem, página e contexto para evitar combinar instantes de promoção diferentes; reduzir o tempo da transação read-only ao estritamente necessário. Alternativa rejeitada: usar a execução mais recente como se fosse último sucesso ou tratar zero filtrado como ausência de gasto.

6. **Erros e dependências.** Utilizar Problem Details já padronizado: 400 para filtros inválidos, 401 para sessão inválida, 404 para conexão/cartão inacessível, 409 para conexão própria ambígua, 429 para limitador já vigente, 503 somente indisponibilidade tipada e 500 para falhas inesperadas. Leituras usam apenas PostgreSQL; indisponibilidade de broker/Pluggy não é 503 dessas rotas, e nenhuma consulta de filtro/navegação cria `SyncRun`. Evitar descrições, valores, tokens e consultas de busca em logs. Alternativa rejeitada: chamar endpoint de atualização ao abrir lista ou derivar status da mensagem de exceção.

7. **Validação proporcional.** Testes de unidade validam filtros, projeções e DTOs sem agregados; testes HTTP + PostgreSQL descartável verificam isolamento, contagem com várias páginas, empate/nulos, intervalo, busca literal, estado após falha, reimportação/visibilidade, erro sanitizado e ausência de criação de SyncRun. Exercitar conjuntos sintéticos, inclusive moeda e sinais mistos; não consultar provedor real ou Neon. Inspecionar planos/custo de contagem e busca para volume sintético previsto (até 5.000 movimentos); aproveitar índice existente `(CardAccountId, TransactionDate, Id)` quando útil. Qualquer índice/migration adicional exige justificativa, validação local e fluxo de aplicação em destino do TDD, não aplicação automática no startup.

## Risks / Trade-offs

- [Busca por substring pode fazer varredura em conjuntos grandes] → impor limite de entrada e página, medir no PostgreSQL descartável e só propor índice/migration após evidência.
- [Contagem e página podem divergir sob importação concorrente] → usar snapshot read-only coerente para a resposta e testes de promoção concorrente; paginações em requisições diferentes não prometem snapshot global imutável.
- [Último sucesso e última execução podem ser confundidos] → informar campos separados e cobertura somente da janela comprovada; não transformar `Partial`, `Unknown` ou histórico legado sem run correlata em completo.
- [Nova API pode duplicar tarefas da mudança do explorador] → manter escopo de capability exclusivo da consulta e explicitar dependência/limite no backlog e no futuro plano de UI.

## Migration Plan

Planejar implementação em feature branch após revisão e aprovação destes artefatos e, se alterar conteúdo canônico, do diff PRD/TDD pertinente. API aditiva não altera rotas atuais e não requer migration no desenho inicial. Validar testes/build/OpenSpec antes de PR; aplicar migration compatível no destino antes de integrar alteração dependente caso índice/schema se torne necessário. Rollback da API aditiva restaura a versão anterior sem apagar histórico. Sem publicação nem migration autorizadas nesta etapa de planejamento.
