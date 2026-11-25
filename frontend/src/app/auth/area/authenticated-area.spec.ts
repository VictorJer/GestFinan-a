import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { API_BASE_URL } from '../../api-base-url.token';
import { apiAuthInterceptor } from '../api-auth.interceptor';
import { SessionService } from '../session';
import { AuthenticatedAreaPage } from './authenticated-area';
import { CardConnectionStateService } from './card-connection-state';

const API_BASE = 'https://api.example.test';
const CARDS_URL = `${API_BASE}/api/cards`;
const SYNC_STATE_URL = `${API_BASE}/api/connections/current/sync-state`;
const ON_ENTRY_URL = `${API_BASE}/api/connections/current/sync/on-entry`;
const MANUAL_URL = `${API_BASE}/api/connections/current/sync`;
const SYNC_RUN_ID = '4b2eaef0-a819-45d9-b90f-dc61531985b1';
const SYNC_RUN_URL = `${API_BASE}/api/sync-runs/${SYNC_RUN_ID}`;

let backend: HttpTestingController;
let fixture: ComponentFixture<AuthenticatedAreaPage> | undefined;
let session: SessionService;

// Inicializa a área protegida com interceptor real e transporte HTTP sintético para testar a fronteira da tela.
function mountArea(): HTMLElement {
  fixture = TestBed.createComponent(AuthenticatedAreaPage);
  fixture.detectChanges();
  return fixture.nativeElement as HTMLElement;
}

// Estado sintético deixa explícito que horários e cobertura desconhecidos não são completados localmente.
function syncStateResponse(overrides: Record<string, unknown> = {}): object {
  return {
    updateNeeded: false,
    reauthorizationRequired: false,
    lastSuccessfulImportAtUtc: null,
    sourceUpdatedAtUtc: null,
    lastAttemptAtUtc: null,
    nextAllowedAtUtc: null,
    latestExecution: null,
    ...overrides,
  };
}

// Fecha as leituras e a avaliação inicial para iniciar cada interação sobre um estado conhecido.
function flushInitialPage(
  decision: 'not_required' | 'reauthorization_required' = 'not_required',
  overrides: Record<string, unknown> = {},
): void {
  backend.expectOne(CARDS_URL).flush([]);
  backend.expectOne(SYNC_STATE_URL).flush(syncStateResponse(overrides));
  backend.expectOne(ON_ENTRY_URL).flush({ decision });
  fixture!.detectChanges();
}

// Cada cenário começa com uma geração de sessão artificial e termina sem requests pendentes não canceladas.
describe('Área protegida de cartões e conexão', () => {
  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [AuthenticatedAreaPage],
      providers: [
        { provide: API_BASE_URL, useValue: API_BASE },
        provideHttpClient(withInterceptors([apiAuthInterceptor])),
        provideHttpClientTesting(),
      ],
    });
    backend = TestBed.inject(HttpTestingController);
    session = TestBed.inject(SessionService);
    session.startSession({
      accessToken: 'synthetic-authenticated-area-token',
      tokenType: 'Bearer',
      expiresIn: 1800,
      expiresAtUtc: new Date(Date.now() + 30 * 60 * 1000).toISOString(),
    });
  });

  afterEach(() => {
    fixture?.destroy();
    fixture = undefined;
    backend.verify({ ignoreCancelled: true });
  });

  // A primeira renderização distingue carregamento; respostas vazias/desconhecidas não viram fatos financeiros.
  it('apresenta carregamento, lista vazia, horários ausentes e cobertura desconhecida', () => {
    const root = mountArea();
    const cards = backend.expectOne(CARDS_URL);
    const syncState = backend.expectOne(SYNC_STATE_URL);
    const onEntry = backend.expectOne(ON_ENTRY_URL);

    expect(root.textContent).toContain('Carregando cartões disponíveis');
    expect(root.textContent).toContain('Carregando estado da conexão');
    expect(cards.request.method).toBe('GET');
    expect(syncState.request.method).toBe('GET');
    expect(onEntry.request.method).toBe('POST');
    cards.flush([]);
    syncState.flush(
      syncStateResponse({
        updateNeeded: true,
        latestExecution: {
          syncRunId: SYNC_RUN_ID,
          state: 'Pending',
          createdAtUtc: '2026-11-25T10:00:00Z',
          lastAttemptAtUtc: null,
          finishedAtUtc: null,
          coverageState: 'Unknown',
          failureCode: null,
        },
      }),
    );
    onEntry.flush({ decision: 'not_required' });
    fixture!.detectChanges();

    expect(root.textContent).toContain('Não há cartões disponíveis nos dados persistidos');
    expect(root.textContent).toContain('não confirma ausência de cartões na origem');
    expect(root.textContent).toContain('Desconhecida');
    expect(root.textContent).toContain('Não disponível');
    expect(root.textContent).not.toMatch(/R\$\s*[\d.,]+|saldo do mês|gasto total|limite disponível/i);
    expect(root.querySelector('script, img, a[href*="transactions"]')).toBeNull();
    backend.expectNone((request) => request.method === 'POST');
  });

  // Angular interpola nomes como texto e a allowlist visual não fabrica nome, final ou métricas monetárias.
  it('exibe campos incompletos sem inserir HTML ou valores presumidos', () => {
    const root = mountArea();
    const cards = backend.expectOne(CARDS_URL);
    const syncState = backend.expectOne(SYNC_STATE_URL);
    const onEntry = backend.expectOne(ON_ENTRY_URL);
    cards.flush([
      {
        id: 'a5a35003-cd81-4f07-9563-5710a034b78a',
        name: '<img src=x onerror=alert(1)>',
        lastFour: null,
        currencyCode: 'BRL',
        availableCreditLimit: 5000,
        externalCardId: 'synthetic-external-card',
      },
      {
        id: 'd446e873-2d5e-4c3a-a1f6-2a3c72a6c164',
        name: null,
        lastFour: null,
        currencyCode: 'USD',
      },
    ]);
    syncState.flush(syncStateResponse());
    onEntry.flush({ decision: 'not_required' });
    fixture!.detectChanges();

    expect(root.querySelector('img')).toBeNull();
    expect(root.textContent).toContain('<img src=x onerror=alert(1)>');
    expect(root.textContent).toContain('Nome não disponível');
    expect(root.textContent).toContain('Não disponível');
    expect(root.textContent).toContain('BRL');
    expect(root.textContent).not.toContain('synthetic-external-card');
    expect(root.textContent).not.toContain('5000');
    expect(root.textContent).not.toMatch(/R\$\s*[\d.,]+|limite|disponível para gasto/i);
  });

  // Falhas mostram mensagem fixa e mantêm o último cartão confirmado em vez de apagar ou inventar dados.
  it('mostra erro sanitizado e conserva cartão confirmado durante nova falha transitória', () => {
    const root = mountArea();
    const cards = backend.expectOne(CARDS_URL);
    const syncState = backend.expectOne(SYNC_STATE_URL);
    const onEntry = backend.expectOne(ON_ENTRY_URL);
    cards.flush([
      {
        id: 'a5a35003-cd81-4f07-9563-5710a034b78a',
        name: 'Cartão confirmado sintético',
        lastFour: '1234',
        currencyCode: 'BRL',
      },
    ]);
    syncState.flush(syncStateResponse());
    onEntry.flush({ decision: 'not_required' });
    fixture!.detectChanges();

    TestBed.inject(CardConnectionStateService).loadCards();
    backend.expectOne(CARDS_URL).flush(
      { title: 'sentinel de erro remoto' },
      { status: 503, statusText: 'Service Unavailable' },
    );
    fixture!.detectChanges();

    expect(root.textContent).toContain('A API está temporariamente indisponível');
    expect(root.textContent).toContain('Cartão confirmado sintético');
    expect(root.textContent).toContain('1234');
    expect(root.textContent).not.toContain('sentinel de erro remoto');
    expect(root.textContent).not.toMatch(/R\$\s*[\d.,]+|gasto|saldo|fatura/i);
  });

  // Um 202 inicia GET de acompanhamento e a tela comunica aceite sem afirmar importação concluída.
  it('mostra atualização pendente após aceite e acompanha somente por GET', () => {
    const root = mountArea();
    backend.expectOne(CARDS_URL).flush([]);
    backend.expectOne(SYNC_STATE_URL).flush(syncStateResponse());
    backend.expectOne(ON_ENTRY_URL).flush({ syncRunId: SYNC_RUN_ID });
    TestBed.tick();
    fixture!.detectChanges();

    const runRequest = backend.expectOne(SYNC_RUN_URL);
    expect(runRequest.request.method).toBe('GET');
    runRequest.flush({
      syncRunId: SYNC_RUN_ID,
      state: 'Running',
      finishedAtUtc: null,
      coverageState: 'Unknown',
      failureCode: null,
    });
    fixture!.detectChanges();

    expect(root.textContent).toContain('Solicitação aceita');
    expect(root.textContent).toContain('ainda não confirma importação concluída');
    expect(root.textContent).not.toContain('Importação concluída e confirmada');
    backend.expectNone((request) => request.method === 'POST');
  });

  // O botão manual inicia um único POST e desabilita concorrência até a decisão do servidor.
  it('impede pedido manual duplicado e apresenta 202 como aceite ainda pendente', () => {
    const root = mountArea();
    flushInitialPage();
    const manualButton = Array.from(root.querySelectorAll('button')).find((button) =>
      button.textContent?.includes('Solicitar atualização manual'),
    ) as HTMLButtonElement;

    expect(manualButton.disabled).toBe(false);
    expect(manualButton.type).toBe('button');
    expect(manualButton.tabIndex).toBe(0);
    expect(manualButton.className).toContain('focus-visible:outline-3');
    manualButton.click();
    fixture!.detectChanges();
    const manualRequest = backend.expectOne(MANUAL_URL);
    const key = manualRequest.request.headers.get('Idempotency-Key');
    expect(key).toMatch(/^[!-~]{1,128}$/);
    expect(manualButton.disabled).toBe(true);
    fixture!.componentInstance.requestManualUpdate();
    backend.expectNone(MANUAL_URL);

    manualRequest.flush({ syncRunId: SYNC_RUN_ID });
    TestBed.tick();
    fixture!.detectChanges();
    const runRequest = backend.expectOne(SYNC_RUN_URL);
    runRequest.flush({
      syncRunId: SYNC_RUN_ID,
      state: 'Running',
      finishedAtUtc: null,
      coverageState: 'Unknown',
      failureCode: null,
    });
    fixture!.detectChanges();

    expect(root.textContent).toContain('Solicitação aceita');
    expect(root.textContent).not.toContain('Importação concluída e confirmada');
    expect(manualButton.disabled).toBe(true);
    expect(root.querySelector('[role="status"][aria-live="polite"]')).not.toBeNull();
    backend.expectNone((request) => request.method === 'POST');
  });

  // Resultado incerto oferece replay explícito e a mesma chave, sem gerar intenção independente.
  it('reenvia a intenção indeterminada com a mesma chave', () => {
    const root = mountArea();
    flushInitialPage();
    const manualButton = Array.from(root.querySelectorAll('button')).find((button) =>
      button.textContent?.includes('Solicitar atualização manual'),
    ) as HTMLButtonElement;
    manualButton.click();
    fixture!.detectChanges();
    const original = backend.expectOne(MANUAL_URL);
    const originalKey = original.request.headers.get('Idempotency-Key');
    original.error(new ProgressEvent('error'));
    fixture!.detectChanges();

    expect(root.textContent).toContain('O resultado da solicitação não foi confirmado');
    expect(manualButton.disabled).toBe(true);
    const replayButton = Array.from(root.querySelectorAll('button')).find((button) =>
      button.textContent?.includes('Repetir a mesma solicitação'),
    ) as HTMLButtonElement;
    replayButton.click();
    fixture!.detectChanges();
    const replay = backend.expectOne(MANUAL_URL);

    expect(replay.request.headers.get('Idempotency-Key')).toBe(originalKey);
    expect(replay.request.headers.get('Idempotency-Key')).toMatch(/^[!-~]{1,128}$/);
    replay.flush({ syncRunId: SYNC_RUN_ID });
    TestBed.tick();
    fixture!.detectChanges();
    backend.expectOne(SYNC_RUN_URL).flush({
      syncRunId: SYNC_RUN_ID,
      state: 'Running',
      finishedAtUtc: null,
      coverageState: 'Unknown',
      failureCode: null,
    });
    fixture!.detectChanges();

    expect(root.textContent).toContain('Solicitação aceita');
    expect(root.textContent).not.toContain('synthetic');
    backend.expectNone((request) => request.method === 'POST');
  });

  // Reautorização orienta sem presumir renovação; o intervalo 429 mostra somente metadados validados.
  it('orienta reautorização e comunica bloqueio de intervalo sem declarar sucesso', () => {
    const root = mountArea();
    flushInitialPage('reauthorization_required', { reauthorizationRequired: true });

    expect(root.textContent).toContain('Renove o acesso no provedor');
    const manualButton = Array.from(root.querySelectorAll('button')).find((button) =>
      button.textContent?.includes('Solicitar atualização manual'),
    ) as HTMLButtonElement;
    expect(manualButton.disabled).toBe(false);
    manualButton.click();
    fixture!.detectChanges();
    backend.expectOne(MANUAL_URL).flush(
      {
        code: 'SYNC_RUN_MINIMUM_INTERVAL_ACTIVE',
        nextAllowedAtUtc: '2026-11-25T08:00:00Z',
      },
      {
        status: 429,
        statusText: 'Too Many Requests',
        headers: { 'Retry-After': '300' },
      },
    );
    fixture!.detectChanges();

    expect(root.textContent).toContain('Aguarde até');
    expect(root.textContent).not.toContain('Importação concluída');
    expect(root.textContent).not.toContain('Too Many Requests');
  });

  // 401 encerra sessão; 403 mantém sessão e aviso, sem refletir detalhe bruto da API na área.
  it.each([
    [401, 'unauthorized', false],
    [403, 'forbidden', true],
  ] as const)('preserva o fluxo de autenticação HTTP %s no pedido manual', (status, notice, remainsAuthenticated) => {
    const root = mountArea();
    flushInitialPage();
    const manualButton = Array.from(root.querySelectorAll('button')).find((button) =>
      button.textContent?.includes('Solicitar atualização manual'),
    ) as HTMLButtonElement;
    manualButton.click();
    fixture!.detectChanges();
    backend.expectOne(MANUAL_URL).flush(
      { title: 'private authentication detail sentinel' },
      { status, statusText: 'Authentication failure' },
    );
    TestBed.tick();
    fixture!.detectChanges();

    expect(session.notice()).toBe(notice);
    expect(session.isAuthenticated()).toBe(remainsAuthenticated);
    expect(root.textContent).not.toContain('private authentication detail sentinel');
    expect(manualButton.disabled).toBe(!remainsAuthenticated);
  });

  // Ação de recuperação repete somente GETs; leitura 401 encerra a sessão e remove conteúdo privado.
  it('reconsulta snapshots sem POST e encerra sessão em 401 da leitura', () => {
    const root = mountArea();
    backend.expectOne(CARDS_URL).flush(
      { title: 'synthetic private detail' },
      { status: 503, statusText: 'Service Unavailable' },
    );
    backend.expectOne(SYNC_STATE_URL).flush(syncStateResponse());
    backend.expectOne(ON_ENTRY_URL).flush({ decision: 'not_required' });
    fixture!.detectChanges();

    const retryButton = Array.from(root.querySelectorAll('button')).find((button) =>
      button.textContent?.includes('Tentar consulta novamente'),
    ) as HTMLButtonElement;
    retryButton.click();
    fixture!.detectChanges();
    backend.expectOne(CARDS_URL).flush(
      { title: 'private auth detail' },
      { status: 401, statusText: 'Unauthorized' },
    );
    backend.expectOne(SYNC_STATE_URL).flush(syncStateResponse());
    TestBed.tick();
    fixture!.detectChanges();

    expect(session.currentSession()).toBeNull();
    expect(root.textContent).not.toContain('private auth detail');
    expect(root.textContent).not.toContain('synthetic private detail');
    backend.expectNone((request) => request.method === 'POST');
  });

  // Troca de geração cancela POST antigo; uma resposta tardia não deve alterar o estado do novo login.
  it('cancela resposta manual pendente ao iniciar nova geração de sessão', () => {
    const root = mountArea();
    flushInitialPage();
    const manualButton = Array.from(root.querySelectorAll('button')).find((button) =>
      button.textContent?.includes('Solicitar atualização manual'),
    ) as HTMLButtonElement;
    manualButton.click();
    fixture!.detectChanges();
    const pending = backend.expectOne(MANUAL_URL);

    session.startSession({
      accessToken: 'synthetic-next-generation-token',
      tokenType: 'Bearer',
      expiresIn: 1800,
      expiresAtUtc: new Date(Date.now() + 30 * 60 * 1000).toISOString(),
    });
    TestBed.tick();
    fixture!.detectChanges();

    expect(pending.cancelled).toBe(true);
    expect(root.textContent).not.toContain('Cartão da sessão anterior');
    backend.expectNone((request) => request.method === 'POST');
  });
});
