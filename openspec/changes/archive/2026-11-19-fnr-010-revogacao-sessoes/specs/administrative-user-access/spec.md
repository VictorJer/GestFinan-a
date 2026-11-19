# Spec Delta

## MODIFIED Requirements

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

## ADDED Requirements

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
