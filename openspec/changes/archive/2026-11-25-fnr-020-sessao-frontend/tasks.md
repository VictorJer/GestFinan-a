# Tasks

## 1. Gates e contrato vigente

- [x] 1.1 Reconciliar em `docs/BACKLOG.md` o PR #37/FNR-019 integrado (merge `c6c310c`) sem presumir review/check, registrar FNR-020 e obter revisão/aprovação explícita do diff documental e dos quatro artefatos desta mudança antes de código; verificar registro de aceite e `git diff --check`.
- [x] 1.2 Conferir em `AuthenticationController.cs`, specs consolidadas e testes HTTP os contratos de login/logout, limites, 401/403/429/503 e CORS; alinhar no `docs/TDD.md` somente decisões técnicas novas desta mudança e obter aprovação do diff canônico antes de código dependente; verificar rastreabilidade RF14/DT-28–30 e ausência de alteração de API.

## 2. Sessão local e transporte restrito

- [x] 2.1 Implementar serviço Angular de sessão volátil e validação do sucesso Bearer/expiração com temporizador e geração local; testar início válido, resposta inválida, expiração passiva, reload sem restauração e respostas antigas sem apagar sessão nova.
- [x] 2.2 Registrar interceptor funcional e URL da API segura com comparação de origem/base e caminhos protegidos; testar prefixos, subdomínio, esquema/porta, login público, ausência de configuração, Bearer somente autorizado e nenhuma chamada no bootstrap público.
- [x] 2.3 Tratar 401/403/indisponibilidade apenas para chamadas protegidas da geração atual, sem repetir login; testar 401 que limpa, 403 que preserva, rede/503 distinguíveis e 401 atrasado que não encerra sessão nova.

## 3. Entrada e navegação

- [x] 3.1 Implementar formulário de login em português com limites, senha sem alteração, e-mail normalizado apenas externamente, estados de envio e mensagens fixas; testar sucesso, 400/401/413/429/500/503, rede, URL ausente e ausência de segredos nos textos/logs controlados.
- [x] 3.2 Adicionar `/login`, `/area` informativa e guard com redirecionamento para login; testar navegação direta sem sessão, sessão expirada, área permitida e ausência de requests financeiros ou conteúdo fictício.
- [x] 3.3 Atualizar shell/home e testes herdados da FNR-019 para menu responsivo de login/área/saída sem links futuros; verificar 404, textos fiéis ao estado, teclado/foco em larguras pequenas e grandes e ausência de token no HTML/URL.

## 4. Saída e integração

- [x] 4.1 Integrar logout remoto da sessão atual com resultado confirmado exclusivamente em 204 e limpeza local após tentativa; testar 204, 401, 503, rede/timeout, duplo clique e logout atrasado diante de novo login, sem prometer rollback remoto.
- [x] 4.2 Integrar aviso seguro de expiração, 401 protegido, 403 e saída confirmada/incerta na navegação e na documentação de uso do frontend; testar trajetos completos login→área→logout/expiração e textos acessíveis sem payload bruto.
- [x] 4.3 Rodar `npm ci`, testes Vitest e builds de desenvolvimento/produção em `frontend`, validar mudança OpenSpec em modo estrito, links documentais e `git diff --check`; registrar resultados reais e limites em `docs/AMBIENTE.md` sem alegar deploy, checks GitHub ou RF14 publicado.

## Workflow follow-up

- Apresentar implementação e evidências ao titular para revisão/aceite; só depois de autorização específica usar o subagente committer para commits locais, abrir PR e avaliar integração segundo `AGENTS.md`.
- Sincronizar deltas e arquivar após validação/aceite, registrando o estado real de integração e publicação; não tomar o checklist como autorização de merge ou deploy.
