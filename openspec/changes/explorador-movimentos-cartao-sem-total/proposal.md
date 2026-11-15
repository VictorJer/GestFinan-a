# Proposal

## Why

A evidência sanitizada da FNR-005 permite expor movimentos e algumas naturezas/situações observáveis, mas não sustenta reconhecer automaticamente todas as compras nem atribuir competência financeira. Este incremento separa a exploração transparente dos movimentos dos indicadores de gasto que dependem desses gates, evitando que subtotais ou ausências sejam apresentados como total ou zero.

## What Changes

- Definir uma visão de movimentos do cartão que apresente os registros disponíveis, valores por movimento, data de origem, moeda, situação e natureza quando identificável, junto à cobertura e ao estado de completude da consulta.
- Manter `PAGAMENTO` ambíguo como natureza desconhecida; preservar `PAGAMENTO_FATURA` e outras naturezas somente quando suas regras aprovadas forem coerentes. Exibir casos desconhecidos explicitamente, sem inferi-los como compra ou descartá-los silenciosamente.
- Não calcular, retornar ou apresentar qualquer agregado monetário nesta entrega, qualquer que seja seu rótulo: valor total, subtotal, bruto/líquido, média, agrupamento, série ou gráfico. Somente o valor individual de cada movimento pode aparecer. Contagens não monetárias de natureza/situação e cobertura são descritivas e devem indicar o universo observado; ausência de dado não equivale a zero.
- Manter fora desta entrega gasto total/líquido, quantidade de compras reconhecidas, gasto por categoria e evolução mensal financeira. Esses indicadores exigem regra de compra e, conforme o eixo temporal, semântica de data/competência validada; abatimento líquido também exige vínculo confiável de estorno.
- Separar o incremento técnico da FNR-005 já validado da investigação financeira pendente: tarefas 5.1–5.2, 5.4–5.15 e 6.1–6.3, já registradas como concluídas, permanecem evidência histórica; 5.3 continua aberta e não é convertida em aceite por esta proposta.
- Preservar os 319 buckets históricos de `billForecastDate` como inconclusivos, sem reclassificação retroativa, nova coleta ou generalização da confirmação local de um caso.

## Capabilities

### New Capabilities
- `card-movement-explorer`: consulta e apresenta movimentos do cartão, cobertura, situações e naturezas identificáveis, mantendo ambiguidades explícitas e sem declarar gasto total.

### Modified Capabilities

Nenhuma capability consolidada existente é alterada nesta proposta. A normalização conservadora da FNR-005 é dependência e permanece conforme seus artefatos aprovados.

## Impact

- Produto: futura revisão do PRD, especialmente RF04, RF07, regras de cartão e critérios de pronto, distinguindo explorador de movimentos de indicadores financeiros completos.
- Arquitetura: futura revisão do TDD §§10–13 e §19 para contratos da consulta/dashboard, cobertura, estados de desconhecido e bloqueios de agregação; a interface deve apenas apresentar os resultados do backend.
- Planejamento: manter em FNR-005 o incremento técnico já validado separado de 5.3, ainda bloqueada. Fatias de FNR-007/012/013/014 e listagem de FNR-015/022 podem avançar preservando movimentos e desconhecidos, dentro das dependências próprias; FNR-019/021 não dependem de classificação financeira. A parte descritiva de cobertura/natureza/situação de FNR-018 e a visão de movimentos de FNR-023 exigem decomposição/revisão de dependências antes de avançar. Totais financeiros em FNR-015/018/023, categorias de gasto e evolução por competência continuam bloqueados por compra/competência; resultado líquido também exige vínculo de estorno. Não há implementação frontend de dashboard neste repositório nesta etapa.
- Evidência: preservar as observações agregadas existentes (319 recebidos/únicos na última coleta autorizada, sem payloads individuais), os limites de natureza e o resultado histórico inconclusivo de `billForecastDate`. Nenhuma consulta real, dado novo ou reclassificação retroativa faz parte desta proposta.
