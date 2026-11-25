# Spec Delta

## MODIFIED Requirements

### Requirement: Navegação corresponde às páginas entregues
O aplicativo SHALL oferecer início e login públicos e uma área protegida de cartão/conexão durante sessão válida. Caminhos desconhecidos SHALL informar que a página não existe e oferecer retorno à página inicial. Rotas de movimentos, visão integrada, investimentos e ações ainda não entregues SHALL não ser apresentadas como disponíveis.

#### Scenario: Caminho inexistente
- **WHEN** a pessoa visita um caminho não reconhecido
- **THEN** recebe uma página de não encontrado com link funcional para o início

#### Scenario: Recursos futuros
- **WHEN** a pessoa consulta o menu após a entrega da visão de cartão/conexão
- **THEN** encontra acesso ao login ou à área protegida de cartão/conexão conforme sua sessão local, mas não encontra links ativos para movimentos, visão integrada, investimentos ou outras funções não implementadas

#### Scenario: Estado do menu após saída ou expiração
- **WHEN** a sessão local termina por logout ou expiração
- **THEN** o menu deixa de oferecer acesso à área protegida e volta a oferecer login, sem apresentar a sessão como ativa

#### Scenario: Acesso público sem intenção de atualização
- **WHEN** uma pessoa abre a página inicial ou o login sem sessão válida
- **THEN** a navegação não consulta dados financeiros nem solicita sincronização
