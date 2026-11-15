# Proposal

## Why

O cartão Nubank foi visualizado pelo autor no painel da Pluggy, mas ainda não há prova de leitura pelo backend .NET. FNR-004 reduz esse risco antes de persistência e dashboard, após FNR-001/002/003 concluídas.

## What Changes

- Introduzir um comando local explícito para autenticar na Pluggy, verificar a conexão configurada, identificar a conta de cartão e recuperar lançamentos reais, sem iniciar servidor HTTP.
- Consumir segredos externos ao repositório e produzir somente resumo sanitizado de etapas, contadores e resultado; distinguir retorno vazio de prova de leitura com lançamentos.
- Implementar o cliente externo na Infrastructure e o caso de uso na Application, respeitando as fronteiras existentes e a adoção de MediatR condicionada ao gate DT-49.
- Cobrir o fluxo com respostas simuladas em MSTest e executar a prova real separadamente, com evidência sanitizada e instruções reproduzíveis.
- Não incluir banco, importação persistente, normalização financeira completa (FNR-005), RabbitMQ/MassTransit, login, frontend ou publicação.

## Capabilities

### New Capabilities

- `pluggy-local-read`: consulta local explícita da conexão, cartão e lançamentos, com isolamento de segredos e diagnóstico sanitizado.

### Modified Capabilities

Nenhuma. `api-diagnostics` e `api-exploration` permanecem inalteradas: o health não consulta a Pluggy e nenhum endpoint financeiro será adicionado ao Swagger.

## Impact

- Implementação futura: `backend/Finora.Api` (modo de comando/composição), `backend/Finora.Application` (query/handler e contratos), `backend/Finora.Infrastructure` (HTTPS/configuração), testes e locks afetados.
- Documentação futura: instruções/evidências em `docs/AMBIENTE.md`, resumo no `README.md` e status verdadeiro de FNR-004 em `docs/BACKLOG.md`; nenhum desses arquivos é alterado nesta proposta.
- Referências canônicas: [backlog FNR-004](../../../../docs/BACKLOG.md), [PRD 0.3](../../../../docs/PRD.md) RF01, RNF-01/02/03/09/10 e [TDD 0.5](../../../../docs/TDD.md) §§3/4/7, DT-27/38/48/49. RF02 e FNR-005 orientam limites, sem serem declaradas concluídas por esta prova.
- Dependência externa: API Pluggy e autorização pessoal da aplicação sobre o item; acesso real ainda não verificado. MediatR 12.x requer seleção/aprovação de versão exata e verificação de licença, segurança e compatibilidade antes de instalação.
- Estado: planejamento para revisão humana; não é aprovação do SDD, autorização de implementação, commit, PR ou deploy.
