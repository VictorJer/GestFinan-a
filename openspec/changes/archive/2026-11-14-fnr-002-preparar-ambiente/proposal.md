# Proposal

## Why

A FNR-002 precisa tornar o ambiente reproduzível antes da criação do backend e da prova real da Pluggy. A exploração encontrou ferramentas instaladas, mas nenhum projeto ou lock, Angular CLI ausente e engine Docker inacessível; o autor confirmou limitar esta mudança à preparação, sem antecipar FNR-003 ou FNR-019.

## What Changes

- Registrar inventário sanitizado, comandos, versões exatas e compatibilidade em documentação de ambiente, distinguindo instalação, disponibilidade e validação funcional.
- Propor fixação do SDK .NET e Node, npm como gerenciador, ferramenta EF local e política de atualização explícita, sem depender das ferramentas globais.
- Definir Angular CLI 22 e runners compatíveis para uso futuro; Vitest e MSTest.Sdk/Microsoft.Testing.Platform são propostas técnicas, não decisões já aprovadas.
- Diagnosticar especificamente a conexão ao engine Docker Linux; comprovar acesso ou registrar bloqueio concreto, sem criar banco, containers ou migrations nesta mudança.
- Ajustar os critérios de FNR-002, FNR-003 e FNR-019 no backlog: locks e testes executáveis serão gerados e validados com os respectivos projetos. Registrar essa redistribuição no TDD, preservando sua aprovação histórica e deixando a nova revisão pendente até aprovação explícita.
- Não criar backend, frontend, endpoints, testes de aplicação, CI ou publicação. Não consultar Pluggy nem Neon e não instalar ou atualizar ferramentas globalmente por conveniência.

## Capabilities

### New Capabilities

Nenhuma. Preparação de ferramentas e documentação, sem mudança no comportamento da aplicação. Esta mudança declara `skip_specs: true`; não serão criadas specs artificiais.

### Modified Capabilities

Nenhuma. O inventário OpenSpec não contém specs consolidadas.

## Impact

- Item principal: **FNR-002**, dependente de FNR-001 concluída; FNR-003 e FNR-019 recebem apenas a explicitação das entregas diferidas, não implementação.
- Referências: [backlog §§4–5 e 7](../../../docs/BACKLOG.md), [TDD §§1, 7 e 14](../../../docs/TDD.md), decisões DT-04–07, DT-12, DT-14 e DT-46. Apoia a preparação para RF01/RF02 e a privacidade do [PRD §§3–5](../../../docs/PRD.md), sem alterar requisitos do produto.
- Na aplicação futura: `global.json`, arquivo de versão Node, manifesto local de ferramentas .NET, `docs/AMBIENTE.md`, `docs/TDD.md`, `docs/BACKLOG.md` e link pontual no `README.md`. Sem `package.json` fictício, locks vazios ou mudanças em `openspec/config.yaml`.
- Riscos: as versões candidatas consultadas nos registros oficiais e registradas no desenho ainda dependem de aprovação e validação executável; o Docker instalado não comprova engine funcional. A matriz deverá separar o que pode ser verificado agora do restore/build/test reservado às mudanças seguintes.
- Estado: SDD, baselines técnicas e diff final aprovados explicitamente pelo autor em 14/11/2026 nesta conversa; a solicitação de apply autorizou a implementação delimitada. Commit, PR, merge e deploy continuam não autorizados. Branch: `chore/fnr-002-preparar-ambiente`.
