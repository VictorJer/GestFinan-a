import { HttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { By } from '@angular/platform-browser';
import { Router, RouterOutlet } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { vi } from 'vitest';
import { App } from './app';
import { appConfig } from './app.config';
import { API_BASE_URL } from './api-base-url.token';
import { LoginPage } from './auth/login/login';
import { SessionService } from './auth/session';
import { routes } from './app.routes';
import { HomePage } from './home/home';
import { NotFoundPage } from './not-found/not-found';

const API_BASE = 'http://127.0.0.1:5080/api';

// Responde às três chamadas permitidas ao abrir /area sem depender de uma API real.
function flushAuthenticatedAreaStartup(http: HttpTestingController): void {
  const cards = http.expectOne(`${API_BASE}/cards`);
  const syncState = http.expectOne(`${API_BASE}/connections/current/sync-state`);
  const onEntry = http.expectOne(`${API_BASE}/connections/current/sync/on-entry`);

  expect(cards.request.method).toBe('GET');
  expect(syncState.request.method).toBe('GET');
  expect(onEntry.request.method).toBe('POST');
  expect(cards.request.headers.get('Authorization')).toMatch(/^Bearer /);
  expect(syncState.request.headers.get('Authorization')).toMatch(/^Bearer /);
  expect(onEntry.request.headers.get('Authorization')).toMatch(/^Bearer /);
  cards.flush([]);
  syncState.flush({
    updateNeeded: false,
    reauthorizationRequired: false,
    lastSuccessfulImportAtUtc: null,
    sourceUpdatedAtUtc: null,
    lastAttemptAtUtc: null,
    nextAllowedAtUtc: null,
    latestExecution: null,
  });
  onEntry.flush({ decision: 'not_required' });
}

// Exercita o shell real com HTTP interceptado para detectar chamadas acidentais ao montar páginas.
describe('Página inicial pública', () => {
  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [...(appConfig.providers ?? []), provideHttpClientTesting()],
    });
  });

  afterEach(() => {
    TestBed.inject(HttpTestingController).verify({ ignoreCancelled: true });
    vi.useRealTimers();
    vi.restoreAllMocks();
  });

  // Confirma que a página identificável e seu texto de preparação renderizam sem backend.
  it('exibe a marca e o estado de preparação sem API ou sessão', async () => {
    const harness = await RouterTestingHarness.create();
    await harness.navigateByUrl('/', HomePage);
    const page = harness.routeNativeElement as HTMLElement;

    expect(page.querySelector('section[aria-labelledby="home-title"]')).not.toBeNull();
    expect(page.querySelector('h1')?.textContent).toContain('O Finora está sendo preparado');
    expect(page.textContent).toContain('Em preparação');
  });

  // Impede que exemplos, valores agregados ou estados de conexão apareçam como dados reais.
  it('não apresenta valores ou estados financeiros demonstrativos', async () => {
    const harness = await RouterTestingHarness.create();
    await harness.navigateByUrl('/', HomePage);
    const text = harness.routeNativeElement?.textContent?.toLocaleLowerCase('pt-BR') ?? '';

    expect(text).not.toMatch(/r\$\s*[\d.,]+/);
    expect(text).not.toMatch(/\b\d{1,3}(?:\.\d{3})*(?:,\d{2})\b/);
    expect(text).not.toMatch(/\b(?:saldo|receita|gasto|fatura|conectado|sincronizado)\b/);
  });

  // Confirma o retorno ao início e o indicador de foco do link da página não encontrada.
  it('mostra 404 para rota desconhecida e permite retornar ao início', async () => {
    const harness = await RouterTestingHarness.create();
    await harness.navigateByUrl('/pagina-inexistente', NotFoundPage);
    const page = harness.routeNativeElement;
    const returnLink = page?.querySelector<HTMLAnchorElement>('a[href="/"]');
    const links = Array.from(page?.querySelectorAll<HTMLAnchorElement>('a') ?? []);

    expect(page?.querySelector('h1')?.textContent).toContain('Esta página não existe');
    expect(returnLink?.textContent).toContain('Voltar ao início');
    expect(returnLink?.classList.contains('focus-visible:outline-solid')).toBe(true);
    expect(returnLink?.classList.contains('outline-finora-brand')).toBe(true);
    expect(links.map((link) => link.getAttribute('href'))).toEqual(['/']);
    expect(page?.querySelectorAll('button, form')).toHaveLength(0);
    await harness.navigateByUrl('/', HomePage);
    expect(harness.routeNativeElement?.querySelector('h1')?.textContent).toContain(
      'O Finora está sendo preparado',
    );
  });

  // Mantém só início/login no menu público e confirma ausência de chamadas financeiras nas duas rotas.
  it('vincula a navegação pública às rotas entregues', async () => {
    const fixture = TestBed.createComponent(App);
    fixture.detectChanges();
    await TestBed.inject(Router).navigateByUrl('/');
    await fixture.whenStable();
    fixture.detectChanges();
    const shell = fixture.nativeElement as HTMLElement;
    const navigation = Array.from(
      shell.querySelectorAll<HTMLAnchorElement>('nav[aria-label="Navegação principal"] a'),
    );
    const linkTargets = Array.from(shell.querySelectorAll<HTMLAnchorElement>('a')).map((link) =>
      link.getAttribute('href'),
    );
    const unavailablePaths = [
      '/dashboard',
      '/card',
      '/transactions',
      '/investments',
      '/simulator',
      '/sync',
    ];

    expect(routes.map((route) => route.path)).toEqual(['', 'login', 'area', '**']);
    expect(navigation.map((link) => link.getAttribute('href'))).toEqual([
      '/',
      '/login',
      '/',
      '/login',
    ]);
    expect(unavailablePaths.some((path) => linkTargets.includes(path))).toBe(false);
    expect(navigation.every((link) => link.classList.contains('focus-visible:outline-solid'))).toBe(
      true,
    );
    expect(navigation.every((link) => !link.classList.contains('outline-none'))).toBe(true);
    expect(navigation[0]?.classList.contains('outline-finora-focus')).toBe(true);
    expect(navigation[2]?.classList.contains('outline-finora-brand')).toBe(true);
    expect(shell.querySelector('aside')?.classList.contains('mobile:flex')).toBe(true);
    expect(shell.querySelector('nav.fixed')?.classList.contains('mobile:hidden')).toBe(true);
    expect(shell.querySelectorAll('button, form, input, select, textarea')).toHaveLength(0);
    expect(shell.querySelector('header')).not.toBeNull();
    expect(shell.querySelector('header')?.textContent).toContain('Aplicação pública');
    expect(shell.querySelector('main#conteudo[tabindex="-1"]')).not.toBeNull();
    expect(shell.querySelector('a[href="#conteudo"]')?.textContent).toContain(
      'Pular para o conteúdo',
    );
    expect(shell.querySelectorAll('nav a[aria-current="page"]')).toHaveLength(2);
    expect(shell.textContent).toContain('Entrar');
    TestBed.inject(HttpTestingController).expectNone(() => true);

    await TestBed.inject(Router).navigateByUrl('/login');
    await fixture.whenStable();
    fixture.detectChanges();
    expect(shell.textContent).toContain('Entrar no Finora');
    TestBed.inject(HttpTestingController).expectNone(() => true);
    fixture.destroy();
  });

  // Reflete sessão ativa nos dois menus e nunca inclui o token em markup ou URL.
  it('vincula a navegação autenticada à área e à saída', async () => {
    TestBed.inject(SessionService).startSession({
      accessToken: 'synthetic-shell-token',
      tokenType: 'Bearer',
      expiresIn: 1800,
      expiresAtUtc: new Date(Date.now() + 30 * 60 * 1000).toISOString(),
    });
    const fixture = TestBed.createComponent(App);
    fixture.detectChanges();
    await TestBed.inject(Router).navigateByUrl('/');
    await fixture.whenStable();
    fixture.detectChanges();
    const shell = fixture.nativeElement as HTMLElement;
    const navigation = Array.from(
      shell.querySelectorAll<HTMLAnchorElement>('nav[aria-label="Navegação principal"] a'),
    );
    const linkTargets = Array.from(shell.querySelectorAll<HTMLAnchorElement>('a')).map((link) =>
      link.getAttribute('href'),
    );

    expect(navigation.map((link) => link.getAttribute('href'))).toEqual([
      '/',
      '/area',
      '/',
      '/area',
    ]);
    expect(navigation.map((link) => link.getAttribute('aria-label'))).toEqual([
      'Início',
      'Cartões e conexão',
      'Início',
      'Cartões e conexão',
    ]);
    expect(navigation[1]?.textContent).toContain('Cartões e conexão');
    expect(navigation[3]?.textContent).toContain('Cartões');
    expect(shell.querySelector('app-home a[href="/area"]')?.textContent).toContain(
      'Abrir cartões e conexão',
    );
    expect(shell.querySelectorAll('button[aria-label="Sair da sessão"]')).toHaveLength(1);
    expect(shell.querySelectorAll('nav button')).toHaveLength(2);
    expect(shell.querySelector('header')?.textContent).toContain('Sessão autenticada');
    expect(shell.textContent).toContain('Sair');
    expect(linkTargets.some((path) => path?.includes('synthetic-shell-token'))).toBe(false);
    expect(shell.innerHTML).not.toContain('synthetic-shell-token');
    expect(TestBed.inject(Router).url).not.toContain('synthetic-shell-token');
    expect(
      linkTargets
        .filter((path) => path?.startsWith('/'))
        .every((path) => ['/', '/area', '/login'].includes(path ?? '')),
    ).toBe(true);
    TestBed.inject(HttpTestingController).expectNone(() => true);
    fixture.destroy();
  });

  // Exercita login, leituras protegidas de cartões/conexão e logout sem expor token no shell.
  it('percorre login, área de cartões e conexão e logout confirmado', async () => {
    const fixture = TestBed.createComponent(App);
    fixture.detectChanges();
    const router = TestBed.inject(Router);
    await router.navigateByUrl('/login');
    await fixture.whenStable();
    const outlet = fixture.debugElement.query(By.directive(RouterOutlet)).injector.get(RouterOutlet);
    const login = outlet.component as LoginPage;
    login.emailControl.setValue('member@example.test');
    login.passwordControl.setValue('synthetic-flow-password');
    login.submit();
    TestBed.inject(HttpTestingController)
      .expectOne('http://127.0.0.1:5080/api/auth/login')
      .flush({
        accessToken: 'synthetic-flow-token',
        tokenType: 'Bearer',
        expiresIn: 1800,
        expiresAtUtc: new Date(Date.now() + 30 * 60 * 1000).toISOString(),
      });
    await fixture.whenStable();
    fixture.detectChanges();

    expect(router.url).toBe('/area');
    expect(fixture.nativeElement.textContent).toContain('Cartões e conexão');
    expect(fixture.nativeElement.innerHTML).not.toContain('synthetic-flow-token');
    flushAuthenticatedAreaStartup(TestBed.inject(HttpTestingController));
    fixture.detectChanges();
    expect(fixture.nativeElement.textContent).toContain(
      'Não há cartões disponíveis nos dados persistidos',
    );

    const exit = fixture.nativeElement.querySelector('nav.fixed button') as HTMLButtonElement;
    exit.click();
    fixture.detectChanges();
    TestBed.inject(HttpTestingController)
      .expectOne('http://127.0.0.1:5080/api/auth/logout')
      .flush(null, { status: 204, statusText: 'No Content' });
    await fixture.whenStable();
    fixture.detectChanges();

    expect(router.url).toBe('/login');
    expect(fixture.nativeElement.textContent).toContain('Saída confirmada pelo servidor');
    expect(fixture.nativeElement.querySelector('[role="status"]')?.textContent).toContain(
      'Saída confirmada',
    );
    expect(TestBed.inject(SessionService).currentSession()).toBeNull();
    expect(router.url).not.toContain('synthetic-flow-token');
    fixture.destroy();
  });

  // Uma falha no logout limpa a área e comunica incerteza sem renderizar payload remoto.
  it('apresenta saída incerta após falha remota de logout', async () => {
    TestBed.inject(SessionService).startSession({
      accessToken: 'synthetic-uncertain-logout-token',
      tokenType: 'Bearer',
      expiresIn: 1800,
      expiresAtUtc: new Date(Date.now() + 30 * 60 * 1000).toISOString(),
    });
    const fixture = TestBed.createComponent(App);
    fixture.detectChanges();
    const router = TestBed.inject(Router);
    await router.navigateByUrl('/area');
    fixture.detectChanges();
    flushAuthenticatedAreaStartup(TestBed.inject(HttpTestingController));
    (fixture.nativeElement.querySelector('nav.fixed button') as HTMLButtonElement).click();
    fixture.detectChanges();
    TestBed.inject(HttpTestingController).expectOne('http://127.0.0.1:5080/api/auth/logout').flush(
      { detail: 'synthetic-uncertain-server-detail' },
      { status: 503, statusText: 'Service Unavailable' },
    );
    await fixture.whenStable();
    fixture.detectChanges();

    expect(router.url).toBe('/login');
    expect(TestBed.inject(SessionService).currentSession()).toBeNull();
    expect(fixture.nativeElement.querySelector('[role="status"]')?.textContent).toContain(
      'não foi possível confirmar a saída no servidor',
    );
    expect(fixture.nativeElement.textContent).not.toContain('synthetic-uncertain-server-detail');
    fixture.destroy();
  });

  // Um 401 protegido encerra a geração e encaminha para login com aviso sem conteúdo remoto.
  it('redireciona após 401 protegido e apresenta orientação sanitizada', async () => {
    TestBed.inject(SessionService).startSession({
      accessToken: 'synthetic-401-token',
      tokenType: 'Bearer',
      expiresIn: 1800,
      expiresAtUtc: new Date(Date.now() + 30 * 60 * 1000).toISOString(),
    });
    const fixture = TestBed.createComponent(App);
    fixture.detectChanges();
    const router = TestBed.inject(Router);
    await router.navigateByUrl('/area');
    fixture.detectChanges();
    flushAuthenticatedAreaStartup(TestBed.inject(HttpTestingController));
    TestBed.inject(HttpClient).get('http://127.0.0.1:5080/api/private/resource').subscribe({
      error: () => undefined,
    });
    TestBed.inject(HttpTestingController)
      .expectOne('http://127.0.0.1:5080/api/private/resource')
      .flush({ detail: 'synthetic-secret-server-detail' }, { status: 401, statusText: 'Unauthorized' });
    await fixture.whenStable();
    fixture.detectChanges();

    expect(router.url).toBe('/login');
    expect(TestBed.inject(SessionService).currentSession()).toBeNull();
    expect(fixture.nativeElement.textContent).toContain('A sessão não está mais válida');
    expect(fixture.nativeElement.textContent).not.toContain('synthetic-secret-server-detail');
    fixture.destroy();
  });

  // Um 403 informa falta de permissão sem limpar sessão nem disparar login em repetição.
  it('preserva área e sessão em 403 protegido com aviso seguro', async () => {
    TestBed.inject(SessionService).startSession({
      accessToken: 'synthetic-403-token',
      tokenType: 'Bearer',
      expiresIn: 1800,
      expiresAtUtc: new Date(Date.now() + 30 * 60 * 1000).toISOString(),
    });
    const fixture = TestBed.createComponent(App);
    fixture.detectChanges();
    const router = TestBed.inject(Router);
    await router.navigateByUrl('/area');
    fixture.detectChanges();
    flushAuthenticatedAreaStartup(TestBed.inject(HttpTestingController));
    TestBed.inject(HttpClient).get('http://127.0.0.1:5080/api/private/resource').subscribe({
      error: () => undefined,
    });
    TestBed.inject(HttpTestingController)
      .expectOne('http://127.0.0.1:5080/api/private/resource')
      .flush({ detail: 'synthetic-secret-server-detail' }, { status: 403, statusText: 'Forbidden' });
    await fixture.whenStable();
    fixture.detectChanges();

    expect(router.url).toBe('/area');
    expect(TestBed.inject(SessionService).currentSession()?.accessToken).toBe('synthetic-403-token');
    expect(fixture.nativeElement.textContent).toContain('não tem permissão');
    expect(fixture.nativeElement.textContent).not.toContain('synthetic-secret-server-detail');
    expect(fixture.nativeElement.querySelector('app-authenticated-area')).not.toBeNull();
    fixture.destroy();
  });

  // Expiração passiva também encerra a área e comunica a necessidade de entrar novamente.
  it('redireciona após expiração passiva e mostra aviso acessível', async () => {
    const initialTime = Date.parse('2026-11-25T12:00:00.000Z');
    const clock = vi.spyOn(Date, 'now').mockReturnValue(initialTime);
    const session = TestBed.inject(SessionService);
    session.startSession({
      accessToken: 'synthetic-expiring-token',
      tokenType: 'Bearer',
      expiresIn: 1800,
      expiresAtUtc: new Date(Date.now() + 1000).toISOString(),
    });
    const fixture = TestBed.createComponent(App);
    fixture.detectChanges();
    const router = TestBed.inject(Router);
    await router.navigateByUrl('/area');
    fixture.detectChanges();
    flushAuthenticatedAreaStartup(TestBed.inject(HttpTestingController));
    clock.mockReturnValue(initialTime + 1001);
    session.currentSession();
    await fixture.whenStable();
    fixture.detectChanges();

    expect(router.url).toBe('/login');
    expect(session.currentSession()).toBeNull();
    expect(fixture.nativeElement.textContent).toContain('Sua sessão expirou');
    expect(fixture.nativeElement.querySelector('[role="alert"]')?.textContent).toContain(
      'Entre novamente',
    );
    fixture.destroy();
  });
});

// Confirma a composição de infraestrutura HTTP e origem pública sem consumir a API.
describe('Composição pública da API', () => {
  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [...(appConfig.providers ?? []), provideHttpClientTesting()],
    });
  });

  // A instância do cliente e a URL de desenvolvimento ficam disponíveis sem iniciar requisições.
  it('disponibiliza HttpClient e a origem local sem enviar chamadas', () => {
    expect(TestBed.inject(HttpClient)).toBeInstanceOf(HttpClient);
    expect(TestBed.inject(API_BASE_URL)).toBe('http://127.0.0.1:5080');
  });

  // Garante que o bootstrap e a navegação pública não consultem login ou endpoints financeiros.
  it('não inicia requests ao montar o shell público', () => {
    const fixture = TestBed.createComponent(App);
    fixture.detectChanges();

    TestBed.inject(HttpTestingController).expectNone(() => true);
    fixture.destroy();
  });
});
