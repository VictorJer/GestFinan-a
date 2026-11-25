# Spec Delta

## MODIFIED Requirements

### Requirement: Navegação corresponde às páginas entregues
O aplicativo SHALL oferecer acesso somente a páginas funcionais desta entrega: início e login públicos e uma área informativa protegida durante sessão válida. Caminhos desconhecidos SHALL informar que a página não existe e oferecer retorno à página inicial. Rotas de dados e ações protegidas ainda não entregues SHALL não ser apresentadas como disponíveis.

#### Scenario: Caminho inexistente
- **WHEN** a pessoa visita um caminho não reconhecido
- **THEN** recebe uma página de não encontrado com link funcional para o início

#### Scenario: Recursos futuros
- **WHEN** a pessoa consulta o menu após a entrega da sessão no navegador
- **THEN** encontra acesso ao login ou à área informativa conforme seu estado local, mas não encontra links ativos para cartão, movimentos, visão financeira, investimentos ou sincronização ainda não implementados

#### Scenario: Estado do menu após saída ou expiração
- **WHEN** a sessão local termina por logout ou expiração
- **THEN** o menu deixa de oferecer acesso à área protegida e volta a oferecer login, sem apresentar a sessão como ativa
