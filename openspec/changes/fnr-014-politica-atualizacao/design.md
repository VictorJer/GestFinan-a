# Design

## Context

Ver [proposta](proposal.md) e as deltas de `connection-update-policy` e `sync-execution`. A FNR-013 já oferece `POST /api/connections/current/sync` com `Idempotency-Key`, `202` após commit de `SyncRun`/outbox, `409` para outra intenção durante execução ativa, e `GET /api/sync-runs/{id}`. Seu store serializa novas intenções por lock da linha da conexão. `FinancialConnection` possui `LastSuccessfulImportAtUtc` e `SourceUpdatedAtUtc`; a promoção preenche apenas o primeiro. Falhas e tentativas permanecem em `SyncRun`, não há frontend Angular nem fonte confiável atualmente populando a atualização na origem. A revisão 0.47 do backlog ainda descreve FNR-013 como não integrada, embora a `main` local contenha merge #27; não presumir publicação.

## Goals / Non-Goals

**Goals:**
- Decidir elegibilidade e bloqueio no backend antes de persistir nova intenção, compartilhando a serialização existente e respeitando replay/commit incerto.
- Oferecer ao futuro cliente da FNR-021 um contrato protegido para consultar o estado e solicitar atualização de entrada, sem depender do relógio da interface.
- Distinguir sucesso Finora, tentativa, estado de execução e horário informado pela origem; não fabricar um horário de origem nem disponibilidade de provedor.

**Non-Goals:**
- Criar SPA, componente, botão ou interceptação da entrada nesta entrega (FNR-020/021).
- Trocar RabbitMQ/outbox, alterar limites de retries internos aprovados na FNR-013, agendar tarefa com app fechado ou introduzir nova fonte de dados externos.
- Executar migration no destino, provisionar broker hospedado ou declarar a aplicação publicada.

## Decisions

### D1. Contratos HTTP separados para política, com aceitação compartilhada

Adicionar `GET /api/connections/current/sync-state` para consulta protegida e `POST /api/connections/current/sync/on-entry` para a intenção automática explícita; manter `POST /api/connections/current/sync` como pedido manual para compatibilidade. Ambos os POST exigem chave idempotente e convergem ao mesmo store/consumer da FNR-013. Entrada recente retorna `200` com decisão `not_required`, sem execução; intenção nova bloqueada por intervalo retorna `429` sanitizado com `nextAllowedAtUtc` e `Retry-After`; execução ativa continua `409` com referência própria; elegível aceita retorna `202` com o contrato atual. O GET descreve `updateNeeded`, `nextAllowedAtUtc` opcional, último sucesso, última tentativa e última execução visível. Não colocar gatilho implícito em GET de status ou listagem. Alternativa descartada: usar um único POST sem modo; ela não permite diferenciar a regra de dados recentes do pedido manual e poderia disparar importações em navegação.

### D2. Tempo e elegibilidade sob a mesma serialização persistente

Validar em configuração backend limiar padrão de 24 horas e intervalo mínimo padrão de cinco minutos, positivos e configuráveis. Após bloquear a linha de `FinancialConnection`, reler a chave (replay primeiro), execução ativa, último sucesso e a última intenção realmente aceita (maior `SyncRun.CreatedAtUtc` para conexão/titular) e avaliar com instante UTC do servidor capturado após adquirir o lock; construir o candidato com esse mesmo instante. Aplicar a condição `agora > últimoSucesso + limiar` à entrada; igualdade é recente. Pedido manual ignora antiguidade, mas não intervalo. Usar `CreatedAtUtc` de execução aceita para cooldown inclusive em falha, sem usar o `LastAttemptAtUtc` da conexão, que hoje não é atualizado nas falhas. Manter proteção de concorrência/constraint já existente para cruzamento de hosts; teste com relógio controlado e PostgreSQL descartável comprova a ordem do lock. Alternativas descartadas: check no controller (TOCTOU) e cache por instância (não converge entre hosts/reinícios).

### D3. Replays e migração de fingerprint sem quebrar clientes existentes

Preservar o fingerprint atual v1 para a rota manual existente e introduzir um fingerprint distinto/versionado para entrada automática. Repetir a mesma chave e modo devolve a execução aceita, mesmo terminal ou agora fora da elegibilidade, sem reiniciar cooldown; reutilizar chave em modo diferente retorna conflito sanitizado. A checagem deve ocorrer novamente dentro da transação após lock e antes da política, e a colisão no índice global permanece convergente. Em resultado de commit incerto, retornar indeterminado e instruir replay da mesma chave, nunca gerar automaticamente chave nova. Alternativa descartada: trocar o fingerprint manual para v2, que faria replays históricos válidos falharem.

### D4. Estado sanitizado derivado do histórico persistido

Projeção por titular e conexão única, usando `LastSuccessfulImportAtUtc`, `SourceUpdatedAtUtc` apenas quando preenchido com fonte confiável e última `SyncRun` ordenada por criação/ID; `lastAttemptAtUtc` vem da execução conhecida, e não do campo legado da conexão. `AUTH_REQUIRED` da última execução terminal suprime novas intenções automáticas até ação manual após renovação no provedor; nenhuma checagem externa para presumir renovação. Mostrar estado/código interno e cobertura conhecida da execução, sem valores, descrições ou identificadores externos. Último sucesso continua acessível mesmo com broker indisponível; falhas do banco retornam indisponibilidade, nunca dados presumidos. Alternativa descartada: inferir atualização na origem de um import bem-sucedido.

### D5. Separar verificação operacional de implementação local

Antes do código dependente, atualizar/revisar TDD DT-43 e backlog FNR-014/FNR-013 com as escolhas confirmadas e estado factual, sem transferir aprovação de versões anteriores. Antes da publicação, confrontar o intervalo local com limites/`Retry-After` efetivos do provedor sem coleta de dados pessoais não autorizada. As tentativas internas existentes da FNR-013 continuam regidas por seu orçamento/agenda; outro pedido não as substitui. Sem evidência de compatibilidade em ambiente hospedado MQ-P02/MQ-P04 ou migration de destino aplicada, esta entrega não libera deploy.

## Risks / Trade-offs

- [Intervalo de cinco minutos menor que limites reais do provedor] → verificar documentação/evidência autorizada e revisar política se necessário antes de publicar; nunca declarar limite externo validado por testes sintéticos.
- [Último sucesso concorre com aceitação no mesmo banco] → serializar decisão com a conexão e testar corrida com promoção, entrada e pedido manual em PostgreSQL descartável.
- [Relógios de hosts divergentes/espera pelo lock] → capturar o instante após lock em fronteira única e verificar monotonicidade dos instantes persistidos; nunca usar horário fornecido pelo cliente.
- [Atualização na origem indisponível] → campo nulo e explicação explícita; não derivar de `LastSuccessfulImportAtUtc`.
- [Repetições durante autorização inválida] → impedir auto-loop após `AUTH_REQUIRED`, preservar histórico; botão continua possível após orientação de renovação e intervalo.

## Migration Plan

- O desenho aproveita colunas existentes de conexão e `SyncRun`, sem exigir schema novo por padrão. Se a implementação exigir índice adicional para consulta da última intenção, validar migration somente em PostgreSQL descartável e preservar atualização administrativa explícita; nunca migrar no startup.
- Evoluir API de forma aditiva, mantendo rotas/respostas FNR-013 e compatibilidade de chaves manuais anteriores. Reversão operacional: desativar os novos gatilhos/rotas e retornar à política anterior somente por revisão aprovada, preservando execuções e outbox existentes; nunca reverter schema ou apagar histórico automaticamente.
