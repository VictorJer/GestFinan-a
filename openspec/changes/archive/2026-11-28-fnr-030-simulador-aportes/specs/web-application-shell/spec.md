# Spec Delta

## MODIFIED Requirements

### Requirement: Navegação corresponde às páginas entregues
O aplicativo SHALL oferecer início e login públicos e, durante sessão válida, acesso à área protegida de cartão/conexão, à lista de movimentos, à visão descritiva integrada, à carteira manual de investimentos e ao simulador de aportes. Caminhos desconhecidos SHALL informar que a página não existe e oferecer retorno à página inicial. Ações ainda não entregues SHALL não ser apresentadas como disponíveis.

#### Scenario: Caminho inexistente
- **WHEN** a pessoa visita um caminho não reconhecido
- **THEN** recebe uma página de não encontrado com link funcional para o início

#### Scenario: Recursos futuros
- **WHEN** a pessoa consulta o menu após a entrega da visão descritiva integrada, da carteira manual e do simulador
- **THEN** encontra acesso ao login ou, conforme sua sessão local, à área protegida de cartão/conexão, à lista de movimentos, à visão descritiva, à carteira de investimentos e ao simulador, mas não encontra links ativos para outras funções não implementadas

#### Scenario: Estado do menu após saída ou expiração
- **WHEN** a sessão local termina por logout ou expiração
- **THEN** o menu deixa de oferecer acesso à área protegida, inclusive à lista de movimentos, à visão descritiva, à carteira e ao simulador, e volta a oferecer login, sem apresentar a sessão como ativa

#### Scenario: Acesso público sem intenção de atualização
- **WHEN** uma pessoa abre a página inicial ou o login sem sessão válida
- **THEN** a navegação não consulta dados financeiros nem solicita sincronização

#### Scenario: Navegação entre cartões e movimentos
- **GIVEN** há sessão local válida
- **WHEN** o titular navega pelo menu em desktop ou celular entre cartões/conexão e movimentos
- **THEN** alcança as duas páginas protegidas, e a navegação para movimentos não solicita nova avaliação automática ou importação

#### Scenario: Navegação para a visão integrada
- **GIVEN** há sessão local válida
- **WHEN** o titular abre a visão descritiva pelo menu em desktop ou celular
- **THEN** alcança a página protegida de resumo, sem solicitar avaliação automática ou importação por causa da navegação

#### Scenario: Navegação para carteira
- **GIVEN** há sessão local válida
- **WHEN** o titular abre a carteira pelo menu em desktop ou celular
- **THEN** alcança a página protegida de investimentos próprios, sem solicitar atualização ou importação dos movimentos do cartão

#### Scenario: Navegação para o simulador
- **GIVEN** há sessão local válida
- **WHEN** o titular abre o simulador pelo menu em desktop ou celular
- **THEN** alcança a página protegida de cenários sem solicitar leitura da carteira ou importação do cartão apenas por navegar
