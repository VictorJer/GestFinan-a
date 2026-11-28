# Tasks

## 1. Alinhamento e gate pré-código

- [x] 1.1 Conferir a rastreabilidade FNR-029 → PRD RF10/RF11/RNF aplicáveis → TDD §§10/11/13/16 → deltas desta mudança, documentando no backlog somente divergência factual encontrada; verificar links e diff sem alterar aprovações herdadas.
- [x] 1.2 Apresentar proposta, as duas delta specs, design (inclusive premissa BRL/duas casas) e tarefas ao titular e registrar aceite explícito do gate pré-código antes de alterar código; verificar a decisão registrada, sem tomar validação CLI como aprovação humana.

## 2. Modelo e persistência da carteira

- [x] 2.1 Criar entidade e validação da carteira em Domain/Application (nome, três classes, valores não negativos representáveis e titular); verificar testes unitários de zero, excesso de escala, overflow, classe desconhecida e nomes inválidos.
- [x] 2.2 Mapear `Investment` e a barreira de gravação no `FinoraDbContext` com FK do titular, constraints e `numeric(19,2)`; verificar testes PostgreSQL descartáveis para propriedade, valores, FK e rollback, sem banco pessoal/remoto.
- [x] 2.3 Criar migration incremental e snapshot, sem seed e sem mudar migrations anteriores; testar instalação em banco vazio e upgrade com dados P0, ausência de migração automática no startup e registrar resultado/limite em `docs/AMBIENTE.md`.

## 3. Casos de uso e API protegida

- [x] 3.1 Implementar comandos de criar, editar e excluir filtrados por `UserId` da sessão, sem revelar existência de investimento alheio; verificar testes de persistência após nova leitura, isolamento entre titulares e 404 idêntico ao inexistente.
- [x] 3.2 Implementar query `GET /api/investments` com lista ordenada e resumo do mesmo conjunto, incluindo diferença e valores atuais por classe; verificar testes de conciliação, alteração/exclusão, carteira vazia, total atual zero e diferença negativa sem leitura de movimentos.
- [x] 3.3 Expor GET/POST/PUT/DELETE em controller protegido, com payload estrito — strings decimais canônicas com duas casas —, 201/200/204 e erros 400/401/404/429/503 conforme fronteira existente; verificar testes HTTP de round-trip monetário, contrato, entrada inválida, challenge e ausência de dados de outro titular, e documentar contratos/limites no registro da entrega.

## 4. Página Angular e navegação

- [x] 4.1 Criar serviço HTTP e estado de carteira ligado à geração da sessão, com descarte de respostas tardias e releitura após mutações confirmadas; verificar falhas, logout/expiração, nova sessão e que o serviço/estado consultem somente a carteira, sem efeitos sobre cartão. A navegação concreta será verificada em 4.3.
- [x] 4.2 Construir página protegida `/area/investimentos` com lista, resumo, criação/edição, confirmação de exclusão e estados de carregamento/vazio/erro/salvando; verificar testes de formulário, acessibilidade/teclado, BRL, valores zero e textos que não aleguem rentabilidade ajustada. A rota e sua proteção serão ligadas/verificadas em 4.3.
- [x] 4.3 Atualizar menu e rotas somente para carteira entregue, sem links de simulador/CSV; verificar testes de menu em sessão válida/inválida, 404, desktop/celular e páginas públicas sem requisição financeira; documentar comportamento e limites da tela em `docs/AMBIENTE.md`.

## 5. Verificação integrada da mudança

- [ ] 5.1 Executar restore/build e suítes backend pertinentes (sem PostgreSQL e com PostgreSQL descartável) e testes/build frontend; registrar comandos, resultados e falhas reais, diferenciando fixtures de evidência com dados reais e checks remotos.
- [ ] 5.2 Validar OpenSpec da mudança estritamente, links/diff e o fluxo local autenticado cadastro → resumo → edição → recarga → exclusão com dados sintéticos; conferir que P0, FNR-030/031, MQ e publicação não sejam declarados concluídos e apresentar evidência para aceite da implementação.

## Workflow follow-up

- Depois da implementação validada e aceita, tratar commits/PR e, quando houver aceite do escopo, sync e arquivo OpenSpec conforme `AGENTS.md`; nenhuma dessas ações está autorizada por esta proposta de planejamento.
- Qualquer migration no destino e publicação dependem de autorização própria e dos gates FNR-025–028; não são tarefas executadas nesta mudança.
