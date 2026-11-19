# administrative-user-access Specification

## Purpose

Permitir que o operador autorizado provisione e recupere o acesso do único titular por comandos administrativos explícitos, preservando sua identidade, seus vínculos e a confidencialidade das credenciais.

## Requirements

### Requirement: Execução administrativa explícita e isolada
O sistema SHALL oferecer `user-create` e `user-reset-password` sem iniciar listener HTTP, solicitar acesso à Pluggy ou exigir configuração JWT. SHALL aceitar somente o nome exato do comando e rejeitar argumentos adicionais antes de resolver serviços ou acessar banco. SHALL não expor cadastro ou recuperação por HTTP nem provisionar usuários no startup.

#### Scenario: Comando administrativo válido
- **WHEN** o operador executa somente `user-create` ou `user-reset-password` com a configuração de banco válida
- **THEN** o processo executa apenas o fluxo administrativo escolhido, sem iniciar servidor HTTP nem consultar provedor externo

#### Scenario: Argumento sensível ou comando inválido
- **WHEN** a invocação inclui senha, e-mail, opção adicional ou nome de comando não reconhecido
- **THEN** o processo retorna código 2 antes de acessar banco, sem repetir o conteúdo dos argumentos na saída

#### Scenario: Inicialização HTTP normal
- **WHEN** a API inicia sem comando administrativo e sem configuração de banco ou Pluggy
- **THEN** nenhum usuário é criado ou alterado e o diagnóstico mínimo continua disponível no contrato existente

### Requirement: Entrada administrativa protegida
Os comandos SHALL solicitar e-mail interativamente e ler senha/nova senha e confirmação sem eco, sem aceitar credenciais por argumentos, arquivos, configuração ou entrada redirecionada. SHALL não alterar nem normalizar o conteúdo da senha. Confirmação divergente, entrada inválida ou cancelamento antes da persistência SHALL não alterar o usuário.

#### Scenario: Senha confirmada sem exposição
- **WHEN** o operador informa e confirma uma senha em terminal interativo
- **THEN** nenhum caractere da senha é exibido, seu conteúdo é preservado para validação e nenhuma cópia é gravada em arquivo ou configuração

#### Scenario: Confirmação divergente
- **WHEN** a senha e a confirmação diferem
- **THEN** o processo retorna código 2 sem criar usuário nem alterar credencial existente

#### Scenario: Entrada redirecionada ou terminal indisponível
- **WHEN** o comando recebe pipe, stdin redirecionado ou não dispõe de leitura protegida em terminal
- **THEN** recusa a execução com código 2, sem fallback para leitura com eco ou configuração de senha

#### Scenario: Cancelamento antes da gravação
- **WHEN** o operador cancela com Ctrl+C ou encerra a entrada antes da confirmação/persistência
- **THEN** o comando termina sem alteração do usuário, com código 130 para cancelamento ou 2 para entrada incompleta

### Requirement: Política uniforme de senha
A criação e a redefinição SHALL exigir senha de pelo menos 6 caracteres, com maiúscula, minúscula, número e caractere não alfanumérico, e no mínimo um caractere distinto. SHALL rejeitar senha que não cumpra qualquer regra sem persistir alteração e sem revelar a senha em mensagens de validação.

#### Scenario: Senha inválida em qualquer operação
- **WHEN** o operador confirma senha com menos de 6 caracteres ou sem uma das classes exigidas em criação ou reset
- **THEN** o comando retorna código 2, informa somente regras violadas por mensagens fixas e mantém o estado persistido anterior

#### Scenario: Mesma política na criação e no reset
- **WHEN** uma senha sintética atende todas as regras e é confirmada para uma operação permitida
- **THEN** a operação não a rejeita por regras de composição diferentes entre criação e redefinição

### Requirement: Provisionamento idempotente do único titular
O comando de criação SHALL criar no máximo um titular, com identificador não vazio e e-mail válido. SHALL comparar e-mails pela normalização de identidade da aplicação, ignorando espaços externos e diferenças de caixa. Repetir criação do titular existente SHALL retornar código 0 com estado distinto de criação, sem pedir senha nem alterar identificador, credencial ou dados associados. Outro e-mail SHALL ser recusado.

#### Scenario: Primeira criação
- **WHEN** não existe titular e o operador fornece e-mail válido e senha confirmada válida
- **THEN** exatamente um usuário é persistido com credencial protegida por hash e o processo retorna código 0 com estado `created`

#### Scenario: Repetição com caixa e espaços externos diferentes
- **GIVEN** o titular já existe
- **WHEN** `user-create` recebe o mesmo e-mail com diferenças de caixa ou espaços externos
- **THEN** retorna código 0 com estado `already_exists`, sem solicitar senha e sem alterar usuário, hash, versão de segurança ou vínculos

#### Scenario: Tentativa de criar outro titular
- **WHEN** já existe titular e a criação recebe e-mail diferente após normalização
- **THEN** retorna código 4 sem criar outro usuário, sem substituir o existente e sem revelar seu e-mail

#### Scenario: E-mail inválido ou estado de múltiplos titulares
- **WHEN** o e-mail informado é inválido ou o banco já contém mais de um usuário
- **THEN** a operação não cria nem modifica usuário e retorna respectivamente código 2 ou 4, sem escolher um titular arbitrariamente

### Requirement: Exclusividade sob concorrência
Criações administrativas concorrentes SHALL preservar no máximo um titular, inclusive quando recebem e-mails diferentes. A criação que perder a disputa SHALL não redefinir credencial nem confirmar sucesso como nova criação. Redefinições concorrentes SHALL ser serializadas ou rejeitadas como conflito, sem publicar alterações parciais.

#### Scenario: Duas criações do mesmo e-mail
- **WHEN** duas invocações simultâneas tentam criar o mesmo titular em base sem usuário
- **THEN** somente uma retorna `created`, a outra retorna `already_exists` e o usuário conserva a credencial da criação confirmada

#### Scenario: Duas criações de e-mails diferentes
- **WHEN** duas invocações simultâneas tentam criar titulares diferentes em base sem usuário
- **THEN** somente uma criação é confirmada, a outra retorna conflito com código 4 e o banco termina com um único usuário

#### Scenario: Dois resets concorrentes
- **WHEN** dois resets válidos são solicitados simultaneamente para o titular
- **THEN** as alterações confirmadas correspondem a uma ordem serial ou uma operação informa conflito, sem confirmação de estado parcialmente gravado

### Requirement: Recuperação sem recriar identidade
O reset SHALL localizar o titular pelo e-mail normalizado e redefinir sua senha sem senha antiga ou envio de e-mail. SHALL preservar identificador, e-mail e vínculos financeiros, alterando credencial/segurança e revogando todas as sessões ainda não revogadas do titular na mesma transação. Usuário ausente SHALL não ser criado. Sucesso SHALL ocorrer somente após confirmação conjunta da gravação.

#### Scenario: Reset confirmado
- **GIVEN** existe um titular com dados financeiros vinculados e múltiplas sessões
- **WHEN** o operador confirma nova senha válida diferente da anterior e o reset é gravado com sucesso
- **THEN** retorna código 0 com estado `password_reset`, a nova senha valida e a antiga não, sem alterar o identificador ou seus vínculos
- **AND** todas as sessões anteriormente não revogadas desse titular ficam revogadas e seus tokens deixam de autorizar novas chamadas protegidas

#### Scenario: Usuário ausente
- **WHEN** `user-reset-password` recebe e-mail que não corresponde ao titular
- **THEN** retorna código 3 com estado `not_found`, sem criar usuário, sem pedir nova senha e sem revelar qual e-mail existe

#### Scenario: Falha de persistência do reset
- **WHEN** a alteração de credencial ou a revogação falha antes da tentativa de commit
- **THEN** credencial, dados de segurança e estado das sessões conservam o estado anterior
- **AND** o comando retorna falha operacional 1 ou conflito 4, sem anunciar reset ou revogação concluídos

#### Scenario: Titular sem sessões
- **WHEN** um titular existente sem sessões tem a senha redefinida com sucesso
- **THEN** o comando confirma o reset e a revogação sem criar sessão, exigir login prévio ou tratar zero sessões como falha

#### Scenario: Sessões já revogadas e expiradas
- **WHEN** o reset encontra sessões já revogadas e sessões expiradas ainda sem revogação
- **THEN** preserva as datas de revogação existentes e marca também as sessões expiradas ainda não revogadas, sem excluir registros ou reativar sessões

#### Scenario: Senha rejeitada ou identidade conflitante
- **WHEN** a nova senha viola a política, a identidade da pré-consulta tornou-se obsoleta ou há conflito de concorrência
- **THEN** o reset mantém seus códigos existentes de rejeição/conflito sem alterar parcialmente credencial ou revogação

### Requirement: Resultados operacionais sem vazamento
Os comandos SHALL emitir apenas estados, categorias de erro e mensagens fixas permitidas. SHALL não incluir e-mail, senha, hash, token de reset, connection string ou exceção bruta em stdout, stderr ou logs, inclusive em falhas de inicialização. Código 0 SHALL distinguir usuário criado, já existente e senha redefinida; falha operacional SHALL não ser apresentada como senha inválida.

#### Scenario: Banco indisponível ou schema ausente
- **WHEN** a conexão configurada falha ou as tabelas necessárias não existem
- **THEN** retorna código 1 com falha operacional sanitizada, sem criar schema, executar migrations ou expor configuração/exceção

#### Scenario: Configuração ausente ou malformada
- **WHEN** não existe configuração válida de conexão para o comando
- **THEN** retorna código 2 com mensagem fixa de configuração, sem fallback para outro banco ou reflexão do valor recebido

#### Scenario: Erros com sentinelas sensíveis
- **WHEN** dados de teste incluem sentinelas em senha, e-mail, token, argumentos, conexão ou mensagens de exceção
- **THEN** nenhuma sentinela aparece em stdout, stderr ou logs capturados, nem nas mensagens dos serviços de identidade e persistência

### Requirement: Limite explícito da recuperação nesta entrega
A operação SHALL não emitir JWT nem criar sessão. O reset confirmado SHALL informar `status=password_reset session_revocation=confirmed` com código 0 somente após gravar senha e revogação conjuntamente. Falha SHALL não anunciar essa confirmação. A documentação SHALL preservar os limites históricos da FNR-008 e não apresentar o reset isolado como RF14 completo ou autorização de publicação.

#### Scenario: Reset antes da entrega de sessões e revogação
- **WHEN** é consultada a evidência histórica de reset da FNR-008, anterior à integração da revogação FNR-010
- **THEN** o registro conserva o aviso original de revogação não implementada como resultado daquela versão
- **AND** esse aviso não é atribuído ao sucesso da versão atual nem convertido retroativamente em prova de revogação

#### Scenario: Reset após a entrega de revogação
- **WHEN** o reset da versão com FNR-010 confirma senha e revogação
- **THEN** emite somente `User admin: operation=reset_password status=password_reset session_revocation=confirmed` no stdout, com código 0
- **AND** não emite o aviso antigo `session_revocation=not_implemented`, token ou identificadores sensíveis

#### Scenario: Preparação da publicação autenticada
- **WHEN** é avaliada a prontidão da versão autenticada para publicação
- **THEN** a entrega isolada da FNR-008 não satisfaz o gate de reset com revogação
- **AND** evidências próprias de integração/testes/aceite FNR-010 são exigidas, sem dispensar FNR-011/020 e demais gates aplicáveis

### Requirement: Falha administrativa sem recuperação parcial
O reset SHALL falhar fechado quando não puder revogar sessões, inclusive por schema ausente. Timeout SHALL ser falha operacional, sem retry automático. Cancelamento anterior à tentativa de commit SHALL não publicar senha ou revogação e SHALL usar código 130. Falha durante a tentativa de commit SHALL usar código 1 sem prometer rollback, mesmo se causada por cancelamento.

#### Scenario: Tabela de sessões ausente
- **WHEN** o comando encontra credencial válida no banco mas o schema necessário às sessões está ausente
- **THEN** retorna código 1 sanitizado e mantém a credencial anterior, sem oferecer fallback de reset sem revogação ou executar migration

#### Scenario: Timeout aguardando outra mutação
- **WHEN** o reset excede seu prazo aguardando login, logout ou outro reset
- **THEN** retorna código 1 sem confirmar sucesso nem publicar alterações parciais e sem repetir automaticamente a mutação

#### Scenario: Cancelamento antes do commit
- **WHEN** o reset é cancelado antes da tentativa de commit, inclusive após salvar a credencial ou alterar sessões dentro da transação
- **THEN** termina com código 130 sem persistir a nova senha nem a revogação

#### Scenario: Confirmação de commit perdida
- **WHEN** a transação de senha e revogação é confirmada pelo banco mas o processo perde sua confirmação
- **THEN** retorna código 1 sanitizado, sem anunciar sucesso nem afirmar que a senha antiga ou as sessões foram restauradas
- **AND** o estado confirmado no banco contém senha e revogação juntas, nunca somente uma delas
