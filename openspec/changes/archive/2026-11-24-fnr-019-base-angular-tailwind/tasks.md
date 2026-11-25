# Tasks

## 1. Gate documental e workspace executável

- [x] 1.1 Apresentar proposta, delta, desenho e tarefas FNR-019 para aprovação explícita antes do código; conferir registro do aceite e dependências FNR-002/003, sem tratar validação do CLI como aprovação humana.
- [x] 1.2 Reconciliar em `docs/BACKLOG.md`/README o merge local do PR #35/FNR-018 e selecionar FNR-019 como mudança principal sem apagar histórico; conferir o grafo Git, links e revisão/aprovação do diff documental quando aplicável.
- [x] 1.3 Criar app único Angular `finora-web` em `frontend/`, com roteamento, CSS, build estático e versões aprovadas; conferir estrutura, `package.json`, `angular.json` e ausência de SSR, backend ou projetos vazios adicionais.
- [x] 1.4 Configurar Tailwind CSS 4 com plugin PostCSS e tokens efetivamente usados, gerar/versionar `package-lock.json` real e testar `npm ci` e build, sem `latest` ou instalação de Chart.js; documentar comandos e versões efetivamente instaladas em `docs/AMBIENTE.md`.

## 2. Shell público e navegação

- [x] 2.1 Implementar início estático com marca, estrutura visual e texto de preparação em português; testar renderização sem API/sessão e ausência de dados demonstrativos, somas e status financeiros na página.
- [x] 2.2 Implementar somente rotas `/` e não encontrado, com navegação vinculada apenas a destinos disponíveis; testar acesso direto a caminho desconhecido, retorno à origem e inexistência de links/ações de login, dados ou integração não entregues.
- [x] 2.3 Construir layout responsivo e acessível baseado em `docs/DESING.md` §§2–5/7/9/11–13, com landmarks, foco visível e textos legíveis; testar semântica em Vitest e verificar manualmente teclado e larguras próximas de 560/900 px, registrando limites da referência visual em `docs/AMBIENTE.md`.

## 3. Base para integrações futuras, sem consumo de API

- [x] 3.1 Separar origem pública da API por ambiente/build, adicionar composição do HttpClient sem chamadas automáticas, token, interceptor ou segredos; verificar build das configurações e teste/inspeção das páginas sem requests de autenticação, importação ou finanças, documentando a configuração pública em `docs/AMBIENTE.md`.
- [x] 3.2 Habilitar Vitest pelo target de testes do Angular e criar regressões do shell, rotas e ausência de ações futuras; executar o teste sem watch e documentar comando e resultados, sem apresentar DOM como prova de layout no navegador.

## 4. Validação integrada e revisão do escopo

- [x] 4.1 Repetir `npm ci`, build e suíte Vitest do workspace após todas as alterações; registrar resultados reais e bloqueios, distinguindo testes locais de checks remotos.
- [x] 4.2 Validar a mudança OpenSpec em modo estrito, links da documentação e `git diff --check`; inspecionar o diff para confirmar que somente arquivos FNR-019 foram alterados e que não há backend, migrations, endpoints novos, credenciais, publicação ou implementação de FNR-020–023/FNR-038.

## Workflow follow-up

- Apresentar a implementação e as evidências para aceite; commits locais, PR, sync e arquivo dependem das autorizações e gates correspondentes, não do simples preenchimento desta lista.
- Publicação em Cloudflare Pages, fallback de rotas hospedadas e integração de login/dados pertencem a FNR-027 e FNR-020–023, respectivamente; não são critérios de encerramento técnico desta mudança.
