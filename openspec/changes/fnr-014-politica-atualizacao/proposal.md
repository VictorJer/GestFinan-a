# Proposal

## Why

A FNR-013 já aceita e executa sincronizações duráveis, mas ainda aceita qualquer nova intenção autenticada sem decidir quando os dados estão antigos ou limitar tentativas manuais. A FNR-014 define essa política antes de integrar o botão e a entrada autenticada na interface, preservando histórico e sinalizando falhas sem confundir aceitação com sucesso.

## What Changes

- Definir elegibilidade no servidor: na entrada autenticada, solicitar atualização quando nunca houve importação completa ou o último sucesso tem mais de 24 horas; filtros e navegação não iniciam importação.
- Aplicar intervalo mínimo de cinco minutos por conexão, configurável e compartilhado entre entrada automática e pedido manual, contado da última intenção nova aceita inclusive quando sua execução falha; informar o próximo instante permitido sem afirmar que o provedor garantiu nova atualização.
- Expor estado sanitizado da conexão: último sucesso no Finora, atualização na origem quando disponível, última tentativa e estado da execução pertinente; distinguir falha/autorização expirada, dados antigos e cobertura desconhecida.
- Adaptar a aceitação existente para impor a política a novas intenções, preservando replay por chave idempotente, exclusão mútua e resposta `202` somente após persistência. Reutilizar retry/retomada da FNR-013, sem criar outro executor ou rotina com a aplicação fechada.
- Delimitar o contrato backend para a futura entrada/botão da FNR-021; não construir a interface Angular nesta mudança. Conferir limites reais do provedor e reconciliar as revisões canônicas antes de implementar/publicar comportamento dependente.

## Capabilities

### New Capabilities

- `connection-update-policy`: elegibilidade de atualização e estado observável da conexão para o titular, sem prometer atualização da origem.

### Modified Capabilities

- `sync-execution`: a aceitação de uma nova intenção passa a aplicar a política de entrada/manual antes do commit, sem alterar convergência de replays e execuções ativas.

## Impact

- RF08/RF16, backlog FNR-014 e TDD §§6/12, DT-32/33/43. A escolha de cinco minutos, compartilhados por entrada automática e pedido manual, foi confirmada pelo titular nesta conversa; sua reconciliação com o TDD (que ainda a chama de proposta) e a revisão factual do encerramento FNR-013 ficam como gates documentais antes do código dependente.
- API/Application/Infrastructure: consultas protegidas de estado e elegibilidade, integração na aceitação persistente e testes PostgreSQL/HTTP para concorrência, falha, limites e dados sanitizados. A resposta/consulta de `SyncRun` existente permanece compatível; eventuais migrations são validadas em banco descartável e só aplicadas em destino pelo processo administrativo previsto no TDD.
- FNR-020/021 implementarão a entrada autenticada, botão, polling e apresentação visual; FNR-015/022 continuarão apenas leitura. Sem coleta real, broker hospedado, deploy, agendamento em segundo plano ou liberação dos gates MQ-P02/MQ-P04 nesta proposta.
