# Proposal

## Why

A visão protegida `/area/visao` já usa as cores e a marca Finora, mas organiza o conteúdo em grandes painéis textuais empilhados e não aproveita a hierarquia, o ritmo e a composição em colunas da visão geral descrita no protótipo. Aproximar sua apresentação da referência facilita localizar contagens, filtros, cobertura e o caminho para os movimentos sem importar indicadores financeiros demonstrativos incompatíveis com o MVP.

## What Changes

- Refinar cabeçalho, tipografia, espaçamento, superfícies e hierarquia da **visão protegida**, usando `docs/DESING.md` §§2–5, 7, 9, 11 e 13 como referência visual; organizar contagens descritivas, eixos independentes, filtros aplicados e cobertura por prioridade de leitura e largura de tela.
- Ajustar apenas o necessário da navegação e do shell compartilhados para contextualizar a visão e preservar rotas e acesso a cartões/conexão, movimentos e saída em desktop e celular; manter a página pública `/` e o login funcionalmente inalterados.
- Manter como únicos dados da visão os campos descritivos já autorizados pelo `GET /api/dashboard`: contagens com universo declarado, filtros aplicados, `TransactionDate`, janela solicitada/observada, último sucesso e execução recente. O link leva à lista única, onde permanecem os valores **individuais** autorizados; não criar lista paralela na visão.
- Não reproduzir saldo, receitas, patrimônio, gasto total, subtotal, média, série, rosca/barras monetárias, limites fictícios, perfil fictício, simulação de conexão ou mensagens de histórico integral. Falta de campo ou falha de leitura não vira zero ou dado de demonstração; conclusão de importação vale apenas para a janela solicitada.
- Fora do escopo: modificar API/backend, semântica financeira, importação/sincronização, autenticação/sessão, rotas, página pública, formulários de login, lista/edição de movimentos e módulos P1 (investimentos, simulador e CSV). Sem **BREAKING** previsto.

## Capabilities

### New Capabilities

Nenhuma.

### Modified Capabilities

- `web-card-movement-overview`: acrescentar critérios observáveis de organização visual e responsiva da visão já protegida, sem mudar o contrato de dados, os filtros, a lista única ou a proibição de agregados monetários.

## Impact

- **Rastreabilidade:** melhoria visual derivada de BACKLOG FNR-019/FNR-023 (entregas existentes), com restrições de FNR-020/021/022; PRD RF04/05/07/08/09/14/16 e RNF-02/03/06/07, §8; TDD DT-10/18/28/56 e §§3/5/11/13; specs `web-card-movement-overview`, `web-application-shell`, `web-authenticated-session` e `card-movement-explorer`. Não atribuir novo ID nem alterar status do backlog sem decisão do titular.
- **Arquivos prováveis de implementação futura:** `frontend/src/app/auth/area/dashboard-overview-page.html`, `dashboard-applied-filters.html`, `frontend/src/app/app.html`, `frontend/src/styles.css` e testes próximos (`dashboard-overview-page.spec.ts`, `dashboard-applied-filters.spec.ts`, `app.spec.ts`, `app.routes.spec.ts`). Ajustes em `.ts` apenas se necessários à apresentação; nenhuma dependência nova prevista. Proteger a base Angular/Tailwind e o serviço de estado/HTTP existentes.
- **Riscos/dependências:** o HTML citado `outputs/Finora-prototipo.html` não está disponível neste checkout (nem a pasta `outputs/`); a comparação direta e detalhes não registrados devem ser conferidos quando o arquivo for disponibilizado, antes de aprovar qualquer fidelidade visual além de `docs/DESING.md`. Há diferenças deliberadas entre o dashboard fictício e o MVP; contrastes, zoom, leitor de tela e navegação móvel exigem verificação específica. BACKLOG/TDD têm revisões factuais pendentes próprias; a aprovação antiga não aprova esta proposta nem resolve M-03/FNR-021 integrados, API/provedor hospedados ou publicação.
- **Validação prevista após aprovação/implementação:** testes de template/navegação e regressão dos estados autorizados e da ausência de métricas monetárias; suíte e builds frontend; inspeção visual em desktop, 900 px, 560 px e celular estreito, teclado/foco, zoom, contraste e leitor de tela quando disponível; validação OpenSpec estrita. Fixtures sintéticas só são testes, nunca conteúdo real exibido. Nenhuma consulta à Pluggy ou sincronização integra esta proposta.
