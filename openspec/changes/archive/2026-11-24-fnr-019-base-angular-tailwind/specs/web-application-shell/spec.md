# Spec Delta

## Purpose

Oferecer uma base de aplicação web navegável, acessível e honesta sobre as funcionalidades disponíveis, pronta para receber posteriormente as telas autenticadas do Finora sem exibir dados de demonstração como dados reais.

## ADDED Requirements

### Requirement: Página inicial pública sem dados simulados
O aplicativo SHALL abrir uma página inicial pública em português que identifique o Finora e informe que as funcionalidades de dados financeiros dependem de entregas posteriores, sem apresentar transações, indicadores ou estados de conexão fictícios como reais.

#### Scenario: Primeira visita sem API ou sessão
- **WHEN** uma pessoa abre a aplicação em execução local sem API acessível ou sessão autenticada
- **THEN** a página inicial é exibida sem exigir backend e sem valores, cartões, login simulado ou confirmação de conexão

### Requirement: Navegação corresponde às páginas entregues
O aplicativo SHALL oferecer acesso somente a páginas funcionais desta entrega; caminhos desconhecidos SHALL informar que a página não existe e oferecer retorno à página inicial. Rotas de dados e ações protegidas SHALL não ser apresentadas como disponíveis antes de suas respectivas entregas.

#### Scenario: Caminho inexistente
- **WHEN** a pessoa visita um caminho não reconhecido
- **THEN** recebe uma página de não encontrado com link funcional para o início

#### Scenario: Recursos futuros
- **WHEN** a pessoa consulta o menu na entrega da base
- **THEN** não encontra links ativos para login, cartão, movimentos, visão financeira, investimentos ou sincronização ainda não implementados

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
