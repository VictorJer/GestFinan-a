# Tasks

## 1. Aprovar e alinhar o escopo documental

- [ ] 1.1 Obter aprovação explícita desta mudança e do modelo sem soma monetária; registrar que isso não aprova regra de compra/competência nem fecha FNR-005/5.3.
- [ ] 1.2 Revisar `docs/PRD.md` em RF04/RF07, regras de cartão e definição de pronto para separar explorador descritivo de indicadores financeiros completos; preservar o objetivo de longo prazo como dependente de gates e registrar a revisão pendente de aprovação.
- [ ] 1.3 Revisar `docs/TDD.md` nos §§10–13 e §19 para manter natureza/situação/cobertura explícitas, proibir somas no frontend e definir dados de origem versus competência; validar rastreabilidade a RFs e backlog.
- [ ] 1.4 Após aprovação das revisões canônicas, atualizar `docs/BACKLOG.md` para manter FNR-005/5.3 aberta, discriminar fatias descritivas de FNR-018/023 e suas dependências, e conferir que nenhum status de aceite representa trabalho não executado.
- [ ] 1.5 Propor no backlog a separação entre o incremento técnico da FNR-005 já registrado como validado e a investigação financeira da tarefa 5.3 ainda bloqueada; conferir a rastreabilidade com as tarefas históricas sem reclassificar evidências nem marcar 5.3 concluída.

## 2. Expor movimentos e cobertura na API

- [ ] 2.1 Definir DTO/consulta protegida para movimentos com valor individual, moeda, data de origem, situação, natureza/motivo e metadados disponíveis; testes devem provar que nenhuma resposta inclui agregado monetário, independentemente de rótulo.
- [ ] 2.2 Expor completude e cobertura da importação junto aos movimentos; testes de falha/paginação devem impedir que subconjuntos sejam anunciados como completos.
- [ ] 2.3 Aplicar somente as regras aprovadas de normalização; testar `PAGAMENTO` desconhecido, `PAGAMENTO_FATURA` distinto, situação independente e enum não reconhecido sem vazar valor externo.
- [ ] 2.4 Documentar o contrato da API e a semântica dos estados no TDD/spec correspondente; verificar que campos ausentes e natureza desconhecida não são serializados ou convertidos como zero.

## 3. Construir a visão de movimentos

- [ ] 3.1 Após as dependências de base Angular e consulta de transações (FNR-019/FNR-022) estarem aceitas, montar a lista descritiva usando somente DTOs do backend; testes verificam valor por movimento, moeda, data de origem, natureza e situação.
- [ ] 3.2 Exibir cobertura/completude e rótulo explicativo para naturezas desconhecidas; testes de componente cobrem carga, ausência, falha, consulta parcial e conjunto com ambiguidades.
- [ ] 3.3 Não calcular, retornar nem renderizar qualquer agregado monetário (incluindo total, subtotal, bruto/líquido, média, série, gráfico ou agrupamento por período/status/natureza/categoria); teste de API e UI comprova a proibição mesmo sob rótulos como “parcial” ou “informativo”.
- [ ] 3.4 Fornecer resumo HTML acessível e documentar a diferença entre movimentos observados, natureza identificada e gasto financeiro não validado; verificar teclado e leitura sem gráficos enganadores.

## 4. Verificar critérios e limitações

- [ ] 4.1 Usar fixtures inteiramente sintéticas para estados, naturezas conhecidas/desconhecidas, ausências e paginação incompleta; verificar que nenhuma fixture é apresentada como evidência real.
- [ ] 4.2 Executar testes e builds afetados, revisar segurança/propriedade e confirmar que nenhum caminho de dashboard agrega valores ou transforma ausências em zero.
- [ ] 4.3 Validar OpenSpec e diff, conferir PRD/TDD/backlog/spec/API/UI coerentes e preservar sem alteração as observações existentes da FNR-005; não realizar coleta real nem reclassificar os 319 resultados históricos.

## Workflow follow-up

- O incremento técnico já validado da FNR-005 (tarefas 5.1–5.2, 5.4–5.15 e 6.1–6.3) permanece registrado como histórico; a investigação financeira da tarefa 5.3 permanece aberta e nenhuma tarefa/status histórico é reclassificado por esta proposta.
- FNR-007/012/013/014 e as fatias de movimentos de FNR-015/022 podem avançar com identidade, campos de origem, paginação/cobertura e classificação conservadora, respeitando suas dependências próprias e sem total monetário.
- FNR-019/021 podem avançar em sua base visual/conexão; somente uma fatia descritiva de FNR-018 e a visão de movimentos de FNR-023 podem avançar após revisão explícita das dependências do backlog.
- Permanecem bloqueados a tarefa 5.3, totais/quantidades financeiras de compras em FNR-015/018/023, distribuição monetária por categoria e evolução de gastos por competência. Gasto líquido também exige vínculo confiável de estorno.
- Indicadores de gasto só podem ser planejados em mudança posterior após evidência aprovada para reconhecer compras, cobertura classificatória suficiente, semântica temporal/competência e, para líquido, vínculo confiável de estornos.
