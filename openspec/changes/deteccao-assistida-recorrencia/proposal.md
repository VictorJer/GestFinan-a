# Proposal

## Why

O titular precisa distinguir padrões mensais que merecem revisão sem transformar coincidências ou metadados incompletos em classificação financeira automática. A direção aprovada para FNR-038 exige que o backend sugira somente “possível recorrência” após três meses consecutivos observados e que a decisão permaneça sob controle do usuário.

## What Changes

- Especificar uma capability própria de recorrência assistida, incluindo geração de candidatos no backend e confirmação/rejeição corrigível pelo titular como assinatura, parcela ou compras independentes.
- Definir no desenho alternativas e uma recomendação provisória para tolerâncias de valor, datas e descrição, com exemplos exclusivamente fictícios e falsos positivos. Nenhum limite fica aprovado por este documento; a aprovação explícita das tolerâncias e dos critérios executáveis continua bloqueando implementação.
- Tratar metadados de parcelamento fornecidos pela Pluggy separadamente da heurística temporal; duplicatas e parcelas apenas previstas não contam como cobranças mensais observadas.
- Preservar confirmações do titular em sincronizações e mantê-las separadas dos dados originais. A decisão não altera valor, data, descrição, natureza, status ou competência financeira e não habilita agregados monetários.
- Reaproveitar as referências da mudança existente do explorador e dos documentos canônicos. Não adicionar detector à FNR-005, não duplicar a capability do explorador e não reabrir o escopo/aceite da FNR-005.

## Capabilities

### New Capabilities

- `transaction-recurrence-assistance`: sugerir candidatos a recorrência a partir de histórico mensal observado e permitir ao titular decidir e corrigir a classificação assistida.

### Modified Capabilities

Nenhuma. A capability do explorador de movimentos continua sendo uma entrega separada e não tem seus requisitos alterados por esta mudança.

## Impact

- Rastreabilidade: FNR-038, PRD RF17/VP-07, TDD DT-35/DT-40/DT-41/DT-55 e roadmap de M4; reutiliza `openspec/changes/explorador-movimentos-cartao-sem-total` como referência, sem modificá-la.
- Dependências de produto: histórico persistido/importado (FNR-012), consulta protegida (FNR-015), sessão (FNR-020) e lista/fluxo do titular (FNR-022), além da aprovação das tolerâncias antes da implementação. A posição permanece após esses pré-requisitos e antes da consolidação de testes/CI do núcleo (FNR-024); não antecipa tarefas dependentes.
- Estado observado no repositório: há classificadores puros e leitura local Pluggy no backend, mas não há persistência, endpoints financeiros nem frontend implementados para esse fluxo. Os metadados de origem já incluem campos opcionais de parcelamento; isso não equivale a um modelo de confirmação ou detector implementado.
- Mudanças futuras de domínio, API protegida, persistência, sincronização, interface e testes sintéticos serão definidas/implementadas somente após revisão e aprovação desta especificação e das tolerâncias. Não exige nova consulta real, tecnologia adicional ou mudança na regra conservadora da FNR-005.
