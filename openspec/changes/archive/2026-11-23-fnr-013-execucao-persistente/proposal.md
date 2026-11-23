# Proposal

## Why

A FNR-012 já importa e promove snapshots de movimentos, mas a FNR-013 ainda precisa fazer a execução sobreviver à resposta HTTP e à reinicialização da API sem perder o resultado, executar duas vezes em paralelo ou deixar uma `SyncRun` eternamente pendente. O TDD aprovado define a direção de persistir `SyncRun` e outbox na mesma transação, entregar o comando por RabbitMQ e processá-lo em consumidor da API; esta mudança detalha esse contrato antes de qualquer implementação.

## What Changes

- Definir solicitação autenticada e convergente por chave idempotente, aceitação persistida com resposta `202`, recuperação de commit incerto e consulta autorizada da `SyncRun`.
- Especificar publicação atômica da mesma `SyncRun` aceita e outbox, com janela persistida; todas as tentativas, posse, fencing na promoção, falha e cancelamento permanecem vinculados a essa execução, sem criar execuções terminais paralelas.
- Definir estados, contadores e códigos de falha sanitizados, classificação de falhas, orçamento persistido de tentativas e tratamento observável de mensagens sem sucesso.
- Integrar o consumidor ao importador FNR-012 existente; não recalcular a janela em retomadas nem manter transação PostgreSQL aberta durante chamadas HTTP ao provedor.
- Preservar os gates técnicos MQ-P01–P04 do TDD, separando os necessários à conclusão local dos gates de hospedagem que permanecem para a publicação futura; versões, topologia e limites não aprovados não serão presumidos.

## Capabilities

### New Capabilities
- `sync-execution`: aceitação, acompanhamento, entrega durável, exclusão mútua e retomada idempotente das execuções de sincronização.

### Modified Capabilities
- Nenhuma. `transaction-import` permanece responsável pela coleta e promoção consistente; esta mudança especifica a orquestração durável em uma capability separada.

## Impact

- Application/API: solicitação autenticada, resposta de aceitação, consulta protegida de `SyncRun` e execução interna pelo consumidor.
- Infrastructure/Domain: integração de outbox/consumer e posse/fencing persistentes; reaproveitamento e extensão compatível do modelo `SyncRun` existente.
- Persistência: mudanças de schema/migration podem ser necessárias para outbox e coordenação; validar localmente em PostgreSQL descartável. Qualquer preflight, backup, aplicação ou verificação em destino continua gate anterior a publicação dependente, não faz parte da preparação desta mudança.
- Mensageria: direção aprovada no TDD usa RabbitMQ/MassTransit na API, sem novo projeto worker. Versões, broker/plano hospedado, topologia e parâmetros operacionais permanecem gates documentados, sem provisionamento nesta proposta.
- Registro documental a incluir no próximo PR relacionado: correção do BACKLOG 0.46 com os merges FNR-012 #25 (`02523a9`) e #26 (`b582bdd2`) e link da proposta arquivada; não abrir PR separado apenas para repetir esse encerramento.
- Fora de escopo: política de quando atualizar da FNR-014, agendamento com a aplicação parada, publicação/deploy, provisionamento de Neon/broker hospedado e execução de migration fora do PostgreSQL descartável nesta etapa.
