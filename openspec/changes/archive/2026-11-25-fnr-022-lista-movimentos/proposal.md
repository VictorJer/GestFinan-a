# Proposal

## Why

A API já entrega movimentos paginados, filtros, cobertura, categorias e edição de anotações, mas a área autenticada ainda mostra apenas cartões e conexão. A FNR-022 torna esses dados consultáveis e organizáveis pelo titular sem antecipar indicadores financeiros ou reconstruir a visão integrada reservada à FNR-023.

## What Changes

- Disponibilizar uma rota autenticada de movimentos, acessível pela navegação desktop e móvel, com lista paginada, filtros combináveis, contagem do conjunto filtrado e apresentação honesta de data, natureza, situação, parcelas e cobertura.
- Permitir listar e criar categorias pessoais, selecionar a opção virtual “Sem categoria” e editar ou limpar categoria e observação de um movimento próprio sem recarregar a aplicação; refletir alterações confirmadas em lista, filtros e contagens.
- Tratar carregamento, consulta vazia, dados incompletos, entrada inválida, falta de permissão e falhas de rede/servidor com mensagens seguras e ações de recuperação; preservar isolamento de sessão e acessibilidade em desktop/celular.
- Não calcular nem exibir agregados monetários. Não alterar o contrato do backend, acionar sincronização ao consultar/navegar, implementar dashboard FNR-023, recorrência FNR-038 ou apresentar dados sintéticos como reais.

## Capabilities

### New Capabilities

- `web-card-movement-list`: consulta, filtragem, paginação e organização pessoal de movimentos na interface autenticada, com cobertura explícita e somente valores monetários individuais.

### Modified Capabilities

- `web-application-shell`: liberar na navegação o destino de movimentos somente após sua entrega, mantendo os demais destinos futuros indisponíveis.
- `web-authenticated-session`: estender a área protegida à lista de movimentos, sem permitir acesso ou retenção de dados privados após o fim da sessão e sem criar outra avaliação automática de atualização.

## Impact

- Rastreabilidade: FNR-022 (dependências FNR-015/016/017/020), PRD RF04/RF05/RF06/RF09/RF15 e M-03 descritiva; TDD §§11/13/15; `docs/DESING.md` apenas como referência visual. Os contratos existentes das specs `card-movement-query`, `category-catalog`, `transaction-annotations`, `web-card-connection` e `web-authenticated-session` são preservados onde não modificados.
- Implementação futura concentrada no frontend Angular (rota, menu, cliente da API, estado de consulta e edição, componentes e testes), reutilizando Bearer/interceptor, base configurada e geração de sessão existentes; sem migration, nova dependência, rota financeira ou alteração no backend.
- A mudança aberta `explorador-movimentos-cartao-sem-total` continua como registro transversal: sua fatia de API já está coberta pela FNR-015, e suas tarefas de visão integrada pertencem à FNR-023 após esta lista. Esta proposta não marca aquelas tarefas como concluídas nem sincroniza/arquiva a mudança.
- O Git local registra o merge do PR #39/FNR-021 em `main`, mas `docs/BACKLOG.md` 0.70 ainda registra PR/aceite documental pendentes; reconciliar esse histórico e obter as aprovações aplicáveis antes de implementar, sem presumir publicação, API real ou aceite da revisão 0.70.
