# Proposal

## Why

A FNR-018 já oferece contagens e cobertura por `GET /api/dashboard`, e a FNR-022 já oferece lista, filtros e organização na interface, mas ainda não há uma visão integrada para interpretar esses dados em conjunto. A FNR-023 entrega essa leitura descritiva sem antecipar os indicadores financeiros adiados nem reconstruir a lista.

## What Changes

- Acrescentar uma visão protegida com contagem do universo filtrado, distribuição por natureza e situação, data técnica explicitada e cobertura/última execução, usando somente a resposta existente de `GET /api/dashboard`.
- Permitir chegar à lista FNR-022 com os mesmos filtros efetivamente aplicados na visão; conferir a contagem do conjunto filtrado, não os itens de uma página, e conservar a lista como único lugar para paginar, criar categorias e editar anotações.
- Diferenciar carga, conjunto filtrado vazio, ausência de importação confirmada, falha recente, falha de leitura e sessão encerrada. Manter leitura acessível em HTML e navegação por teclado no desktop e celular.
- Disponibilizar o destino protegido na navegação existente somente quando houver sessão válida, sem alterar a entrada em cartões/conexão e sem solicitar sincronização ao abrir ou filtrar a visão.
- Não criar somas, médias, séries ou gráficos monetários; não presumir compras confirmadas, competência financeira, histórico bancário completo nem recorrência detectada.

## Capabilities

### New Capabilities

- `web-card-movement-overview`: leitura integrada e acessível de contagens descritivas e cobertura da API, com passagem coerente de filtros para a lista existente, sem métricas financeiras derivadas.

### Modified Capabilities

- `web-application-shell`: a navegação passa a incluir a visão integrada protegida após sua entrega, mantendo destinos públicos, responsividade e remoção de links protegidos ao encerrar a sessão.

## Impact

- **Rastreabilidade:** FNR-023; PRD RF05/RF07/RF09 e M-03; TDD §§11/13/14/19; dependências FNR-018/020/022. O PR #40/FNR-022 consta como merge em `main` no Git local (`78d5121`), embora `docs/BACKLOG.md` 0.75 ainda diga “integração pendente”; reconciliar o registro documental antes de alegar estado atualizado, sem transferir aceite documental.
- **Código futuro:** frontend Angular em `frontend/src/app/auth/area/`, rotas e navegação em `frontend/src/app/app.routes.ts` e `app.html`, testes de API/estado/componente e documentação do fluxo. Consumir contratos existentes sem alterar `GET /api/dashboard`, `GET /api/transactions`, schema, migrations ou o provedor.
- **Fronteiras:** a mudança aberta `explorador-movimentos-cartao-sem-total` contém tarefas amplas que se sobrepõem à FNR-015/018/022/023; registrar correspondência e evidência sem marcá-las automaticamente como concluídas, reescrever o explorador ou duplicar sua capability. FNR-038 e indicadores monetários seguem entregas próprias.
- **Evidência:** testes locais com fixtures sintéticas não comprovam API/CORS/provedor reais nem publicação. Artefatos desta mudança aguardam revisão e aprovação humana antes de qualquer implementação.
