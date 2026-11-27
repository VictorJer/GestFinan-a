# Proposal

## Why

O titular consegue entrar na aplicação local, mas o PostgreSQL de desenvolvimento não contém conexão, cartão ou movimento; as leituras protegidas não têm um item persistido para consultar. A leitura diagnóstica anterior da Pluggy não criou vínculo no Finora, e cadastrar esse vínculo diretamente por SQL, sem as invariantes do domínio, pode comprometer identidade, propriedade e reconexões futuras.

## What Changes

- Oferecer uma operação **administrativa local e explícita** para vincular pela primeira vez o item Pluggy já configurado de forma protegida ao único titular existente, após validar destino, identidade do titular, estado do item e presença inequívoca de cartão de crédito.
- Criar, na mesma transação, a conexão financeira e a identidade externa ativa exigida pela importação atual; repetir a operação com o mesmo vínculo confirmado não cria duplicatas nem altera histórico, e item ou conexão divergente são recusados sem substituição silenciosa.
- Fornecer resultados operacionais sanitizados e um roteiro de validação local com dados sintéticos. Uma prova real futura só ocorre com autorização própria e não é declarada concluída por esta proposta.
- Não acrescentar tela de conexão, endpoint público, migrations, ingestão automática, credenciais no repositório ou mudança nas regras financeiras.

## Capabilities

### New Capabilities

- `administrative-connection-provisioning`: vinculação inicial controlada do item Pluggy ao titular existente em ambiente local, sem exposição de identificadores ou alteração de conexões existentes.

### Modified Capabilities

Nenhuma. A persistência e a importação já exigem propriedade do titular, identidade externa ativa e reconexão conservadora; esta mudança acrescenta uma entrada administrativa para criar o estado inicial compatível, sem alterar esses contratos.

## Impact

- Planejado: comando administrativo roteado em `backend/Finora.Api/Program.cs`, handler em `backend/Finora.Application/`, adaptadores e transação em `backend/Finora.Infrastructure/`, testes sintéticos em `backend/tests/Finora.Api.Tests/` e instruções em `docs/AMBIENTE.md`/`README.md` quando a implementação for aprovada.
- Rastreabilidade: PRD RF01/RF02/RF14/RF16 e §13; TDD §§5/10/12/20; backlog M3/M5. O backlog ainda não possui item próprio para esse provisionamento e deverá registrar seu escopo e dependências antes da implementação; FNR-034 permanece reservado ao futuro fluxo iniciado na interface. A mudança ativa FNR-024 deve ter seu encerramento factual reconciliado antes de abrir uma segunda implementação principal.
- A API e o frontend locais podem continuar disponíveis para inspeção, mas esta proposta não configura RabbitMQ na API, não consulta dados pessoais, não inicia sincronização e não comprova entrega publicada. O vínculo sozinho deixa a lista vazia até uma importação bem-sucedida pelos gates apropriados.
