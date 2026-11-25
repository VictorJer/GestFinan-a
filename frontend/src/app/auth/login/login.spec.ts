import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { Component } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter, Router } from '@angular/router';
import { API_BASE_URL } from '../../api-base-url.token';
import { apiAuthInterceptor } from '../api-auth.interceptor';
import { SessionService } from '../session';
import { LoginPage } from './login';
import { vi } from 'vitest';

const API_BASE = 'https://api.example.test/proxy/';
const LOGIN_URL = 'https://api.example.test/proxy/api/auth/login';

// Fornece um destino de navegação protegido sintético sem introduzir serviços de domínio.
@Component({
  standalone: true,
  template: '<p>Destino de teste</p>',
})
class TestAreaPage {}

let fixture: ComponentFixture<LoginPage>;
let page: LoginPage;
let backend: HttpTestingController;

// Isola formulário, interceptor, router e servidor HTTP simulado em cada caso.
function configureLogin(apiBaseUrl: string | null = API_BASE): void {
  TestBed.configureTestingModule({
    imports: [LoginPage],
    providers: [
      { provide: API_BASE_URL, useValue: apiBaseUrl },
      provideRouter([{ path: 'area', component: TestAreaPage }]),
      provideHttpClient(withInterceptors([apiAuthInterceptor])),
      provideHttpClientTesting(),
    ],
  });
  fixture = TestBed.createComponent(LoginPage);
  page = fixture.componentInstance;
  backend = TestBed.inject(HttpTestingController);
  fixture.detectChanges();
}

// Preenche dados sintéticos preservando intencionalmente qualquer espaço da senha.
function fillValidForm(email = 'member@example.test', password = 'synthetic password'): void {
  page.emailControl.setValue(email);
  page.passwordControl.setValue(password);
}

// Cria resposta de login válida sem usar conta ou token real.
function validLoginResponse(): Record<string, unknown> {
  return {
    accessToken: 'synthetic-login-token',
    tokenType: 'Bearer',
    expiresIn: 1800,
    expiresAtUtc: new Date(Date.now() + 30 * 60 * 1000).toISOString(),
  };
}

// Verifica contrato de entrada, feedback seguro e ausência de credenciais na apresentação.
describe('Formulário de login', () => {
  beforeEach(() => configureLogin());

  afterEach(() => backend.verify());

  // Envia somente o e-mail aparado, preserva a senha e instala sessão após resposta válida.
  it('faz login válido e navega sem exibir o token', async () => {
    fillValidForm('  member@example.test  ', '  exact password  ');
    page.submit();

    const request = backend.expectOne(LOGIN_URL);
    expect(request.request.method).toBe('POST');
    expect(request.request.body).toEqual({
      email: 'member@example.test',
      password: '  exact password  ',
    });
    expect(page.emailControl.value).toBe('  member@example.test  ');
    request.flush(validLoginResponse());
    await fixture.whenStable();

    expect(TestBed.inject(SessionService).currentSession()?.accessToken).toBe(
      'synthetic-login-token',
    );
    expect(TestBed.inject(Router).url).toBe('/area');
    expect(fixture.nativeElement.textContent).not.toContain('synthetic-login-token');
  });

  // Impede credenciais acima dos limites contratuais antes de criar qualquer requisição.
  it.each([
    ['e-mail', 'email'],
    ['senha', 'password'],
  ] as const)('bloqueia %s acima do limite', (_label, field) => {
    const oversized = 'x'.repeat(field === 'email' ? 257 : 1025);
    if (field === 'email') {
      fillValidForm(oversized);
    } else {
      fillValidForm('member@example.test', oversized);
    }

    page.submit();

    expect(page.form.invalid).toBe(true);
    backend.expectNone(LOGIN_URL);
  });

  // Os limites dos campos mantêm até conteúdo com escape JSON abaixo do teto de 16 KiB da API.
  it('mantém o corpo serializado abaixo de 16 KiB no maior valor de senha aceito', () => {
    fillValidForm('member@example.test', '\u0000'.repeat(1024));
    page.submit();
    const request = backend.expectOne(LOGIN_URL);
    const encodedBody = new TextEncoder().encode(JSON.stringify(request.request.body));

    expect(page.form.valid).toBe(true);
    expect(encodedBody.byteLength).toBeLessThan(16 * 1024);
    request.flush({}, { status: 401, statusText: 'Unauthorized' });
  });

  // Traduz respostas previstas pelo contrato em mensagens fixas sem refletir o payload remoto.
  it.each([
    [400, 'Confira os dados informados e tente novamente.'],
    [401, 'E-mail ou senha inválidos.'],
    [413, 'Os dados informados excedem o limite permitido.'],
    [429, 'Muitas tentativas em pouco tempo. Aguarde antes de tentar novamente.'],
    [500, 'Não foi possível processar o login. Tente novamente.'],
    [503, 'A autenticação está temporariamente indisponível. Tente novamente mais tarde.'],
  ])('mapeia HTTP %s para orientação fixa e segura', (status, expectedMessage) => {
    fillValidForm('member@example.test', 'synthetic-secret-password');
    const errorLog = vi.spyOn(console, 'error');
    page.submit();
    backend.expectOne(LOGIN_URL).flush(
      { detail: 'synthetic-secret-response', accessToken: 'synthetic-secret-token' },
      { status, statusText: 'Synthetic failure' },
    );

    expect(page.message()).toBe(expectedMessage);
    expect(fixture.nativeElement.textContent).not.toContain('synthetic-secret-password');
    expect(fixture.nativeElement.textContent).not.toContain('synthetic-secret-response');
    expect(fixture.nativeElement.textContent).not.toContain('synthetic-secret-token');
    expect(errorLog).not.toHaveBeenCalled();
    errorLog.mockRestore();
  });

  // Falha de rede não é apresentada como senha inválida nem expõe o objeto do navegador.
  it('distingue falha de rede de credenciais inválidas', () => {
    fillValidForm();
    page.submit();
    backend.expectOne(LOGIN_URL).error(new ProgressEvent('error'));

    expect(page.message()).toBe(
      'Não foi possível conectar à API. Verifique sua conexão e tente novamente.',
    );
    expect(page.message()).not.toContain('synthetic password');
  });

  // Sem configuração explícita, bloqueia o envio em vez de inferir a origem do próprio frontend.
  it('não envia credenciais quando a URL da API está ausente', () => {
    TestBed.resetTestingModule();
    configureLogin(null);
    fillValidForm();

    page.submit();

    expect(page.message()).toBe(
      'O login está indisponível porque a API não está configurada neste ambiente.',
    );
    backend.expectNone(() => true);
  });

  // Sucesso HTTP com resposta inválida não cria sessão nem torna o corpo sensível visível.
  it('recusa resposta de sucesso sem contrato Bearer utilizável', () => {
    fillValidForm();
    page.submit();
    backend.expectOne(LOGIN_URL).flush({
      accessToken: 'synthetic-invalid-response-token',
      tokenType: 'Basic',
      detail: 'synthetic-secret-response',
    });

    expect(TestBed.inject(SessionService).currentSession()).toBeNull();
    expect(page.message()).toBe('Não foi possível iniciar a sessão. Tente novamente.');
    expect(fixture.nativeElement.textContent).not.toContain('synthetic-invalid-response-token');
    expect(fixture.nativeElement.textContent).not.toContain('synthetic-secret-response');
  });

  // Mantém os campos rotulados, operáveis por teclado e com feedback textual associado.
  it('renderiza campos acessíveis e impede envio duplicado durante a requisição', () => {
    const root = fixture.nativeElement as HTMLElement;
    const email = root.querySelector<HTMLInputElement>('#login-email');
    const password = root.querySelector<HTMLInputElement>('#login-password');

    expect(root.querySelector('label[for="login-email"]')?.textContent).toContain('E-mail');
    expect(root.querySelector('label[for="login-password"]')?.textContent).toContain('Senha');
    expect(email?.getAttribute('autocomplete')).toBe('username');
    expect(password?.getAttribute('autocomplete')).toBe('current-password');

    fillValidForm();
    page.submit();
    expect(page.isSubmitting()).toBe(true);
    page.submit();
    const requests = backend.match(LOGIN_URL);
    expect(requests).toHaveLength(1);
    requests[0].flush(validLoginResponse());
  });
});
