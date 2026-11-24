# Proposal

## Why

O catálogo FNR-016 e a consulta FNR-015 já permitem ler categorias e anotações, mas o titular ainda não consegue aplicar uma categoria ou editar uma observação por API. A FNR-017 completa a organização pessoal prevista em PRD RF06/RF15 sem alterar fatos recebidos da origem nem antecipar a interface FNR-022.

## What Changes

- Oferecer edição protegida de categoria e observação de um movimento persistido próprio, inclusive remoção independente de cada campo.
- Validar categoria inicial ou pessoal do próprio titular; “Sem categoria” permanece ausência virtual, não registro persistido. Rejeitar movimentos ausentes/alheios indistintamente e categorias inexistentes/alheias sem revelar sua titularidade.
- Aceitar observação de texto simples de até 500 caracteres, sem truncar; refletir alterações na consulta e no filtro/contagem por categoria já existentes.
- Preservar anotações após importação/reconexão e não alterar valor, data, descrição, natureza, situação, metadados de origem ou cobertura; não produzir agregados monetários nem iniciar sincronização.
- Registrar o merge já confirmado do PR #30 e a pendência de revisão do BACKLOG 0.55 antes da implementação dependente; nenhuma aprovação documental nova é presumida nesta proposta.

## Capabilities

### New Capabilities

- `transaction-annotations`: edição protegida e isolada da categoria/observação pessoal de movimentos, com leitura refletida pelos contratos de consulta existentes.

### Modified Capabilities

Nenhuma: `financial-data-persistence` já define separação, unicidade e integridade; `card-movement-query` já devolve categoria/observação e filtra/conta por categoria. Este incremento viabiliza a escrita, sem mudar seus requisitos.

## Impact

- Rastreabilidade: FNR-017; PRD RF06/RF15, §§7–8; TDD DT-35/36/44, §§6/10/11/15; depende das entregas FNR-015/016, com PR #30 integrado. Aprovação destes artefatos e de qualquer revisão canônica necessária precede código.
- Backend: nova rota protegida sob `/api/transactions/{id}`; caso de uso Application, persistência EF/PostgreSQL de `TransactionAnnotation`, leitura FNR-015, autorização/erros sanitizados e testes sintéticos. Não prever migration sem evidência de necessidade: o modelo já contém anotação e vínculos.
- Fora do escopo: interface Angular/seletor FNR-022, resumos FNR-018, recorrência FNR-038, renomeação/exclusão de categorias, regras de compra/competência, soma de movimentos, coleta Pluggy real e publicação.
