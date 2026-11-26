# Tasks

Direção de escopo e conteúdo documental revisado aprovados pelo titular em 16/11/2026 como parte do gate FNR-005/7.1; isso não aceita a implementação ou o encerramento deste explorador. O rastreamento próprio desta mudança não foi avançado nesta execução. O incremento técnico da FNR-005 está integrado pelo PR #12, merge `adee6998342722e905a1db98ed1423620ce77ee4`; isso não implementa este explorador. As tarefas de implementação permanecem pendentes. Os grupos 2–4 mapeiam trabalho futuro das entregas indicadas no backlog, não autorizam iniciá-las em sequência nem duplicar suas implementações. Recorrência será especificada e implementada em mudança própria.

## 1. Aprovar e alinhar o escopo documental

- [x] 1.1 Apresentar e obter aprovação explícita do conteúdo revisado desta mudança e do modelo sem soma monetária; verificar que a direção já aprovada está distinguida do aceite documental final e que nenhuma regra de compra/competência é declarada validada.
- [x] 1.2 Revisar `docs/PRD.md` em RF04/RF07, regras de cartão e definição de pronto para separar explorador descritivo de indicadores financeiros completos; preservar o objetivo de longo prazo dependente de gates e registrar a aprovação documental explícita de 16/11/2026, distinta do aceite da implementação.
- [x] 1.3 Revisar `docs/TDD.md` nos §§10–13 e §19 para manter natureza/situação/cobertura explícitas, proibir somas no frontend e definir dados de origem versus competência; validar rastreabilidade a RFs e backlog.
- [x] 1.4 Preparar o backlog revisado com FNR-005/5.3 substituída por revisão de escopo, não tecnicamente atendida; discriminar consulta FNR-015, lista FNR-022, resumos não monetários FNR-018 e visão descritiva FNR-023, verificando dependências sem ciclos ou lista duplicada. Registrar o aceite já obtido das revisões canônicas antes de qualquer implementação; não declarar encerramento fora dos limites aceitos.
- [x] 1.5 Registrar no backlog o incremento técnico integrado pelo PR #12, o critério original de 5.3 não atendido, os critérios revisados de encerramento e o encaminhamento da recorrência para item/mudança próprios; verificar que o padrão de três meses exige confirmação pelo usuário, distingue metadados de parcelamento e mantém tolerâncias pendentes, sem reclassificar evidências nem marcar 5.3 concluída.

## 2. Expor movimentos e cobertura na API

- [ ] 2.1 Definir DTO/consulta protegida para movimentos com valor individual, moeda, data de origem, situação, natureza/motivo e metadados disponíveis; testes devem provar que nenhuma resposta inclui agregado monetário, independentemente de rótulo.
- [ ] 2.2 Expor completude e cobertura da importação junto aos movimentos; testes de falha/paginação devem impedir que subconjuntos sejam anunciados como completos.
- [ ] 2.3 Aplicar somente as regras aprovadas de normalização; testar `PAGAMENTO` desconhecido, `PAGAMENTO_FATURA` distinto, situação independente e enum não reconhecido sem vazar valor externo.
- [ ] 2.4 Documentar o contrato da API e a semântica dos estados no TDD/spec correspondente; verificar que campos ausentes e natureza desconhecida não são serializados ou convertidos como zero.

## 3. Construir a visão de movimentos

- [ ] 3.1 Conferir os gates próprios da base Angular/sessão, consulta protegida, lista FNR-022 e resumos FNR-018 conforme o backlog; verificar que a visão geral FNR-023 apresenta resumos, cobertura e filtros aplicados e leva à lista **única** FNR-022 preservando esses filtros, sem reproduzir nela os movimentos. Verificar com evidência de integração que valor individual, moeda, data de origem, natureza e situação vindos dos DTOs do backend aparecem na lista, não na visão geral; registrar o limite de GETs independentes sem snapshot atômico. A ausência de aceite explícito da FNR-020 permanece uma pendência separada, não uma reprovação retroativa das demais entregas.
- [ ] 3.2 Exibir cobertura/completude e rótulo explicativo para naturezas desconhecidas; testes de componente cobrem carga, ausência, falha, consulta parcial e conjunto com ambiguidades.
- [ ] 3.3 Não calcular, retornar nem renderizar qualquer agregado monetário (incluindo total, subtotal, bruto/líquido, média, série, gráfico ou agrupamento por período/status/natureza/categoria); teste de API e UI comprova a proibição mesmo sob rótulos como “parcial” ou “informativo”.
- [ ] 3.4 Fornecer resumo HTML acessível e documentar a diferença entre movimentos observados, natureza identificada e gasto financeiro não validado; verificar teclado e leitura sem gráficos enganadores.

## 4. Verificar critérios e limitações

- [ ] 4.1 Usar fixtures inteiramente sintéticas para estados, naturezas conhecidas/desconhecidas, ausências e paginação incompleta; verificar que nenhuma fixture é apresentada como evidência real.
- [ ] 4.2 Executar testes e builds afetados, revisar segurança/propriedade e confirmar que nenhum caminho de dashboard agrega valores ou transforma ausências em zero.
- [ ] 4.3 Validar OpenSpec e diff, conferir PRD/TDD/backlog/spec/API/UI coerentes e preservar sem alteração as observações existentes da FNR-005; não realizar coleta real nem reclassificar os 319 resultados históricos.

As caixas 2.1–4.3 permanecem abertas: a matriz da revisão 0.81 em `docs/BACKLOG.md` identifica implementações existentes e evidências locais, mas não executa as verificações restantes nem transfere aceite das FNRs ao explorador. O titular aprovou os quatro artefatos revisados e a versão 0.81 documentalmente, com ressalva para a referência antiga no TDD §13; isso não é aceite global. Antes de marcar cada tarefa, conferir o critério integral, a evidência e seus limites; tratar separadamente a correção/prova das duas falhas PostgreSQL históricas como encaminhamento **proposto** à FNR-024, sem declarar a suíte ampla aprovada. O ajuste da 3.1 foi aprovado como critério documental, não como aceite da implementação do explorador, e não autoriza sync ou arquivo.

## Workflow follow-up

- O incremento técnico integrado pelo PR #12 e suas tarefas já concluídas permanecem como histórico. O objetivo original de FNR-005/5.3 será registrado como substituído por revisão de escopo, não tecnicamente atendido, preservando o critério original; não converter a caixa pendente em conclusão técnica.
- Encerrar a FNR-005 somente após alinhamento, validação e aceite explícito do conteúdo revisado e de seus critérios próprios. Implementar este explorador ou o detector de recorrência não é pré-requisito desse encerramento; nenhum deles é declarado pronto por esta proposta.
- FNR-007/012/013/014 e as fatias de movimentos de FNR-015/022 podem avançar com identidade, campos de origem, paginação/cobertura e classificação conservadora, respeitando suas dependências próprias e sem total monetário.
- FNR-019 pode fornecer a base visual; FNR-021 depende da atualização e sessão. FNR-018 fornece resumos não monetários e FNR-023 reúne a visão descritiva, reutilizando a lista de FNR-022; revisar e aceitar as dependências do backlog antes de implementar cada entrega.
- Permanecem adiados o reconhecimento automático de compras e os indicadores financeiros originais de FNR-015/018/023, incluindo totais/quantidades de compras, distribuição monetária por categoria e evolução por competência. Não são considerados atendidos por resumos descritivos. Gasto líquido também exige vínculo confiável de estorno.
- Planejar recorrência em item/mudança próprios após histórico persistido, consulta protegida e fluxo de confirmação. A direção é candidato após padrão em três meses consecutivos, confirmação pelo usuário como assinatura/parcela/compras independentes e metadados de parcelamento separados. Tolerâncias de valor, datas e descrição permanecem pendentes e bloqueiam implementar o detector; valor igual sozinho não basta. Não criar capability ou implementar o detector nesta mudança.
- Indicadores de gasto só podem ser planejados em mudança posterior após evidência aprovada para reconhecer compras, cobertura classificatória suficiente, semântica temporal/competência e, para líquido, vínculo confiável de estornos.
- Esta etapa documental não executa coletas reais, código, commits, PR, sync, arquivamento ou publicação. A aprovação da direção não substitui aprovação do conteúdo final ou aceite de encerramento.
