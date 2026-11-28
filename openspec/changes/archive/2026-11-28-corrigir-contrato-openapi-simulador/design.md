# Design

## Context

Ver `proposal.md` para a causa e os links das duas execuções de CI. O PR #53 trouxe um controlador `[Authorize]` com `[HttpPost("calculate")]` sob `[Route("api/simulations")]`; PRD RF12, TDD §11/§13 (DT-59), `investment-simulation` e `api-exploration` já estabelecem sua proteção e publicação local. `AuthenticationAvailabilityTests.NormalApiSurfaceContainsHealthLoginAndProtectedLogout` já reconhece `/api/simulations/calculate`, método `post` e requisito Bearer. Em contrapartida, `ExplorationTests.DevelopmentPublishesHealthContract` omite essa operação das duas coleções comparadas exatamente com o documento real. O primeiro `CollectionAssert.AreEquivalent` falha antes de chegar à matriz de autenticação; corrigir só a lista de caminhos deixaria a próxima asserção desatualizada. `ExplorationIsolationTests` já verifica 404 para Swagger/OpenAPI em Production/Staging e `GET /health` 200 nesses ambientes.

## Goals / Non-Goals

**Goals:**

- Alinhar a dupla de expectativas estritas de `ExplorationTests` ao método/rota e à proteção já aprovados e publicados, sem mascarar caminhos ou políticas desconhecidos.
- Preservar a distinção entre `/health` e login anônimos e operações protegidas, bem como os testes de isolamento por ambiente.
- Reproduzir localmente o job falho sem banco ou broker, deixando resultados remotos de PR e merge identificados separadamente.

**Non-Goals:**

- Alterar políticas, endpoint, respostas, cálculos, dados, documentação de produção ou configurações de workflow.
- Afirmar que a correção reverte o check histórico já falho no commit integrado, ou que evidência local constitui CI aprovado.

## Decisions

### D1 — Correção restrita à matriz de contrato

No teste `DevelopmentPublishesHealthContract`, acrescentar literalmente o caminho `/api/simulations/calculate` à coleção esperada e a tupla `("/api/simulations/calculate", "post")` à lista `protectedOperations`. Manter intactas a comparação exata dos caminhos, a extração de métodos com `security`, a exigência de um único esquema `Bearer` resolvível, a lista `publicOperations` e as asserções de `/health`. Atualizar o comentário do bloco lógico em português para incluir a nova operação. Alternativa descartada: comparação por subconjunto ou exclusão da rota; isso deixaria passar novos endpoints indevidos. Não alterar o teste já correto de `AuthenticationAvailabilityTests` nem a API.

### D2 — Gate de divergência e regressões proporcionais

Antes de aplicar, conferir no checkout atualizado o método e a autorização do controlador e o contrato OpenAPI coberto por `AuthenticationAvailabilityTests`. Se a API efetiva não corresponder a `POST` protegido e às specs aprovadas, interromper a correção de expectativa e apresentar a divergência para revisão do plano. Depois da edição aprovada, executar testes focais `ExplorationTests`, `ExplorationIsolationTests` e `AuthenticationAvailabilityTests` (em especial a operação do simulador) e reproduzir `dotnet restore backend/Finora.slnx --locked-mode`, `dotnet build backend/Finora.slnx --no-restore` e exatamente a seleção do job `backend-no-postgres` de `.github/workflows/ci.yml`, com seleção não vazia e zero skips. Validar o OpenSpec estritamente e conferir `git diff --check`; relatar quantidade de testes, falhas e limites sem inferir resultado remoto futuro. Alternativa descartada: rodar apenas o teste que falhou, sem atingir a matriz de segurança e a seleção completa do job.

## Risks / Trade-offs

- [Adicionar só o caminho supera a primeira falha, mas rompe a matriz de Bearer] → incluir as duas entradas e manter todas as asserções exatas.
- [Uma rota inesperada poderia ser tratada como permitida] → não afrouxar as coleções; em caso de divergência da spec, parar e pedir revisão.
- [Testes locais em Windows não representam o runner Linux] → reproduzir a seleção do CI localmente e aguardar o check de um novo PR, se esse encaminhamento vier a ser autorizado.
- [Histórico do backlog ainda descreve PR #53 como aberto] → preparar registro factual prospectivo sob revisão documental própria; não reescrever aprovação ou histórico anterior.

## Migration Plan

Nenhuma migration ou alteração de dados. Esta etapa produz somente planejamento. Após aprovação específica e solicitação posterior de implementação, a correção poderá seguir em PR próprio com checks do novo head; o merge antigo e seus checks falhos permanecem fatos históricos. Nenhum commit, push, PR, merge ou deploy está autorizado por este design.
