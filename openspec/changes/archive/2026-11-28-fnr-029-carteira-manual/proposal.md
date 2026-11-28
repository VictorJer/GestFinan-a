# Proposal

## Why

O checkpoint P0 local foi aceito com ressalva, tornando elegível o primeiro complemento P1 antes da primeira publicação. Hoje não existe carteira persistente de investimentos: o protótipo é apenas demonstrativo e não permite ao titular manter seus próprios registros e acompanhar valores conciliados após recarga.

## What Changes

- Entregar carteira manual do titular com inclusão, consulta, edição e exclusão de investimentos (nome, classe, valor aplicado e valor atual), persistida em PostgreSQL e acessível somente por sessão válida.
- Limitar as classes às três opções confirmadas pelo titular: renda fixa, renda variável e fundos imobiliários. Apresentar total aplicado, total atual, diferença simples e distribuição por classe com base somente nos registros da carteira, sem chamar diferença de rentabilidade ajustada.
- Preservar precisão monetária na fronteira HTTP usando strings decimais canônicas invariáveis com exatamente duas casas; a persistência permanece `numeric(19,2)`.
- Oferecer API e página protegida de investimentos em português, com estados de carregamento, vazio e erro; atualizar navegação para mostrar apenas a função P1 efetivamente entregue.
- Preservar a fronteira entre investimentos manuais, movimentos do cartão e cenários do futuro simulador; não adicionar cotações, compra/venda, projeções nem agregados de movimentos.

## Capabilities

### New Capabilities

- `investment-portfolio`: carteira manual do titular, autorização e integridade das operações, resumos conciliados e apresentação persistente na interface.

### Modified Capabilities

- `web-application-shell`: a navegação protegida passa a disponibilizar a carteira entregue, mantendo ocultos simulador e outras funções não implementadas.

## Impact

- Backlog FNR-029 (P1), PRD RF10/RF11, RNF-02–08 conforme aplicável; TDD §§10/11/13/16 e contratos P1; checkpoint P0 local aceito com ressalva. Não fecha FNR-020, FNR-021/M-03, tarefa 3.4 da visão, MQ ou FNR-028.
- Backend Domain/Application/Infrastructure (modelo, EF Core/migration administrativa, consultas e comandos), controllers/DTOs protegidos em `/api/investments`; frontend Angular (serviço, estado, página, rota e menu), testes e documentação de operação/validação.
- Migration deve ser validada localmente e aplicada administrativamente no destino antes de qualquer deploy que dependa dela; sem migração automática no início da API e sem publicação nesta mudança.
