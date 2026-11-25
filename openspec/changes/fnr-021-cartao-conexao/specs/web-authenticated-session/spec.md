# Spec Delta

## MODIFIED Requirements

### Requirement: Navegação protegida e acesso honesto
A aplicação SHALL permitir abrir sua área autenticada somente com sessão local válida e orientar novo login quando necessário. Na área protegida, SHALL apresentar cartões e estado de conexão próprios obtidos da API e avaliar atualização conforme o contrato de entrada, sem revelar dados ao visitante ou atribuir à sessão local autoridade sobre o acesso real da API.

#### Scenario: Acesso direto sem sessão
- **WHEN** uma pessoa abre diretamente a URL da área protegida sem sessão local válida
- **THEN** é encaminhada ao login e não vê conteúdo protegido

#### Scenario: Acesso autenticado
- **WHEN** o titular navega para a área protegida durante sessão local ainda válida
- **THEN** vê somente os cartões e o estado da conexão de sua sessão quando disponíveis, com avaliação de entrada conforme a política aprovada, sem movimentos da futura lista ou dados de demonstração apresentados como reais

#### Scenario: Sessão encerrada na visão de cartão
- **WHEN** a sessão local termina enquanto uma consulta de cartões ou estado ainda está pendente
- **THEN** a área protegida deixa de exibir dados privados e uma resposta tardia não reabre acesso nem restaura o conteúdo
