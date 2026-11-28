# Proposal

## Why

O PR #53/FNR-030 foi integrado, mas o check `backend-no-postgres` do PR e o CI do merge em `main` falharam no mesmo teste: `ExplorationTests.DevelopmentPublishesHealthContract` compara 17 caminhos esperados com 18 caminhos publicados. A rota protegida `POST /api/simulations/calculate` já está prevista no PRD RF12, no TDD §11/§13 e nas specs aprovadas; falta atualizar a matriz estrita desse teste, sem mudar a API.

## What Changes

- Ajustar somente as expectativas de `backend/tests/Finora.Api.Tests/ExplorationTests.cs`: incluir `/api/simulations/calculate` entre os caminhos esperados e `(path, "post")` entre as operações protegidas com Bearer.
- Preservar as comparações exatas das rotas e da matriz de segurança, além das verificações de `GET /health` anônimo, login anônimo, documentação Swagger/OpenAPI em Development e indisponibilidade desses recursos em Production/Staging.
- Planejar regressões focais e a mesma seleção `backend-no-postgres` do CI; registrar separadamente os resultados do PR e do merge, sem reinterpretar uma execução falha como aprovada.
- Registrar a correção e seus limites no backlog durante a execução, como revisão factual pendente de aprovação própria; não alterar PRD/TDD nem antecipar FNR-031.

## Capabilities

### New Capabilities

Nenhuma.

### Modified Capabilities

Nenhuma. `api-exploration` já exige descrever todos os endpoints implementados e aplicar Bearer às operações protegidas; `investment-simulation` já exige autenticação. Esta é uma correção de teste para refletir o contrato aprovado, sem alteração de requisito ou comportamento. Por isso, `.openspec.yaml` declara `skip_specs: true` e não há delta spec.

## Impact

Escopo de código previsto: apenas `backend/tests/Finora.Api.Tests/ExplorationTests.cs`; registro factual futuro em `docs/BACKLOG.md`. Sem mudança em controlador, endpoint, autenticação efetiva, UI, banco, migration, dependências ou CI. Referências: PRD RF12, TDD §§11/13 (DT-59), specs `api-exploration` e `investment-simulation`, FNR-030 arquivada e PR #53. O [check do PR #53](https://github.com/VictorJer/GestFinan-a/actions/runs/46162660260) concluiu com `backend-no-postgres` FAILURE (416/417 passaram) e os outros dois jobs SUCCESS; o [CI do merge `460a29d`](https://github.com/VictorJer/GestFinan-a/actions/runs/46180992740) também concluiu com `backend-no-postgres` FAILURE (416/417) e `backend-postgres`/`frontend` SUCCESS. Ambos apontam para `ExplorationTests.cs:44`, esperado 17 e obtido 18. Nenhum desses resultados prova publicação ou aceite desta correção.
