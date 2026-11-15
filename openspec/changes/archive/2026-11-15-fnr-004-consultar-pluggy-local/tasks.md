# Tasks

Implementação de FNR-004 concluída e aceita pelo autor; as 16 tarefas abaixo estão concluídas com evidências registradas. A prova real validou a leitura de 321 transações em uma única página, `has_more=false`, sem demonstrar importação completa nem persistência. Referências: [proposta](proposal.md), [SDD](design.md) e [spec](specs/pluggy-local-read/spec.md). O aceite não amplia o escopo para FNR-005.

Em 15/11/2026, o autor autorizou executar as tarefas 3.1, 3.2 e 3.3 antes da 2.3 para completar a leitura antes de documentar um comando utilizável. A ordem alterada não amplia requisitos nem remove os gates 3.4–3.6; depois da primeira página funcional, retornar à tarefa 2.3.

## 1. Gates de revisão e dependências

- [x] 1.1 Obter aprovação humana explícita da proposta/spec/SDD e autorização separada para implementação; verificar registro da manifestação, confirmar branch não-main e registrar FNR-004 como em execução/especificação no backlog conforme o estado verdadeiro, sem avançar para FNR-005.
- [x] 1.2 Conferir referências oficiais de `/auth`, `/items/{id}`, `/accounts` e `/v2/transactions`, anotando envelopes, tipo de cartão, estados aptos e paginação de contas em AMBIENTE.md; verificar que o cliente planejado usa contrato documentado sem misturar v1/v2, registrando bloqueio se houver incompatibilidade material.
- [x] 1.3 Executar gate DT-49: levantar candidato MediatR 12.x, licença/metadados e transitivas, frameworks, manutenção e avisos de segurança; registrar fontes/resultado em AMBIENTE.md e obter seleção/aprovação explícita da versão exata antes de instalar. Verificar ausência de mudança silenciosa de linha/baseline; interromper se exigir revisão da decisão.

## 2. Caso de uso e composição do comando

- [x] 2.1 Adicionar a versão aprovada do MediatR, query/handler, contrato específico de leitura e DTOs/resultados mínimos na Application, sem dependências HTTP/console no handler ou pacotes no Domain; verificar testes unitários com cliente simulado, descoberta do handler e escopos DI válidos em .NET 10, atualizando os locks reais afetados.
- [x] 2.2 Introduzir modo `pluggy-read` na composição da Api, sem iniciar servidor e sem validar segredos no modo HTTP normal; verificar testes de comando conhecido/desconhecido, códigos de saída de D5, ausência de listener e regressões existentes de health/OpenAPI/Swagger sem configuração Pluggy.
- [x] 2.3 Documentar no README o comando planejado agora implementado, sua finalidade local e o limite de uma página; verificar que exemplos não contêm argumentos sensíveis, não alegam importação completa e correspondem ao entry point testado.

## 3. Cliente Pluggy e proteção de configuração

- [x] 3.1 Implementar opções/configuração externa, autenticação em memória, destino HTTPS fixo e prevenção de redirects externos; verificar testes de configuração ausente sem chamadas, rejeição de argumentos sensíveis e ausência de API key fora da execução, sem gravar payloads financeiros.
- [x] 3.2 Implementar consulta do item, validação de estado/consentimento e enumeração das contas conforme contrato; verificar testes para expiração com status anterior de sucesso, validade ausente sinalizada, estado desconhecido, conta não-cartão, contas paginadas, ausência/ambiguidade e AccountId externo que não pertence ao item/cartão.
- [x] 3.3 Implementar leitura de uma página de `/v2/transactions`, validação de envelope/pertencimento e resumo de contagem/continuação; verificar testes de resposta válida, `next` presente/nulo, vazio inconclusivo, JSON/campos incompatíveis e vínculo incorreto, sem seguir cursor ou calcular indicadores.
- [x] 3.4 Implementar timeout por chamada/total, cancelamento e tradução sanitizada de falhas sem retries automáticos; verificar testes de 401/403/429/5xx, transporte, timeout e Ctrl+C/cancelamento com códigos diferentes de zero e sem sucesso parcial.
- [x] 3.5 Controlar logs HTTP e renderizar somente os campos permitidos em D3; verificar captura de stdout/stderr/logs com sentinelas de credenciais, tokens, IDs e descrições, em todos os caminhos de sucesso/falha, comprovando ausência de URL sensível, corpos e exceções brutas.
- [x] 3.6 Documentar em AMBIENTE.md configuração privada, nomes das opções, seleção de cartão, códigos de saída, tempos e renovação pelo provedor; verificar exemplos sem valores reais, orientação sem segredos no chat/histórico e separação entre importação no Finora e atualização na origem.

## 4. Validação integrada e prova real local

- [x] 4.1 Executar `dotnet restore backend/Finora.slnx --locked-mode`, `dotnet build backend/Finora.slnx --no-restore` e `dotnet test --solution backend/Finora.slnx --no-build`; verificar sucesso e registrar resultados reais, incluindo arquitetura, handler/DI, isolamento HTTP e privacidade, sem usar credenciais reais nos testes automatizados.
- [x] 4.2 Com configuração privada do titular, executar o comando real sem capturar payloads/segredos; verificar autenticação, item, cartão e pelo menos um lançamento, registrando somente etapas, contadores, continuação, duração, data e exit code. Se faltar acesso ou vier vazio, registrar bloqueio/inconclusivo e manter o aceite real de FNR-004 pendente.
- [x] 4.3 Revisar o diff e evidências contra RF01 e RNF-01/02/03/09/10; verificar ausência de segredos/finanças reais versionados, preservar as specs existentes, registrar aceite humano ou pendências no guia/backlog e encaminhar descobertas de contrato para FNR-005 sem iniciá-la.
- [x] 4.4 Validar `openspec validate fnr-004-consultar-pluggy-local --strict` e `git diff --check`, conferir links locais e coerência entre proposta/spec/desenho/tarefas; registrar resultados sem confundir validação estrutural com aceite funcional ou aprovação humana.

## Workflow follow-up

- Quando autorizado, usar o subagente committer para commits locais coesos dos caminhos autorizados e validados; conferir conteúdo, hashes e alterações restantes antes de qualquer PR.
- Abrir PR somente com autorização aplicável, vinculando FNR-004, PRD, TDD e esta mudança; integrar exclusivamente após revisão e verificações exigidas. Sem migration ou deploy aplicável a esta prova local.
- Após validação e aceite, sincronizar a delta spec e arquivar preferencialmente no PR da entrega, informando o estado real de integração. Arquivamento não comprova merge, publicação ou conclusão de FNR-005.
