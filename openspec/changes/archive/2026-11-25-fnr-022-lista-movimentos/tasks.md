# Tasks

## 1. Gates documentais e delimitação

- [x] 1.1 Conferir no Git local o merge PR #39/FNR-021 e registrar no backlog o estado factual sem presumir aceite da revisão 0.70, reviews/checks ou publicação; verificar o diff documental e os links alterados antes de iniciar código.
- [x] 1.2 Apresentar proposta, três deltas, design e tarefas desta mudança para aprovação explícita pré-código, mantendo FNR-015/016/017/020 como dependências e o explorador/FNR-023/FNR-038 fora do escopo; verificar registro do aceite e rastreabilidade FNR-022 ↔ PRD RF04/05/06/09/15 ↔ TDD §§11/13/15 no backlog sem marcar outras mudanças concluídas.

## 2. Cliente de contratos existentes

- [x] 2.1 Criar cliente tipado e allowlist para `GET /api/transactions` e `GET /api/categories`, com URL configurada, filtros opcionais, página/tamanho e cobertura separada; verificar testes HttpTestingController de parâmetros literais, DTO incompleto, resposta vazia, sessão ausente e ausência de `/sync*`.
- [x] 2.2 Integrar `POST /api/categories` e `PATCH /api/transactions/{id}/annotation` com corpos estritos, campos omitidos versus `null`, geração de sessão e erros sanitizados; verificar testes de 201/200, 400/401/403/404/409/429/503, falha de rede, resposta não reconhecida e não envio de campos externos.
- [x] 2.3 Documentar em README/AMBIENTE o uso frontend dos contratos existentes, seus limites de cobertura e o caráter sintético das provas HTTP; verificar consistência com README da API, TDD §11/15 e links documentais.

## 3. Estado de filtros e leitura

- [x] 3.1 Criar estado da lista por geração de sessão com rascunho/filtro aplicado, página 1 após alteração, paginação estável e `filteredCount`/`coverage` da mesma resposta; verificar testes de filtros combinados, fim exclusivo, busca literal, página além do fim, respostas fora de ordem e cancelamento.
- [x] 3.2 Fazer logout/expiração/401 limpar snapshots e requests pendentes, preservando snapshot anterior apenas em falha transitória da mesma sessão com aviso e nova leitura explícita; verificar testes com resposta tardia, duas gerações, 403 distinto de 401 e nenhum POST de sincronização.
- [x] 3.3 Documentar semântica dos filtros e mensagens da lista em README/AMBIENTE, incluindo limite de 200 caracteres e ausência de filtro para “Sem categoria” na API; verificar que as opções e os textos não prometem uma consulta inexistente.

## 4. Navegação e apresentação descritiva

- [x] 4.1 Registrar `/area/movimentos` com guarda existente e adicionar links condicionais desktop/móvel sem montar coordenador de atualização na lista; verificar testes de rota direta sem sessão, menu após login/logout e navegação sem nova intenção de entrada.
- [x] 4.2 Criar tela reutilizável da lista com campos individuais, filtros, paginação, contagem, cobertura e estados de carga/vazio/erro/recuperação; verificar testes de naturezas desconhecidas, situações pendentes, parcelas opcionais, data original versus técnica, página vazia com contagem positiva e ausência de somas/gráficos.
- [x] 4.3 Validar interação por teclado e layout celular/desktop, rótulos e feedback em português; registrar limitações de inspeção visual em AMBIENTE e verificar testes de foco/nomes acessíveis e ausência de corte de dados essenciais.

## 5. Organização pessoal na interface

- [x] 5.1 Oferecer seletor de categorias do catálogo e criação pessoal confirmada sem reload, inclusive virtual nulo; verificar testes de 201, 400/409, catálogo indisponível e ausência de opção artificial após falha.
- [x] 5.2 Permitir editar/limpar categoria e observação com PATCH parcial, texto simples até 500 caracteres e releitura dos filtros/contagens após confirmação; verificar testes de campo omitido, `null`, 404/erro, mudança de categoria que esvazia a página e anotação preservada após nova leitura com fatos de origem atualizados.
- [x] 5.3 Documentar fluxo de organização e limites de confirmação em README/AMBIENTE, sem alegar mudança de natureza, situação ou competência; verificar links e casos sintéticos de reimportação/categoria.

## 6. Validação integrada da mudança

- [x] 6.1 Executar suíte frontend e builds de desenvolvimento/produção dos componentes afetados; verificar cenários de sessão, GET/POST/PATCH, resposta tardia, filtros e ausência de agregados, registrando resultados e limites locais em AMBIENTE.
- [x] 6.2 Executar validação OpenSpec estrita da mudança e specs afetadas, `git diff --check` e conferência de links/rastreabilidade e do diff restrito ao escopo; registrar falhas ou bloqueios concretos sem declarar API real, CORS, provedor ou publicação comprovados por testes sintéticos.
- [x] 6.3 Apresentar a implementação e as evidências para aceite humano antes de encaminhar commits/PR ou sync/arquivo; verificar que critérios pendentes do explorador e gates de FNR-023/038 não foram marcados como concluídos por esta entrega.

## Workflow follow-up

- Após aprovação/validação e autorização específicas, organizar commits locais pelo subagente committer e submeter PR da feature branch; não inferir autorização de merge ou publicação deste planejamento.
- Sincronizar delta specs e arquivar esta mudança somente após validação e aceite aplicáveis, sem resolver automaticamente as tarefas pendentes de `explorador-movimentos-cartao-sem-total`.
