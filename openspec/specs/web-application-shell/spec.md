# web-application-shell Specification

## Purpose

Oferecer uma base de aplicação web navegável, acessível e honesta sobre as funcionalidades disponíveis, pronta para receber posteriormente as telas autenticadas do Finora sem exibir dados de demonstração como dados reais.

## Requirements

### Requirement: Página inicial pública sem dados simulados
O aplicativo SHALL abrir uma página inicial pública em português que identifique o Finora e informe que as funcionalidades de dados financeiros dependem de entregas posteriores, sem apresentar transações, indicadores ou estados de conexão fictícios como reais.

#### Scenario: Primeira visita sem API ou sessão
- **WHEN** uma pessoa abre a aplicação em execução local sem API acessível ou sessão autenticada
- **THEN** a página inicial é exibida sem exigir backend e sem valores, cartões, login simulado ou confirmação de conexão

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

### Requirement: Estrutura responsiva e acessível
O aplicativo SHALL manter a página e sua navegação legíveis e utilizáveis por teclado em telas pequenas e grandes, com região principal, identificação da navegação, foco visível e textos que não dependem apenas de cor para transmitir seu significado.

#### Scenario: Navegação por teclado
- **WHEN** a pessoa navega pelos controles disponíveis usando Tab, Shift+Tab e Enter
- **THEN** pode identificar o foco, acionar os links e chegar ao conteúdo principal sem depender de ponteiro

#### Scenario: Visualização em larguras distintas
- **WHEN** a página é visualizada em larguras de celular e desktop, inclusive próximo a 560 e 900 px
- **THEN** o conteúdo e os links continuam acessíveis sem sobreposição ou corte de informações essenciais

### Requirement: Base estática sem exposição de credenciais
O aplicativo SHALL funcionar nesta entrega sem segredos de servidor ou dados pessoais embutidos, sem solicitar acesso bancário nem invocar rotas financeiras apenas por abertura de página ou navegação entre as páginas públicas da base.

#### Scenario: Inspeção da aplicação inicial
- **WHEN** uma pessoa abre e percorre as páginas públicas disponíveis
- **THEN** não são feitas requisições de autenticação, importação, consulta financeira ou sincronização e não são exibidos tokens, chaves ou identificadores externos
