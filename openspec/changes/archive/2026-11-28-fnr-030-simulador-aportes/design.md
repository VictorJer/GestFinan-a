# Design

## Context

Ver `proposal.md` para a motivação e `specs/investment-simulation/spec.md` e `specs/web-application-shell/spec.md` para o contrato. A API existente usa ASP.NET, controlador protegido, respostas sanitizadas e registro de handlers MediatR em allowlist (`AuthenticationServiceCollectionExtensions.cs`); o Angular mantém token somente em memória, geração de sessão e cliente de carteira com `GET /api/investments` e resposta monetária textual. `app.routes.ts` e `app.html` hoje oferecem a carteira, mas não o simulador; o frontend não tem dependência Chart.js. A spec da carteira proíbe projeções **na página da carteira**, não um simulador separado com preenchimento opcional.

PRD §8 fixa fórmula, intervalos de taxa/prazo e ausência de persistência na carteira; TDD §13 pede cálculo no backend. O titular confirmou neste planejamento os extras visuais de `docs/DESING.md` §6.5, os tetos de R$ 10.000.000,00/R$ 1.000.000,00 e o arredondamento somente para apresentação. Esses tetos e cenários guardados são concretizações novas: antes do código dependente, alinhar sua rastreabilidade em revisão pendente do PRD/TDD e obter o aceite documental aplicável, sem alterar retroativamente revisões aprovadas. O BACKLOG 0.109 registra PR #52 ainda aberto apesar do merge presente no Git local; reconciliar factual e separadamente, sem inferir checks/review remoto.

## Goals / Non-Goals

**Goals:**

- Calcular no backend uma série pequena e determinística (0 a 360 meses), com entradas rejeitadas antes da aritmética e dinheiro HTTP preservado como texto.
- Garantir que cópias temporárias, respostas atrasadas e o preenchimento opcional jamais cruzem gerações de sessão ou convertam resultados em escrita na carteira.
- Manter gráficos acessíveis com representação textual da série e sem dependência adicional obrigatória.

**Non-Goals:**

- Criar migration, tabela de cenários, rendimento ajustado, preço de ativo, integração Pluggy/cartão ou exportação CSV FNR-031.
- Transformar o visual `docs/DESING.md` em contrato de produto para outras telas ou habilitar deploy antes de FNR-031 e dos gates de FNR-025–028.

## Decisions

### D1 — Fronteira HTTP e segurança

Criar `POST /api/simulations/calculate` com `[Authorize]`, limitação e envelope `application/problem+json` existentes. Request com exatamente `initialAmount`, `monthlyContribution`, `annualRatePercent` (strings invariáveis com ponto e duas casas) e `years` (inteiro JSON). Faixas inclusivas: `0.00..10000000.00`, `0.00..1000000.00`, `0.00..50.00`, `1..30`. Recusar tipos/formas/campos extras/duplicados, payload excessivo ou cálculo não representável com erro sanitizado; não logar entradas. Resposta 200 allowlisted com `points: [{month, totalContributed, projectedValue}]` para mês zero até mês `years*12`, e `totalContributed`, `projectedValue`, `projectedReturn` no topo, todos os valores monetários como strings invariáveis de duas casas. 401/429/503 seguem a fronteira padrão. Registrar o handler puro explicitamente na allowlist autenticada (ou usar serviço puro invocado só após autorização) para não habilitar handlers de CLI/Pluggy na composição sem configuração; endpoint não acessa banco. Alternativa descartada: fazer cálculo no Angular ou derivar o titular do corpo, pois quebraria o contrato e os limites da API protegida.

### D2 — Aritmética e arredondamento

Converter a taxa anual efetiva em taxa mensal com `Math.Pow(1 + taxaAnual/100, 1.0/12) - 1`, converter a taxa calculada de forma controlada para `decimal` e iterar meses usando `decimal` para saldos/aportes. Guardar a taxa mensal com precisão suficiente para testes independentes e não arredondar cada saldo antes do próximo mês. Valor exibido de cada mês: arredondamento a centavos somente ao formatar, com `MidpointRounding.AwayFromZero`; aportes acumulados são somas exatas em centavos. Derivar `projectedReturn` do saldo final **sem arredondar** menos total aportado, e arredondar apenas a saída; como os aportes são centavos exatos, valor final publicado menos total aportado publicado concilia em centavos. O mês zero explicita valor inicial em ambos os campos. Ter ramo de taxa zero para igualdade exata e impedir erro de aproximação. Testar limites extremos e arredondamento contra referência independente com tolerância de centavos, além de invariantes de série e monotonicidade. Alternativas descartadas: iterar em `double` com moeda ou arredondar a cada mês, que acumulam desvios e contrariam a escolha do titular. A raiz 12ª não é racional em geral: não apresentar a aritmética interna como exata matematicamente.

### D3 — Página, resposta tardia e acessibilidade

Criar rota lazy protegida `/area/simulador` com página Angular em português, valores inicialmente exemplificativos rotulados (R$ 10.000,00, R$ 500,00, 10%, 5 anos) e validação com mensagens por campo; usar `inputmode=decimal` e strings canônicas, sem enviar `Number` monetário. Atualizações válidas geram cálculo da API com debounce/cancelamento; inválidas limpam o resultado atual e não mostram resultado antigo como novo. Cliente HTTP usa base configurada, interceptor Bearer e decoder estrito; estado compara geração da sessão e sequência da requisição antes de publicar resultados, cancela em mudança/saída e redige conteúdo após logout/401. A visualização gráfica pode usar SVG simples, sem Chart.js obrigatório; a tabela/descrição textual com valores exatos de pontos mensais fornece alternativa acessível e pode ser percorrida por teclado. Números derivados para geometria do gráfico **não** alimentam totais exibidos. Alternativas descartadas: cálculos/formatos financeiros feitos no browser e nova dependência de gráfico sem benefício funcional.

### D4 — Comparação e preenchimento explícito da carteira

Guardar até cinco cópias de inputs e resultados confirmados da mesma geração em estado Angular volátil; comparar pelo menos patrimônio e aportes finais e permitir percorrer a série mensal de cada cenário, limpar individualmente ou todos. Nenhuma gravação ou `sessionStorage`/`localStorage`; logout, expiração, 401, troca de geração e reload descartam as cópias. Botão “Usar valor da carteira” faz `GET /api/investments` **somente quando acionado**, reutilizando o contrato/decoder de resumo já existente, e copia `summary.totalCurrentAmount` como texto para `initialAmount` se <= `10000000.00`. Não transferir automaticamente, não copiar diferença nem valor aplicado, e rejeitar total acima do teto em vez de truncar; comparar geração e sequência também antes de aplicar a leitura, inclusive se o usuário alterou o campo enquanto a leitura estava pendente. Um novo cálculo após o preenchimento não afeta cenário já salvo nem a carteira. Alternativas descartadas: acoplar o cálculo ao banco ou persistir cenários sem requisito; ambas ampliariam a área de privacidade e alterariam o contrato FNR-029.

### D5 — Validação, documentação e gate

Backend: testes unitários da fórmula, taxa zero, aporte ao final do mês, limites, precisão, overflow e ausência de escrita; teste HTTP do contrato/auth/400/sanitização em host local sem PostgreSQL se possível e integração PostgreSQL descartável apenas para provar o botão de carteira por chamadas separadas quando necessário. Frontend: testes de contrato, navegação desktop/mobile, série e estados de formulário, cinco cenários/limpeza, expiração e respostas tardias, total acima do teto. Completar build, suites afetadas, lint/formatter disponível, `openspec validate fnr-030-simulador-aportes --strict`, links e diff check; resultados locais não equivalem a CI remoto. Documentar revisões de PRD/TDD e backlog sob revisão própria antes do código dependente, apresentar os artefatos OpenSpec para aprovação humana pré-código; nenhuma caixa de implementação é marcada apenas por existir plano. Alternativa descartada: tratar artefatos completos como aprovação.

## Risks / Trade-offs

- [Aproximação da raiz 12ª e representação em centavos] → taxa mensal calculada uma vez, `decimal` na recorrência, regra única de arredondamento na saída e vetores de referência independentes.
- [Carteira total acima do teto do simulador] → explicitar impedimento no botão sem truncamento e manter entrada atual.
- [Resultado ou cenário privado exibido após logout/mudança de sessão] → vincular estado ao identificador de geração, cancelar requisições e negar publicação de callbacks obsoletos.
- [Série de até 361 pontos e gráfico em tela pequena] → limite fixo de prazo, saída bounded, representação resumida no gráfico e tabela textual consultável.
- [Revisões documentais pendentes e backlog histórico desatualizado] → alinhar e submeter o escopo novo antes do código, sem registrar checks/review/publicação não verificados.

## Migration Plan

Sem migration de banco: introduzir endpoint protegido e cliente/tela/rota correspondentes na mesma entrega; manter caminhos atuais intocados. Na publicação futura, aplicar checks e gates próprios antes do deploy; o frontend não deve anunciar um simulador se a API correspondente ainda não estiver disponível. Reversão remove rota/menu e endpoint da entrega, sem necessidade de transformar dados persistidos, pois cenários são voláteis. Não efetuar deploy nesta mudança de planejamento.
