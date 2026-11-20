# Design

## Context

Ver `proposal.md` e `specs/transaction-import/spec.md`. Atualmente `PluggyAnalysisReader`/`PluggyConnectionInspector` são somente diagnóstico local: o primeiro descarta payload após contar, o segundo escolhe um cartão, e ambos dependem de `PluggyOptions` com item/conta configurados; `PluggyServiceCollectionExtensions` só registra esse cliente no comando local. `FinoraDbContext` já contém `FinancialConnection`, `CardAccount`, `FinancialTransaction`, `TransactionAnnotation` e `SyncRun` com FKs/unicidades; `FinancialTransaction.Create` não dispõe de atualização de origem pública. Os testes existentes provam update controlado e rollback, **não** uma importação integrada. Não há endpoint financeiro nem frontend para a importação.

## Goals / Non-Goals

**Goals:**

- Definir uma operação interna testável para coletar contas/cartões e movimentos, validar todos antes de uma única promoção transacional e retornar resultado sanitizado.
- Reaproveitar transporte e mapeamento mínimo do contrato sem transformar o comando diagnóstico em gravador ou criar atalhos de segurança via `PluggyOptions` globais.
- Preservar UUIDs internos, anotações, semântica conservadora e rastreabilidade de intervalo/cobertura para FNR-013/015.

**Non-Goals:**

- Não criar endpoint, agendador, broker, outbox, política de tentativas, lock distribuído de coleta ou atualização incremental otimizada; estes pertencem à FNR-013/014.
- Não mudar o contrato de análise local, habilitar regra de compras, consultar dados reais sem consentimento específico ou interpretar mês sem movimento como cobertura integral.

## Decisions

### D1. Contratos internos e separação do modo diagnóstico

Criar caso de uso em `Finora.Application` que recebe titular e ID interno de conexão de um chamador confiável, resolve a conexão pelo par `(Id, UserId)` e usa contratos explícitos para leitura paginada e gravação. `Finora.Infrastructure` implementa os contratos, usando segredos operacionais fora do banco/repositório e item obtido da conexão persistida; a identidade da conta não é uma escolha livre do corpo HTTP. Reaproveitar o `PluggyApiClient` e mapeadores de campos mínimos onde seguro, mas não usar `PluggyAnalysisReader` como importador: ele descarta dados e seleciona só um cartão. O caminho normal de inicialização HTTP segue sem exigir segredo ou conexão com a Pluggy. Reconexão usa um comando interno separado, cujo item candidato só pode vir de chamador confiável; não há campo ou endpoint HTTP para selecionar esse ID. Alternativa rejeitada: expor o comando `pluggy-analyze` como importação ou introduzir endpoint financeiro antes de FNR-013.

### D2. Coleta, validação e limite de recursos antes de gravar

Fixar referência UTC no início; primeira janela usa `DateOnly` da referência menos 12 meses para `dateFrom` e não usa `dateTo` para não descartar parcelas futuras. Listar todas as páginas de contas do item verificado, filtrar `CREDIT/CREDIT_CARD`, ler todas as páginas de cada conta com orçamento total e limites explícitos (referência diagnóstica inicial: 100 páginas por conta, até 50.000 movimentos no conjunto, 30 s por chamada e 120 s por execução). Anexar `next` exatamente como recebido ao endpoint fixo, sem reconstruir `after` nem acrescentar filtros. Validar caminho/origem, conta e filtros explicitamente conflitantes; não exigir `dateFrom` literal em `next` como única prova de preservação. Rejeitar ciclo, duplicata de conta ou conflito de payload do mesmo `(conta, ID)`. Um limite excedido produz falha/incompletude, nunca sucesso parcial. Reservar memória para o conjunto mínimo permitido, sem logar seu conteúdo, e medir consumo/duração com fixtures multípáginas antes de confirmar que os limites suportam o volume esperado; se não, revisar o desenho para staging por execução antes de implementar publicação em lotes. Alternativa rejeitada: gravar uma página de cada vez como histórico visível e chamar a execução de completa.

**Gate de contrato:** [Basic concepts](https://docs.pluggy.ai/en/docs/reference/basic-concepts.md) determina anexar `next` tal como recebido e afirma que ele já carrega os filtros e `after`; a [referência de `GET /v2/transactions`](https://docs.pluggy.ai/en/reference/transaction/transactions-list-by-cursor) confirma esse uso. Seu exemplo omite `dateFrom`, mas não mostra uma primeira chamada filtrada: não prova repetição literal nem perda do filtro. A leitura real anterior terminou numa página (`next=null`), sem observar continuação filtrada. Após aprovação desta revisão, testar sinteticamente `next` com e sem `dateFrom` literal, conta/filtros conflitantes, destino inseguro, ciclos e limites; os testes verificam somente o cliente. Não inferir comportamento observado do servidor, nem declarar completa uma coleta interrompida ou avançar `LastSuccessfulImportAtUtc`. Sem consulta real neste plano.

### D3. Promoção transacional e concorrência no commit

Depois da coleta e normalização completa, abrir transação curta no PostgreSQL (nenhuma transação aberta durante HTTP); bloquear a linha da conexão apenas na promoção, reler o estado e recusar aplicar snapshot iniciado antes do último sucesso já confirmado. Aplicar cartões e movimentos com upsert lógico por chaves contextuais usando as unicidades/constraints existentes, mantendo UUIDs de movimentos existentes e `TransactionAnnotation` intocada. No mesmo commit atualizar `LastSuccessfulImportAtUtc` e registrar resultado terminal `SyncRun` com janela, extremos e contadores disponíveis: `CompleteForRequestedWindow` significa somente paginação terminada para a janela solicitada, nunca cobertura bancária integral. Resultado de falha antes da promoção pode registrar `SyncRun` terminal com código allowlisted numa transação separada, sem tocar o histórico nem renovar sucesso; confirmação de commit incerta exige leitura independente antes de declarar sucesso ou repetir a promoção. A FNR-013 acrescentará fila/posse/retry/retomada sobre esse núcleo; esta operação não inicia trabalho em background. Alternativas rejeitadas: `SaveChanges` por página e transação durante chamadas externas.

### D4. Reconciliação e atualização sem inferência

Na conexão corrente, chave de conta é `(ConnectionId, ExternalId)` e de movimento `(CardAccountId, ExternalId)`. Reconexão com novo item só reutiliza ID interno quando um comando interno confiável solicita a troca e a coleta completa do novo item comprova o mesmo titular/provedor e o conjunto não vazio exato de IDs externos de cartões já persistidos; nomes e finais não provam continuidade. A troca do item ativo, promoção financeira e registro do item anterior como aposentado ocorrem na mesma transação. Uma tabela-registro de identidades externas representa itens ativos e aposentados: chave primária `(Provider, ExternalItemId)` impede reutilização entre conexões/titulares e índice único parcial permite no máximo um item ativo por conexão. A migration aditiva registra inicialmente os itens ativos existentes e falha diante de colisões preexistentes não resolvidas. Inserção concorrente do item candidato é arbitrada pela unicidade PostgreSQL; conflito com identidade ativa ou histórica bloqueia a promoção. Se um cartão mudar de ID externo, faltar prova de continuidade ou houver múltiplos candidatos, bloquear a promoção para reconciliação posterior, sem criar segunda carteira por semelhança de nome/final. A origem não fornece prova geral de equivalência entre IDs de conta distintos; esse caso permanece bloqueado, não solucionado por heurística. Testar também repetição do item anterior após a troca. Alternativa rejeitada: fundir por últimos dígitos, descrição, valor ou identidade de usuário apenas.

### D5. Semântica de dados, privacidade e esquema

Converter `PluggyTransactionData` em `TransactionFacts`, chamar `TransactionNormalizer` com regra positiva desativada e fixar `NormalizationReferenceDate`/versão; criar/atualizar somente os campos de origem de `FinancialTransaction`, preservando referências pessoais. A conta exige moeda real do DTO mínimo de conta (`currencyCode` documentado pela Pluggy); se ausente, falhar em vez de inferir da primeira transação. Metadados opcionais continuam nulos quando indisponíveis. Datas técnicas inválidas permanecem ausentes com motivo quando o contrato de persistência permite; campos-base realmente ausentes e valores fora de `numeric(19,4)` falham o conjunto. Usar códigos `SyncRunFailureCodes` e mensagens fixas, sem IDs, cursores, descrições, valores, payloads nem credenciais em logs, exceções expostas ou evidências. A reconexão requer migration aditiva para histórico de identidades externas aposentadas; a migration deve fazer backfill conservador, falhar diante de colisões preexistentes não resolvidas e ser testada em banco descartável vazio e atualizado. Migrations continuam administrativas e nunca são aplicadas no startup.

## Risks / Trade-offs

- [Memória para uma coleta completa] → impor orçamento e cap explícitos, medir multi-página com dados sintéticos; se exceder, revisar SDD para staging antes de declarar suporte ao intervalo completo.
- [Formato literal de `next` em consulta filtrada não observado] → aplicar a garantia documental de continuação sem reconstruir o cursor; rejeitar conflitos explícitos e falhar fechado em destino, conta, ciclo, limite ou resposta inseguros. Teste sintético não comprova a resposta real.
- [Commit com confirmação perdida] → reler estado em contexto independente e distinguir resultado indeterminado de falha comprovada; retry idempotente não deve atualizar uma versão mais nova com snapshot antigo.
- [Reconexão altera ID de cartão sem prova] → bloquear reconciliação e preservar histórico; não prometer continuidade automática nesse caso.
- [Registro terminal sem trabalhador] → FNR-012 é componente interno, não endpoint nem fila; FNR-013 deve adaptar ciclo de vida de `SyncRun` ao executor sem duplicar gravação ou forjar sucesso.
- [Data e natureza ambíguas] → manter motivos da normalização existentes; cobertura de páginas não valida compras ou competência.

## Migration Plan

Implementar e validar somente na feature branch com fixtures Pluggy simuladas e PostgreSQL local descartável; manter API sem rota de importação e comandos diagnósticos inalterados. Havendo alteração de schema, criar migration aditiva e aplicar explicitamente em banco de teste vazio e atualizado, sem migration automática no startup; verificar compatibilidade antes de PR ou deploy dependente. Rollback de código não apaga histórico, anotações nem executa down migration automaticamente; uma eventual reversão de schema exige plano administrativo separado e prova de preservação dos dados. Não publicar endpoint/worker antes da FNR-013 e dos gates de integração/publicação.
