# Spec Delta

## MODIFIED Requirements

### Requirement: Aceitação persistente e resposta honesta
Uma solicitação autorizada SHALL aplicar a política de atualização da conexão antes de criar uma intenção nova; se elegível, SHALL persistir atomicamente uma única `SyncRun`, sua janela e a obrigação de entrega antes de responder como aceita. Cada intenção SHALL usar uma chave de idempotência estável, vinculada ao titular e à conexão, para resolver repetição e resultado de commit incerto. Replay válido SHALL preservar a execução anterior mesmo quando uma intenção nova estaria bloqueada. A resposta `202` SHALL referenciar a execução e não SHALL significar entrega ou conclusão da importação.

#### Scenario: Solicitação aceita
- **GIVEN** o titular autenticado solicita sincronização elegível de uma conexão que lhe pertence
- **WHEN** a criação da execução e sua obrigação durável são confirmadas
- **THEN** a API responde `202` com a referência da `SyncRun` persistida, ainda que a entrega ou importação não tenha ocorrido

#### Scenario: Falha antes da confirmação da aceitação
- **GIVEN** uma solicitação autenticada e uma falha ao persistir a execução ou sua obrigação de entrega
- **WHEN** a transação de aceitação é revertida
- **THEN** a API não responde `202` e não deixa uma execução parcialmente aceita

#### Scenario: Rollback confirmado da aceitação
- **GIVEN** nenhuma `SyncRun` ou obrigação foi persistida para a chave de idempotência
- **WHEN** o banco confirma que a transação de aceitação sofreu rollback
- **THEN** a resposta informa que o pedido não foi aceito e a repetição com a mesma chave pode criar uma única `SyncRun`, sujeita à política vigente

#### Scenario: Resultado do commit de aceitação desconhecido
- **GIVEN** a conexão é interrompida durante a confirmação e o cliente não sabe se o commit foi concluído
- **WHEN** a API não consegue provar commit nem rollback
- **THEN** o resultado é informado como indeterminado, sem afirmar que não existe execução, e uma repetição com a mesma chave converge para a `SyncRun` e obrigação já gravadas ou cria exatamente um par se o rollback for confirmado e a política permitir

#### Scenario: Chave de idempotência reutilizada com outro pedido
- **GIVEN** uma chave já está vinculada a uma execução aceita para titular e conexão
- **WHEN** a chave é repetida com titular, conexão ou janela incompatível
- **THEN** a API rejeita a reutilização sem alterar ou expor a execução previamente vinculada

#### Scenario: Entrada com dados recentes
- **GIVEN** uma conexão possui sucesso completo ainda dentro do limiar configurado
- **WHEN** chega uma nova intenção automática de entrada autenticada
- **THEN** a API informa que a importação não é necessária, sem criar `SyncRun`, outbox ou chamada externa

#### Scenario: Intervalo mínimo de entrada ou botão
- **GIVEN** houve intenção nova aceita para a conexão há menos de cinco minutos e não se trata de replay
- **WHEN** chega outra intenção automática ou manual
- **THEN** a API informa bloqueio temporário e próximo instante permitido, sem responder `202` nem criar `SyncRun` ou obrigação

#### Scenario: Replay preservado durante intervalo
- **GIVEN** uma intenção foi aceita e uma segunda intenção estaria bloqueada pela política
- **WHEN** o titular repete a chave original com a mesma intenção
- **THEN** recebe `202` com a `SyncRun` original, mesmo se já estiver terminal, sem reiniciar intervalo ou fazer nova importação
