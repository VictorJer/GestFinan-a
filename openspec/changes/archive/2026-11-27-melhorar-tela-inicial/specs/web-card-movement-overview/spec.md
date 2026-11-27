# Spec Delta

## ADDED Requirements

### Requirement: Hierarquia visual da visão descritiva protegida
A visão SHALL organizar título, contexto dos filtros, contagem do conjunto, distribuições independentes, cobertura e acesso à lista única em níveis visuais distinguíveis, usando identidade Finora sem conferir destaque financeiro a dados inexistentes. SHALL identificar o universo persistido filtrado e a limitação temporal da cobertura perto dos dados correspondentes.

#### Scenario: Resumo autenticado com dados disponíveis
- **GIVEN** uma sessão válida e resposta descritiva com contagens, filtros e janela de importação confirmada
- **WHEN** o titular abre a visão descritiva
- **THEN** encontra primeiro o contexto da página e do conjunto filtrado, identifica separadamente natureza e situação, lê que a cobertura se refere apenas à janela solicitada e alcança o link para a lista única sem ver somas ou gráficos monetários

#### Scenario: Ausência de cobertura confirmada
- **WHEN** a visão recebe contagem de movimentos persistidos mas nenhuma janela de sucesso confirmada
- **THEN** destaca a cobertura como desconhecida, sem associar a contagem a histórico completo, sucesso de importação ou valor financeiro zero

### Requirement: Adaptação responsiva sem perda de contexto
A visão SHALL manter a mesma informação autorizada e o mesmo caminho para a lista em telas largas e estreitas; a apresentação em colunas SHALL se reorganizar sem esconder cobertura, contexto dos filtros ou estados operacionais. A navegação compartilhada SHALL continuar acessível aos destinos já entregues, sem criar caminhos públicos para dados protegidos.

#### Scenario: Visão em celular e largura intermediária
- **GIVEN** uma sessão válida e um resumo com natureza desconhecida, situação pendente e falha recente após sucesso anterior
- **WHEN** o titular visualiza a visão em celular ou largura intermediária
- **THEN** consegue ler esses estados separadamente, acessar a lista de movimentos e navegar para cartões/conexão e sair sem sobreposição, truncamento de informação essencial ou nova intenção de atualização

#### Scenario: Visão em desktop
- **WHEN** o titular visualiza a visão em largura desktop
- **THEN** os grupos descritivos e operacionais aproveitam a largura disponível com hierarquia legível, mantendo a ordem de leitura e o link para a lista única

### Requirement: Interação visual acessível e estados honestos
A apresentação da visão SHALL manter títulos em ordem compreensível, foco visível, texto com contraste legível e controles utilizáveis por teclado e toque. Carregamento, conjunto filtrado vazio, falha e resposta anterior da mesma sessão SHALL permanecer textualmente identificáveis, sem usar apenas cor, animação ou valores fictícios para comunicá-los.

#### Scenario: Navegação sem ponteiro ou com ampliação
- **WHEN** o titular percorre a visão por teclado ou amplia a interface
- **THEN** identifica o foco, a hierarquia de títulos, os estados e o link à lista sem conteúdo essencial oculto ou dependência exclusiva de ícones/cores

#### Scenario: Falha de leitura com resposta anterior autorizada
- **GIVEN** uma resposta anterior da mesma sessão e dos mesmos filtros ainda pode ser apresentada
- **WHEN** a nova leitura do resumo falha
- **THEN** a visão diferencia visual e textualmente o dado anterior do erro e oferece repetição somente da leitura, sem fingir atualização concluída nem iniciar sincronização
