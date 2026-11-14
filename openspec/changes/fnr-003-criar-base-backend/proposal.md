# Proposal

## Why

O Finora ainda não possui solução backend nem testes executáveis. A FNR-003 precisa estabelecer uma base compilável e verificável para a prova local de integração da FNR-004, incluindo Swagger UI para testar chamadas pelo navegador, conforme pedido do autor nesta conversa.

## What Changes

- Criar a solução backend .NET 10 com Finora.Api, Finora.Application, Finora.Domain e Finora.Infrastructure, Controllers e referências direcionadas conforme a proposta DT-38 do TDD.
- Introduzir uma chamada técnica mínima `GET /health`, sem dados financeiros ou dependências externas, documentada e executável no Swagger UI apenas em Development.
- Gerar OpenAPI a partir dos Controllers; desabilitar tanto o documento quanto a interface fora de Development.
- Preparar MSTest.Sdk 4.5.3/Microsoft.Testing.Platform, testes HTTP e de referências entre projetos, locks NuGet reais e restore bloqueado.
- Documentar restore, build, testes, execução local e verificação pelo Swagger; oferecer configuração de exemplo sem segredos.
- Manter banco, EF/Npgsql, autenticação, Pluggy, frontend, CI e deploy fora deste incremento. Não criar entidades, repositórios ou regras financeiras artificiais.

## Capabilities

### New Capabilities

- `api-diagnostics`: resposta técnica mínima para verificar que a API está atendendo, sem indicar saúde de integrações ainda inexistentes.
- `api-exploration`: documentação OpenAPI e Swagger UI locais para inspecionar contratos e executar chamadas, indisponíveis fora de Development.

### Modified Capabilities

Nenhuma. O inventário OpenSpec consultado não contém specs consolidadas.

## Impact

- Implementação futura: `backend/`, ajuste pontual de `global.json` para o runner MTP, documentação de execução e eventuais ajustes de ignore. Nesta etapa são criados somente os artefatos desta mudança.
- Dependências futuras: Microsoft.AspNetCore.OpenApi, Swashbuckle.AspNetCore.SwaggerUI e Microsoft.AspNetCore.Mvc.Testing; versões exatas e compatibilidade serão verificadas e fixadas no incremento, preservando SDK 10.0.402 e MSTest.Sdk 4.5.3 aprovados.
- Rastreabilidade: [BACKLOG FNR-003](../../../docs/BACKLOG.md), [TDD §§3, 7 e 14; DT-02/03/04/12/38](../../../docs/TDD.md), [PRD §3 e RF01/RF14 como limites de privacidade, não entregues aqui](../../../docs/PRD.md), [baseline de ambiente](../../../docs/AMBIENTE.md).
- Dependências FNR-001/002: há documentos e validações locais registrados; o PR #7 de FNR-002 foi integrado em 14/11/2026, commit `a3f8a99cff3fdd51d3af97381cc640377bba7927`. O backlog ainda indica Em validação; o desenho registra a divergência sem declarar aceite adicional ou alterar o histórico.
- Estado: SDD e TDD 0.3 aprovados separadamente; implementação validada localmente e revisão posterior do diff completo sem problemas que exigissem correção no escopo. Em 14/11/2026, o autor aprovou explicitamente a entrega e autorizou registro do aceite, commits locais pelo committer e abertura do PR. Evidências e limites em `docs/AMBIENTE.md` e no desenho. Sem autorização de merge, arquivamento, publicação ou avanço para FNR-004.
