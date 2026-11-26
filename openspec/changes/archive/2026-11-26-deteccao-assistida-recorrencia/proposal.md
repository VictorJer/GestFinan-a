# Proposal

## Why

O titular precisa distinguir padrões mensais que merecem revisão sem transformar coincidências ou metadados incompletos em classificação financeira automática. A direção aprovada para FNR-038 exige que o backend sugira somente “possível recorrência” após três meses consecutivos observados e que a decisão permaneça sob controle do usuário.

## What Changes

- Planejar a capability própria de recorrência assistida sobre os movimentos já persistidos e a lista autenticada existente: geração de candidatos no backend e confirmação/rejeição corrigível pelo titular como assinatura, parcela ou compras independentes.
- Registrar a política `recurrence-v1` aprovada explicitamente pelo titular no harness em 25/11/2026, com critérios reproduzíveis de valor, data, descrição, situação elegível e exemplos exclusivamente fictícios; alternativas anteriores não são limites da implementação.
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
- Dependências de produto: FNR-012, FNR-015, FNR-020 e FNR-022 têm entregas presentes no repositório; a FNR-023 também consta no histórico Git local. O plano e a política `recurrence-v1` foram aprovados explicitamente antes do código; o registro de aceite da implementação FNR-020 não foi encontrado e não é presumido. A FNR-024 depende desta entrega; a presença das dependências técnicas não autoriza antecipar seu aceite.
- Estado-base observado ao preparar a mudança: `FinancialTransaction` persistia identidade contextual, data técnica, situação, valor/moeda e metadados de parcela; havia importação da mesma identidade, `GET /api/transactions` protegido e lista Angular autenticada. Naquele ponto não havia detector nem decisão de recorrência; respostas HTTP/testes não demonstravam integração hospedada ou provedor real.
- O trabalho deve integrar domínio, persistência, sincronização, API e interface existentes, sem duplicar lista ou contrato de movimento. A política está aprovada; os detalhes de modelo/persistência e operações/DTOs serão revisados nos gates das tarefas 3.1 e 4.1 antes de implementar essas camadas. Não exige nova consulta real, tecnologia adicional nem mudança na regra conservadora da FNR-005.
- Estado factual posterior da implementação local, evidências e limitações: [tasks.md](tasks.md) e [AMBIENTE.md — FNR-038](../../../../docs/AMBIENTE.md#fnr-038--preservacao-e-validacao-integrada-local). O titular aceitou a implementação local em 26/11/2026; a integração e a publicação permanecem pendentes e a evidência local não comprova API/CORS/provedor reais.
