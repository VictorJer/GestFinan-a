# administrative-connection-provisioning Specification

## Purpose

Permitir ao operador vincular inicialmente um item Pluggy válido ao único titular em um ambiente local autorizado, sem expor identificadores externos nem substituir vínculos financeiros existentes.

## Requirements

### Requirement: Operação administrativa isolada e destino local verificado
O sistema SHALL disponibilizar uma operação administrativa explícita, fora do servidor HTTP, que SHALL rejeitar parâmetros sensíveis em argumentos e qualquer banco não confirmado como o PostgreSQL local autorizado antes de consultar ou gravar dados. SHALL não criar schema, aplicar migrations ou iniciar a operação no startup.

#### Scenario: Destino local confirmado
- **GIVEN** a operação dispõe de configuração protegida para o banco local correto e o operador confirmou sua execução interativa
- **WHEN** solicita o vínculo inicial sem argumentos adicionais
- **THEN** somente o fluxo administrativo é executado, sem listener HTTP ou alteração automática de schema

#### Scenario: Destino remoto, de testes ou configuração incerta
- **WHEN** a configuração do banco está ausente, aponta a host remoto, ao banco de integração descartável ou não permite confirmar o destino local aprovado
- **THEN** o comando recusa a operação antes de acessar esse banco, sem fallback para outra conexão

#### Scenario: Entrada por argumentos ou canal não interativo
- **WHEN** o operador passa identificador de item, credenciais ou outro argumento, ou não pode confirmar a operação em terminal interativo
- **THEN** o comando recusa a vinculação sem abrir listener, consultar a origem ou persistir vínculo

### Requirement: Titular único e item externo comprovado antes do vínculo
A operação SHALL exigir exatamente um titular existente e um item obtido da configuração protegida. SHALL conferir na Pluggy, antes da gravação, o estado e autorização do item e uma seleção não ambígua de conta de cartão pertencente a ele. SHALL recusar respostas incompletas, conflito de titular ou item não elegível sem afirmar que o cartão foi importado.

#### Scenario: Titular e item elegíveis
- **GIVEN** há exatamente um titular no banco autorizado e credenciais/item configurados fora do repositório
- **WHEN** a verificação da origem confirma item utilizável e seleção válida de cartão vinculada ao mesmo item
- **THEN** o vínculo pode prosseguir sem receber identificadores externos do navegador ou da linha de comando

#### Scenario: Item inacessível ou cartão ambíguo
- **WHEN** a autenticação da origem falha, a autorização do item está bloqueada, não há cartão elegível ou há múltiplos cartões sem seleção protegida inequívoca
- **THEN** nenhuma conexão é gravada e a falha é informada sem copiar a resposta do provedor

#### Scenario: Titular ausente ou múltiplo
- **WHEN** o banco autorizado não contém titular ou contém mais de um titular
- **THEN** nenhum usuário é escolhido arbitrariamente e nenhum vínculo é gravado

### Requirement: Vínculo inicial atômico e repetição segura
Um sucesso SHALL persistir a conexão do titular e sua identidade externa ativa como um único conjunto confirmado, sem criar cartões ou movimentos fictícios. Repetir o mesmo vínculo íntegro SHALL ser idempotente; item ativo/aposentado em conflito, vínculo preexistente divergente ou concorrência SHALL não alterar propriedade, item, anotações ou histórico por substituição silenciosa.

#### Scenario: Primeiro vínculo
- **GIVEN** não há conexão própria nem registro prévio do item e a verificação externa foi bem-sucedida
- **WHEN** o operador confirma a operação
- **THEN** uma conexão do titular e exatamente uma identidade ativa correspondente são confirmadas juntas, sem cartão, movimento ou horário de importação bem-sucedida fabricados

#### Scenario: Repetição do mesmo vínculo íntegro
- **GIVEN** a conexão própria e a identidade ativa já correspondem ao mesmo item configurado
- **WHEN** a operação é repetida com o item ainda válido
- **THEN** retorna estado distinto de vínculo já existente, sem alterar identificadores, histórico ou horários de sucesso

#### Scenario: Item divergente, aposentado ou corrida
- **WHEN** existe conexão própria com outro item, identidade ativa/aposentada conflitante ou duas operações concorrem pelo mesmo vínculo
- **THEN** no máximo um novo vínculo é confirmado e não há adoção, reativação ou troca implícita de identidade externa

#### Scenario: Falha antes ou durante a confirmação
- **WHEN** falha a gravação de uma das duas entidades ou a confirmação da transação se torna incerta
- **THEN** o comando não declara sucesso; antes do commit não publica vínculo parcial e, diante de confirmação incerta, não promete rollback

### Requirement: Saída privada e fronteira da importação
A operação SHALL emitir somente estados e códigos sanitizados, sem e-mail, item, identificadores de conta/cartão, credenciais, payloads, valores financeiros ou exceções brutas. SHALL não emitir sessão, iniciar sincronização ou declarar que cartões e movimentos foram importados ao apenas criar o vínculo.

#### Scenario: Vínculo confirmado
- **WHEN** a transação inicial é confirmada
- **THEN** o operador recebe apenas confirmação sanitizada do vínculo, e as consultas de cartões e movimentos continuam representando fielmente o conteúdo persistido, inclusive vazio

#### Scenario: Falha externa ou operacional com conteúdo sensível
- **WHEN** configuração, entrada ou resposta externa contém sentinelas privadas e a operação falha
- **THEN** stdout, stderr e logs não reproduzem esses valores nem anunciam importação concluída
