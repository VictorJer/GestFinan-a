# Spec Delta

## MODIFIED Requirements

### Requirement: Navegação protegida e acesso honesto
A aplicação SHALL permitir abrir sua área autenticada, incluindo cartões/conexão e movimentos, somente com sessão local válida e orientar novo login quando necessário. Na área protegida, SHALL apresentar somente dados próprios obtidos da API; a avaliação automática de atualização SHALL ocorrer apenas na entrada prevista pelo contrato de conexão, nunca ao abrir, filtrar ou paginar movimentos. A sessão local SHALL não ser tratada como autoridade sobre o acesso real da API.

#### Scenario: Acesso direto sem sessão
- **WHEN** uma pessoa abre diretamente a URL da área protegida sem sessão local válida
- **THEN** é encaminhada ao login e não vê conteúdo protegido

#### Scenario: Acesso autenticado
- **WHEN** o titular navega para a área protegida durante sessão local ainda válida
- **THEN** vê somente os cartões e o estado da conexão de sua sessão quando disponíveis, com avaliação de entrada conforme a política aprovada, ou a lista de movimentos próprios ao visitar o destino correspondente, sem dados de demonstração apresentados como reais

#### Scenario: Sessão encerrada na visão de cartão
- **WHEN** a sessão local termina enquanto uma consulta de cartões ou estado ainda está pendente
- **THEN** a área protegida deixa de exibir dados privados e uma resposta tardia não reabre acesso nem restaura o conteúdo

#### Scenario: Acesso direto à lista sem sessão
- **WHEN** uma pessoa abre diretamente o destino de movimentos sem sessão local válida
- **THEN** é encaminhada ao login sem consultar ou exibir movimentos, categorias ou anotações

#### Scenario: Sessão encerrada na lista
- **WHEN** a sessão local termina enquanto uma consulta de movimentos ou escrita de anotação/categoria ainda está pendente
- **THEN** a lista deixa de exibir dados privados e a resposta tardia não repõe conteúdo nem confirma a escrita para a nova sessão

#### Scenario: Navegação não repete intenção de entrada
- **GIVEN** uma avaliação automática já foi enviada para a sessão atual
- **WHEN** o titular abre movimentos, aplica filtros e volta para cartões/conexão
- **THEN** a aplicação não envia outra avaliação automática por causa da navegação, da consulta ou da paginação
