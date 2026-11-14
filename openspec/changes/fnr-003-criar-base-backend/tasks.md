# Tasks

Plano da FNR-003, com execução indicada pelas caixas abaixo. Referências: [proposta](proposal.md), [SDD](design.md), [diagnóstico](specs/api-diagnostics/spec.md) e [exploração da API](specs/api-exploration/spec.md). Aceite explícito do SDD e solicitação de apply registrados no desenho; os demais gates continuam sujeitos às suas evidências.

## 1. Confirmar os gates e registrar a revisão técnica

- [x] 1.1 Registrar a aprovação explícita deste SDD, incluindo direção DT-38, endpoint técnico e Swagger restrito a Development; verificar a fonte da aprovação e conferir branch/estado Git antes de qualquer código, preservando alterações não relacionadas.
- [x] 1.2 Reconciliar a dependência FNR-002 com suas evidências locais, aceite e PR #7 integrado, sem deduzir aceite só pelo merge; verificar exigências aplicáveis e atualizar pontualmente o registro no backlog com referências verdadeiras, mantendo bloqueio explícito se faltar evidência.
- [x] 1.3 Atualizar pontualmente o TDD para refletir a direção DT-38 aprovada neste SDD e Swagger/OpenAPI, preservando histórico e aprovação 0.2 e identificando a nova revisão como pendente até seu aceite; apresentar o diff e verificar registro explícito de aprovação da revisão antes de tratar o documento novo como aprovado. Registrar FNR-003 e sua branch/mudança no backlog, sem marcar entrega concluída.

## 2. Estabelecer solução, dependências e testes de fronteira

- [x] 2.1 Conferir SDK 10.0.402 e metadados dos candidatos Microsoft.AspNetCore.OpenApi/Mvc.Testing 10.0.15 e SwaggerUI 10.2.3; fixar versões estáveis compatíveis segundo D4, preservando MSTest.Sdk 4.5.3 e sem usar latest. Verificar disponibilidade e grafo no restore; registrar versões efetivas e fontes em docs/AMBIENTE.md, interrompendo para revisão se necessário mudar major/baseline.
- [x] 2.2 Criar backend/Finora.slnx, os quatro projetos net10.0 e backend/tests/Finora.Api.Tests com MSTest.Sdk/MTP; configurar as referências exatas de D1 e runner em global.json sem mudar a seleção do SDK. Verificar build da solução e ausência de classes/exemplos de template, entidades provisórias ou dependências de EF/Npgsql/Pluggy.
- [x] 2.3 Criar testes MSTest que leiam os projetos e confiram as referências diretas de D1, sem caminhos absolutos ou marcadores artificiais; executar com contagem positiva e verificar que Domain não referencia os outros projetos. Comentar a intenção dos testes e configurações manuais em português.
- [x] 2.4 Ativar geração de packages.lock.json nos projetos reais, gerar/versionar locks e executar novo restore bloqueado; verificar falha esperada para divergência de dependência em cópia descartável sem modificar o grafo aceito. Documentar comandos de restore/build/test MTP verificados e limites do pin do SDK de testes em docs/AMBIENTE.md.

## 3. Disponibilizar o diagnóstico técnico sem dados financeiros

- [x] 3.1 Implementar Controller e DTO de GET /health conforme api-diagnostics, preparar acesso do WebApplicationFactory ao entry point e testes de resposta 200/JSON exato sem autenticação, banco ou provedor; executar os testes e conferir ausência de campos extras, chamadas externas e dados financeiros. Comentar os blocos novos em português.
- [x] 3.2 Preparar configuração de exemplo não sensível e perfis locais em loopback, revisar ignores dos artefatos gerados e documentar inicialização/localização de /health no README; executar os passos documentados e verificar que nenhum segredo, string de conexão real ou artefato bin/obj está sendo versionado. Explicar JSON estrito na documentação associada.

## 4. Documentar e testar chamadas pelo Swagger

- [x] 4.1 Configurar OpenAPI nativo em /openapi/v1.json e Swagger UI em /swagger, ambos exclusivamente em Development, com assets locais e descrição do GET /health; criar e executar testes HTTP que validem documento/operação/resposta, HTML/configuração da UI e disponibilidade do asset JS. Verificar ausência de segundo gerador e de endpoints financeiros inexistentes.
- [x] 4.2 Criar e executar testes com ambientes explícitos Production e Staging, cobrindo os cenários negativos de api-exploration e /health ainda disponível; verificar 404 para UI/documento e, em Production, index e asset, sem configurar autenticação falsa ou CORS permissivo.
- [x] 4.3 Documentar no README e guia a abertura do Swagger, uso de Try it out, limitação a Development e diferença entre liveness e saúde das integrações; iniciar a API local e executar GET /health pela UI no navegador, verificando 200/corpo esperado e ausência de erros que impeçam carregar documento/assets. Registrar evidência sanitizada real; não concluir por teste apenas do HTML nem antecipar Authorize/JWT.

## 5. Validar o incremento integrado e apresentá-lo para revisão

- [x] 5.1 Executar da raiz os comandos documentados de restore bloqueado, build sem restore e testes MTP da solução inteira; verificar código de saída e número positivo de testes, registrar versões/comandos/resultados reais e confirmar que banco, containers, provedor e segredos não são necessários. Não relatar os resultados locais como checks aprovados no GitHub.
- [x] 5.2 Executar openspec validate fnr-003-criar-base-backend --strict, git diff --check e checagem dos links locais afetados; revisar o inventário contra o escopo, os comentários em português e os critérios FNR-003. Apresentar diff, evidências e limitações para aceite, atualizando o backlog com estado verdadeiro sem declarar integração/publicação ou avançar a FNR-004.

## Workflow follow-up

Registro em 14/11/2026: revisão somente por leitura do diff completo sem problemas que exigissem correção no escopo; autor aprovou a entrega e autorizou registro do aceite, commits locais pelo committer e abertura do PR. Evidências e limites em [AMBIENTE.md](../../../docs/AMBIENTE.md#revisão-e-aceite-da-entrega--14112026). Sem autorização de merge, arquivamento, publicação ou avanço para FNR-004; estas ações não serão executadas neste acompanhamento.

- Após validação e com autorização de commits, enviar ao committer raiz, branch, caminhos explícitos, referências e evidências; conferir hashes/conteúdo e alterações restantes, preservando trabalho não relacionado. Planejamento não concede essa autorização.
- Abrir PR somente com autorização pertinente, vinculando FNR-003, PRD, TDD e esta mudança; distinguir checks ausentes, pendentes, falhos e aprovados e integrar apenas após revisão e gates aplicáveis. Não criar pipeline/deploy fora do escopo.
- Após validação e aceite, sincronizar as duas delta specs e arquivar pela skill correspondente, preferencialmente no mesmo PR, registrando o estado real da integração. Arquivar não comprova merge nem publicação; não marcar tarefas executadas só porque seus artefatos existem.
- Atualizar status/evidências finais no backlog sem iniciar FNR-004 automaticamente. Não há migration ou deploy de aplicação a executar neste incremento.
