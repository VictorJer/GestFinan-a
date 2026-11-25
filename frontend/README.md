# FinoraWeb

This project was generated using [Angular CLI](https://github.com/angular/angular-cli) version 22.2.7.

## Executar localmente

Na pasta `frontend/`, instale as dependências fixadas e inicie o Angular:

```powershell
npm ci
npm start
```

O perfil de desenvolvimento usa a URL configurada em `src/environments/environment.ts`. A API
precisa estar disponível nessa origem e permitir a origem local do navegador; a aplicação não
infere outro destino quando a configuração está ausente. O perfil de produção mantém
`apiBaseUrl: null` até que uma origem pública seja aprovada.

Para executar testes sem modo watch, use `npm test -- --watch=false`. Builds de desenvolvimento e
produção podem ser gerados com `npm run build` e `npm run build -- --configuration production`.

## Rotas disponíveis

- `/` apresenta o estado público do produto e oferece somente o próximo acesso disponível.
- `/login` recebe e-mail e senha; não há cadastro público nem recuperação por e-mail.
- `/area` exige uma sessão local válida e contém somente informação sobre o estado da entrega. Não
  carrega cartões, movimentações ou dados de demonstração.
- Caminhos desconhecidos mostram a página 404 e o retorno ao início.

## Sessão e saída

O token Bearer fica somente na memória da página: recarregar o navegador exige novo login. A sessão
expira conforme o prazo informado pela API e não é renovada automaticamente. Chamadas protegidas
recebem o token somente na origem e na base configuradas.

Se uma chamada protegida responder 401, a sessão local termina e a navegação orienta novo login.
Uma resposta 403 preserva a sessão e informa falta de permissão; falha de rede ou indisponibilidade
da API é apresentada separadamente. Esses avisos não exibem mensagens brutas do servidor.

Ao escolher **Sair**, a aplicação solicita `POST /api/auth/logout` e sempre limpa a sessão local
após a tentativa. Somente HTTP 204 é mostrado como saída confirmada pelo servidor. Qualquer outro
status, falha de rede, timeout ou URL ausente deixa o estado remoto não confirmado; isso não afirma
rollback nem revogação. O logout não é repetido automaticamente.

## Referência do Angular CLI

O servidor de desenvolvimento pode ser iniciado por `npm start` (equivalente ao `ng serve`); por
padrão, o Angular CLI abre `http://localhost:4200/` e recarrega as alterações de arquivos.
Scaffolding continua disponível, por exemplo `ng generate component component-name`; consulte
`ng generate --help` para os schematics instalados.

`ng build` compila em `dist/`. O script de testes usa Vitest (`npm test -- --watch=false`). O Angular
CLI não inclui um framework end-to-end por padrão; qualquer ferramenta E2E deve ser escolhida e
configurada separadamente, não há comando `ng e2e` configurado neste workspace.

Mais informações: [visão geral e referência do Angular CLI](https://angular.dev/tools/cli) e
[documentação do Vitest](https://vitest.dev/).
