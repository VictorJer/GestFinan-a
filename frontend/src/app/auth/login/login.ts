import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { Component, inject, signal } from '@angular/core';
import {
  AbstractControl,
  FormControl,
  FormGroup,
  ReactiveFormsModule,
  ValidationErrors,
  Validators,
} from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { finalize } from 'rxjs';
import { NORMALIZED_API_BASE_URL } from '../../api-base-url.token';
import { LogoutService } from '../logout';
import { SessionService } from '../session';

// Valida o endereço aparado sem alterar o valor controlado nem o conteúdo digitado pela pessoa.
function trimmedEmailValidator(control: AbstractControl): ValidationErrors | null {
  const value = typeof control.value === 'string' ? control.value.trim() : '';
  if (value.length === 0) {
    return { required: true };
  }

  if (value.length > 256) {
    return { maxlength: { requiredLength: 256, actualLength: value.length } };
  }

  return Validators.email(new FormControl(value));
}

// Mapeia apenas status conhecidos para mensagens fixas, sem consultar ou exibir o corpo remoto.
function loginFailureMessage(error: unknown): string {
  if (!(error instanceof HttpErrorResponse)) {
    return 'Não foi possível concluir o login. Tente novamente.';
  }

  switch (error.status) {
    case 0:
      return 'Não foi possível conectar à API. Verifique sua conexão e tente novamente.';
    case 400:
      return 'Confira os dados informados e tente novamente.';
    case 401:
      return 'E-mail ou senha inválidos.';
    case 413:
      return 'Os dados informados excedem o limite permitido.';
    case 429:
      return 'Muitas tentativas em pouco tempo. Aguarde antes de tentar novamente.';
    case 500:
      return 'Não foi possível processar o login. Tente novamente.';
    case 503:
      return 'A autenticação está temporariamente indisponível. Tente novamente mais tarde.';
    default:
      return 'Não foi possível concluir o login. Tente novamente.';
  }
}

// Envia credenciais somente à API configurada e inicia sessão apenas após validar a resposta.
@Component({
  imports: [ReactiveFormsModule, RouterLink],
  selector: 'app-login-page',
  templateUrl: './login.html',
})
export class LoginPage {
  private readonly http = inject(HttpClient);
  private readonly router = inject(Router);
  private readonly apiBase = inject(NORMALIZED_API_BASE_URL);
  private readonly logout = inject(LogoutService);
  private readonly session = inject(SessionService);

  readonly isSubmitting = signal(false);
  readonly message = signal<string | null>(null);
  readonly form = new FormGroup({
    email: new FormControl('', {
      nonNullable: true,
      validators: [trimmedEmailValidator, Validators.maxLength(256)],
    }),
    password: new FormControl('', {
      nonNullable: true,
      validators: [Validators.required, Validators.maxLength(1024)],
    }),
  });

  // Entrega controles tipados ao template para associar erros e estado acessível aos campos.
  get emailControl(): FormControl<string> {
    return this.form.controls.email;
  }

  get passwordControl(): FormControl<string> {
    return this.form.controls.password;
  }

  // Bloqueia entradas inválidas e não envia senha transformada nem para uma origem inferida.
  submit(): void {
    if (this.isSubmitting()) {
      return;
    }

    this.form.markAllAsTouched();
    this.message.set(null);
    if (this.form.invalid) {
      return;
    }

    if (this.apiBase === null) {
      this.message.set('O login está indisponível porque a API não está configurada neste ambiente.');
      return;
    }

    const apiPath = this.apiBase.pathPrefix.toLowerCase().endsWith('/api')
      ? this.apiBase.pathPrefix
      : `${this.apiBase.pathPrefix}/api`;
    const loginUrl = new URL(`${apiPath}/auth/login`, this.apiBase.origin).toString();
    const credentials = {
      email: this.emailControl.value.trim(),
      password: this.passwordControl.value,
    };

    this.isSubmitting.set(true);
    this.http
      .post<unknown>(loginUrl, credentials)
      .pipe(finalize(() => this.isSubmitting.set(false)))
      .subscribe({
        next: (response: unknown) => {
          if (this.session.startSession(response) === null) {
            this.message.set('Não foi possível iniciar a sessão. Tente novamente.');
            return;
          }

          this.logout.clearOutcome();
          void this.router.navigateByUrl('/area');
        },
        error: (error: unknown) => this.message.set(loginFailureMessage(error)),
      });
  }

  // Remove avisos anteriores quando a pessoa altera os dados, sem refletir o conteúdo digitado.
  clearMessage(): void {
    this.message.set(null);
  }
}
