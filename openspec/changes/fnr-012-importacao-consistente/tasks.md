# Tasks

## 1. Gates de execução e contrato de origem

- [ ] 1.1 Apresentar proposta, delta, desenho e tarefas FNR-012 para aprovação explícita; conferir separadamente a revisão pendente do BACKLOG 0.42 e registrar no backlog o status verdadeiro da FNR-012 sem herdar aprovação documental ou iniciar código antes dos gates aplicáveis.
- [ ] 1.2 Conferir a documentação de `next` com `dateFrom` e a validação atual de `PluggyApiClient.TryBuildAnalysisPath`, criar prova sintética para continuação de múltiplas páginas com e sem filtro e documentar em `docs/PLUGGY-CONTRACT.md` o comportamento seguro; se não houver garantia de preservação do filtro, bloquear a implementação dependente e submeter revisão de desenho antes de afrouxar a validação.
- [ ] 1.3 Medir com fixtures sintéticas de múltiplos cartões/páginas o orçamento inicial de 100 páginas por conta, 50.000 movimentos, 30 s por chamada e 120 s totais; registrar memória/tempo e decisão em `docs/AMBIENTE.md`, revisando SDD/aceite antes de trocar a promoção única por staging caso a coleta delimitada não seja viável.

## 2. Coleta de contas e movimentos

- [ ] 2.1 Criar contratos internos de leitura e resultado em `Finora.Application` e adaptador Pluggy em `Finora.Infrastructure` sem alterar os comandos diagnósticos; testar titular/conexão incompatíveis, item indisponível e ausência de segredos no startup da API.
- [ ] 2.2 Percorrer páginas de contas `CREDIT/CREDIT_CARD` com moeda de origem e movimentos com `dateFrom` fixo, sem `dateTo`, cursor/filtros seguros e limites; testar duas páginas, múltiplos cartões, futuro recebido, página vazia, cursor cíclico, conta alheia, falha intermediária e orçamento excedido com provedor simulado.
- [ ] 2.3 Rejeitar campos-base ausentes, payload contraditório do mesmo ID e valores fora da precisão suportada sem publicar dados; documentar contrato de coleta e critérios de incompletude em `docs/PLUGGY-CONTRACT.md` e verificar os casos com testes sintéticos.

## 3. Normalização e identidade

- [ ] 3.1 Mapear somente os campos mínimos permitidos para `TransactionFacts` e `FinancialTransaction` usando `TransactionNormalizer` com compra positiva desativada; testar `PAGAMENTO` desconhecido, `PAGAMENTO_FATURA`, estorno, parcela futura, data ambígua, moeda e precisão decimal sem soma ou competência presumida.
- [ ] 3.2 Definir atualização de fatos da origem em entidades financeiras sem alterar UUID, categoria ou observação; testar com PostgreSQL descartável duas importações idênticas, IDs diferentes com conteúdo igual, mudança de pendência/estorno e ausência de movimento na segunda coleta.
- [ ] 3.3 Documentar em `docs/AMBIENTE.md` a correspondência entre identidade externa, snapshot técnico e anotação separada; verificar que não há dado fictício apresentado como amostra real ou reclassificação financeira.

## 4. Promoção atômica e estado observável

- [ ] 4.1 Implementar coleta completa antes da transação e promoção única de cartões/movimentos, `LastSuccessfulImportAtUtc` e resultado terminal `SyncRun`; testar rollback após gravação intermediária e que nenhuma transação PostgreSQL fica aberta durante HTTP externo.
- [ ] 4.2 Registrar falhas com código allowlisted e cobertura/contadores desconhecidos ou parciais sem renovar o último sucesso; testar timeout, cancelamento, autorização inválida, janela vazia, erro na segunda página e ausência de mensagens/IDs/valores sensíveis no resultado, logs e banco.
- [ ] 4.3 Proteger a promoção contra coletas concorrentes/stale, relendo a conexão sob bloqueio apenas no commit; testar duas ordens de promoção, reexecução idempotente e confirmação perdida com verificação independente antes de declarar estado final.
- [ ] 4.4 Documentar em `docs/AMBIENTE.md` intervalo pedido versus cobertura observada/indeterminada, interpretação restrita de `CompleteForRequestedWindow`, códigos de falha e limites de confirmação; verificar os exemplos com testes PostgreSQL e sem prometer histórico integral.

## 5. Reconexão conservadora

- [ ] 5.1 Verificar continuidade do mesmo titular/provedor e IDs externos dos cartões antes de trocar item da conexão, preservando UUIDs/anotações numa única promoção; testar reconexão demonstrável e repetição com item antigo usando PostgreSQL descartável.
- [ ] 5.2 Recusar novo ID de cartão sem prova, nomes/finais iguais, mapeamento contraditório e colisão com outra conexão/titular; testar que histórico/último sucesso não mudam e documentar em `docs/AMBIENTE.md` a necessidade de revisão quando a origem não permite provar equivalência.

## 6. Verificação integrada do incremento

- [ ] 6.1 Executar restore bloqueado, build e regressão dos testes afetados sem/PostgreSQL descartável, incluindo isolamento do banco e comandos locais; registrar números reais e limitações em `docs/AMBIENTE.md` sem conectar à Pluggy real, Neon ou banco pessoal.
- [ ] 6.2 Validar OpenSpec estrito, links, diff/whitespace, limites de privacidade e rastreabilidade FNR-012 ↔ RF02/RF03 ↔ TDD §§10/12 ↔ `transaction-import`; conferir ausência de endpoint financeiro, broker, migration automática e agregados monetários, registrando falhas sem marcar tarefa não comprovada como concluída.

## Workflow follow-up

- Apresentar evidências e obter aceite explícito da implementação antes de encaminhar commits/PR conforme `AGENTS.md`; tarefas marcadas não substituem o aceite.
- Integrar somente por PR após revisão e verificações aplicáveis; se houver migration, aplicá-la administrativamente em destino compatível antes da integração que dependa do deploy. Sincronizar a delta e arquivar após validação/aceite, preservando gates posteriores da FNR-013/015/038.
