# Spec Delta

## Purpose

Permitir verificar que a API está atendendo requisições sem consultar dados financeiros ou apresentar dependências externas como validadas.

## ADDED Requirements

### Requirement: Resposta técnica mínima
A API SHALL disponibilizar `GET /health` sem exigir credenciais, retornando HTTP 200 e JSON com somente `status` igual a `ok` quando puder atender à requisição. Essa resposta indica apenas disponibilidade da aplicação, não saúde de banco ou provedor.

#### Scenario: API em execução
- **WHEN** um cliente envia `GET /health` à API em execução
- **THEN** recebe HTTP 200 com tipo de conteúdo JSON e corpo `{"status":"ok"}`
- **AND** não precisa fornecer token ou credenciais bancárias

#### Scenario: Dependências financeiras não configuradas
- **GIVEN** a API não tem conexão de banco nem credenciais de provedor configuradas
- **WHEN** um cliente envia `GET /health`
- **THEN** recebe a mesma resposta técnica mínima sem consulta a serviços externos
- **AND** a resposta não inclui versões, configurações, caminhos, segredos ou dados financeiros
