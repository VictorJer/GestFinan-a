# api-exploration Specification

## Purpose

Oferecer documentação interativa da API para inspecionar contratos e testar chamadas locais, limitando sua exposição ao ambiente de desenvolvimento.

## Requirements

### Requirement: Documento dos endpoints disponíveis
Em Development, a API SHALL servir um documento OpenAPI válido em `/openapi/v1.json`, descrevendo os endpoints implementados, seus métodos e respostas. O documento SHALL incluir `GET /health` e não apresentar operações financeiras ainda inexistentes.

#### Scenario: Consulta do documento local
- **WHEN** um cliente solicita `/openapi/v1.json` em Development
- **THEN** recebe HTTP 200 com documento OpenAPI que descreve `GET /health` e sua resposta JSON HTTP 200
- **AND** o documento não contém credenciais nem compras fictícias apresentadas como dados reais

### Requirement: Execução interativa de chamadas
Em Development, a API SHALL disponibilizar Swagger UI em `/swagger`, carregando o documento local e permitindo executar `GET /health` pelo recurso Try it out sem ferramenta externa.

#### Scenario: Teste pelo navegador
- **GIVEN** a API está em execução local em Development
- **WHEN** o desenvolvedor abre `/swagger` e executa `GET /health` pelo Try it out
- **THEN** a interface mostra o código HTTP 200 e o corpo JSON `{"status":"ok"}` retornados pela API
- **AND** os recursos da interface são servidos pela aplicação sem depender de CDN externa

### Requirement: Documentação indisponível fora de desenvolvimento
A API SHALL não disponibilizar Swagger UI, seus recursos ou o documento OpenAPI quando o ambiente não for Development. Desabilitar a documentação SHALL não desabilitar a resposta técnica de `/health`.

#### Scenario: Execução em Production
- **GIVEN** a API executa com ambiente Production
- **WHEN** um cliente solicita `/swagger`, `/swagger/index.html`, `/swagger/swagger-ui-bundle.js` ou `/openapi/v1.json`
- **THEN** cada endereço retorna HTTP 404 sem servir documentação ou recursos da interface
- **AND** `GET /health` permanece disponível com a resposta técnica mínima

#### Scenario: Execução em Staging
- **GIVEN** a API executa com ambiente Staging
- **WHEN** um cliente solicita `/swagger/index.html` ou `/openapi/v1.json`
- **THEN** recebe HTTP 404 sem acesso à documentação
