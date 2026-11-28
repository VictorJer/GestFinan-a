# Proposal

## Why

O checkpoint P0 local foi aceito com ressalvas e a carteira manual FNR-029 já está no histórico da `main`; falta o simulador P1 previsto em RF12 antes da primeira publicação. O titular precisa explorar projeções sem confundi-las com posições reais nem com promessa de retorno.

## What Changes

- Adicionar cálculo protegido no backend para projeção mensal de valor inicial, aportes ao fim de cada mês, taxa anual efetiva e prazo, com série, total aportado e rendimento projetado.
- Oferecer página autenticada com entradas e validação em português, resultado/gráfico comparativo e avisos sobre incerteza, impostos, taxas e inflação.
- Conforme escolha explícita do titular, permitir guardar e comparar cenários somente na memória da sessão e preencher, por ação explícita, o valor inicial a partir do total atual da carteira própria; não gravar cenários nem alterar investimentos.
- Expor rota e navegação do simulador apenas com a funcionalidade entregue, preservando o menu público e o tratamento de sessão existente.

## Capabilities

### New Capabilities

- `investment-simulation`: cálculo, validação, apresentação, comparação temporária e preenchimento opcional a partir da carteira manual do titular.

### Modified Capabilities

- `web-application-shell`: incluir o simulador entregue na navegação autenticada, sem expor função indisponível ao público.

## Impact

- RF12/PRD §8/§13, BACKLOG FNR-030, TDD §§11/13/16 e referência visual `docs/DESING.md` §6.5; FNR-031 e FNR-028 permanecem mudanças próprias.
- Nova rota `POST /api/simulations/calculate` no backend e página/cliente/estado Angular protegidos, além de ajustes de rota/menu e testes. Integração de leitura apenas com `GET /api/investments` já existente para o botão de preenchimento.
- Sem migration, cotações, persistência de cenários, leitura de transações de cartão, mudança no CRUD/resumo da carteira ou publicação nesta mudança.
- O BACKLOG 0.109 ainda descreve o PR #52 como aberto, mas o grafo Git local já contém seu merge `1f56500`; reconciliar o registro factual em revisão documental própria antes de tratá-lo como evidência atual de checks/review/publicação. TDD 0.47 mantém revisão documental própria pendente; decisões anteriores não são reabertas.
