# Proposal

## Why

As APIs descritivas do núcleo já estão disponíveis, mas o repositório ainda não tem um projeto frontend. A FNR-019 cria uma base Angular executável e visualmente coerente para que login, cartão, lista e visão descritiva sejam entregues separadamente, sem antecipar funcionalidades ou números financeiros fictícios.

## What Changes

- Criar o workspace da aplicação em `frontend/` com Angular 22, templates HTML, Tailwind CSS 4 e dependências fixadas na baseline aprovada; versionar `package-lock.json` gerado a partir do projeto real.
- Entregar uma estrutura visual responsiva e acessível, em português, com identidade de `docs/DESING.md`, incluindo apenas navegação para páginas efetivamente implementadas. A página inicial pública explicará que a aplicação está em preparação, sem simular dados financeiros ou sessão.
- Preparar roteamento e configuração pública da origem da API por ambiente, além dos pontos de composição do cliente HTTP, sem chamadas de negócio, guard, interceptor de sessão ou armazenamento de token nesta mudança.
- Disponibilizar testes executáveis com Vitest e comandos reprodutíveis de `npm ci`, build e teste; atualizar documentação operacional e estado factual da FNR-018/FNR-019 no momento da implementação, sem transferir aprovação documental pendente.
- Não incluir tela de login, sessão, cartões, movimentos, dashboard financeiro, investimentos, gráficos, exportação, integração bancária ou publicação. Essas entregas permanecem em FNR-020–023 e itens posteriores.

## Capabilities

### New Capabilities

- `web-application-shell`: apresentar a base pública, responsiva e acessível do aplicativo, com navegação honesta e separação entre configuração pública e dados de negócio.

### Modified Capabilities

Nenhuma. As especificações existentes de autenticação, segurança e consulta da API permanecem inalteradas; esta mudança não altera contratos HTTP.

## Impact

- Novo projeto sob `frontend/`, sem alterar endpoints, banco, migrations ou código de backend.
- Base aprovada em `docs/BACKLOG.md` FNR-019 (dependências FNR-002/003), `docs/TDD.md` DT-05/10/13/18 e §§3/7/14, `docs/PRD.md` RNF-06 e referência visual `docs/DESING.md` §§2–5/7/9/11–13. O HTML original citado na referência não está no repositório; não é pré-requisito para esta base.
- A baseline de `docs/AMBIENTE.md` fixa Node 24.22.0, npm 11.19.1, Angular CLI/build 22.2.7, framework/compiler 22.2.6, TypeScript 6.0.3, Vitest 5.1.0 e Tailwind 4.3.5; compatibilidade transitiva e execução real deverão ser verificadas ao implementar, sem atualizar versões silenciosamente.
- FNR-020 depende desta base para autenticação e rotas protegidas; FNR-021–023 e FNR-038 permanecem entregas independentes. O BACKLOG 0.63 e o README contêm registros factuais anteriores ao merge local do PR #35/FNR-018; reconciliar esses registros em revisão própria durante a execução, preservando as aprovações anteriores.
