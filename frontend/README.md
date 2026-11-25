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
- `/area` exige uma sessão local válida e consulta cartões e estado da conexão dessa sessão.
- `/area/movimentos` usa a mesma guarda e apresenta a lista paginada, filtros e cobertura dos movimentos
  persistidos, com organização pessoal por categoria e observação.
- Caminhos desconhecidos mostram a página 404 e o retorno ao início.

## Consulta e organização de movimentos (FNR-022 — implementação local aceita; integração pendente)

O cliente `TransactionListApi` usa a URL pública configurada e o interceptor Bearer da sessão em
memória. Ele consome `GET /api/transactions` com filtros/paginação e `GET /api/categories`; as
respostas são lidas como dados desconhecidos e reduzidas a allowlists. O `filteredCount` descreve o
conjunto filtrado inteiro e `coverage` permanece separado: `CompleteForRequestedWindow` cobre somente
a janela solicitada, não prova histórico bancário integral nem gasto zero.

O cliente também envia `POST /api/categories` somente com `{ name }`; a nova opção só aparece após a
resposta 201 allowlisted. Conflito 409 é informado separadamente de uma rejeição de validação, e falha
de catálogo não cria opções artificiais. O seletor usa somente categorias recebidas do catálogo,
inclusive sua opção virtual de ID nulo quando fornecida. “Sem categoria” envia `categoryId: null` para
limpar a associação; a API não oferece filtro para movimentos sem categoria.

Cada movimento permite editar categoria ou observação em ações separadas. O `PATCH
/api/transactions/{id}/annotation` contém somente o campo alterado; propriedade omitida preserva o
valor anterior e `null` limpa explicitamente a categoria ou a observação. A observação é texto simples,
limitado a 500 caracteres; campo vazio limpa pelo valor nulo. A interface só sinaliza a escrita depois
da confirmação HTTP 200 e relê a página/filtros ativos para refletir o resultado e as contagens da API.
Rejeições, 404 e falhas transitórias não transformam rascunhos em valores salvos nem exibem o corpo
remoto. Categoria e observação organizam dados pessoais: não alteram natureza, situação, fatos de
origem ou competência.

Nenhuma leitura ou escrita desta tela solicita sincronização. Valores permanecem individuais por
movimento, sem soma, conversão ou agregado monetário. O estado aceita filtros combináveis de cartão,
categoria atribuída, situação e intervalo técnico, além de busca literal por descrição. A busca
preserva `%`, `_` e demais caracteres, não é aparada nem expandida localmente e tem limite de 200
caracteres. Datas devem ser dias reais em `AAAA-MM-DD`; quando ambas são informadas, `toExclusive`
precisa ser posterior a `from`. Aplicar filtros diferentes volta à página 1; paginação conserva os
filtros aplicados, e `filteredCount` e `coverage` pertencem somente à resposta atual da API. Consulte
os contratos da [API](../README.md) e as decisões em [TDD §§11/15](../docs/TDD.md).

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
