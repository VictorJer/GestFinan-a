# Proposal

## Why

O titular decidiu concluir o núcleo P0 e os complementos P1 antes de publicar. O backlog e o TDD ainda exigem FNR-028 (validação do núcleo **publicado**) para iniciar FNR-029–031, impedindo essa ordem e misturando aceite local com evidência hospedada.

## What Changes

- Reordenar os marcos e dependências de `docs/BACKLOG.md` e o roadmap correlato de `docs/TDD.md`: validar o P0 localmente, então executar FNR-029 (carteira), FNR-030 (simulador) e FNR-031 (CSV) em mudanças próprias, antes da publicação.
- Alinhar pontualmente `docs/PRD.md` §12: para o recorte escolhido pelo titular, CSV integra o pacote P1 pré-publicação em vez de ficar condicionado a “se couber”; preservar RF10–RF13, prioridades P0/P1 e aprovações anteriores em seus escopos.
- Definir evidências locais verificáveis de passagem para P1, sem concluir FNR-028, M-03 integrado ou FNR-021 por inferência; manter pendências de Pluggy real, persistência, RabbitMQ, acessibilidade e aceite humano explícitas conforme aplicáveis.
- Preservar FNR-024 como CI já integrado e os gates FNR-025–028 para preparação, publicação e validação hospedada **depois** dos incrementos P1; conservar a exigência de migration administrativa antes do deploy dependente e os checks aplicáveis.
- Reconciliar somente os registros factuais necessários dos PRs #47/#48 com o grafo Git local, distinguindo merge de aceite, checks do head e publicação; preservar as versões históricas e aprovações anteriores.
- Não alterar requisitos funcionais, interfaces, regras de negócio, código ou infraestrutura; não iniciar testes com dados reais nem publicar nesta mudança documental.

## Capabilities

### New Capabilities

Nenhuma. Esta mudança não introduz comportamento novo da aplicação.

### Modified Capabilities

Nenhuma. O reordenamento de execução não modifica requisitos das specs existentes; a mudança declara `skip_specs: true` em `.openspec.yaml` para não fabricar uma delta de comportamento.

## Impact

- Documentação de execução: `docs/PRD.md` (§12, apenas ordem/condição de CSV), `docs/BACKLOG.md` (§§3–5/7) e `docs/TDD.md` (§16 e referências dependentes a FNR-028/M5/M6); RF10–RF13 e demais critérios de aceite do PRD permanecem canônicos e inalterados.
- Sem alterações de API, frontend, banco, migrations, CI ou dependências. Planejamentos e implementações de FNR-029–031 serão mudanças separadas, sujeitas a aprovação antes de código; nenhum gate hospedado é dado como satisfeito pela validação local.
