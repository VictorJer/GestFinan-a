import { HttpClient } from '@angular/common/http';
import { inject, Injectable, signal } from '@angular/core';
import { finalize, timeout } from 'rxjs';
import { NORMALIZED_API_BASE_URL } from '../api-base-url.token';
import { SessionService } from './session';

export type LogoutOutcome = 'confirmed' | 'uncertain';

// Solicita revogação remota uma vez e expõe somente o grau seguro de confirmação ao shell.
@Injectable({ providedIn: 'root' })
export class LogoutService {
  private readonly http = inject(HttpClient);
  private readonly apiBase = inject(NORMALIZED_API_BASE_URL);
  private readonly session = inject(SessionService);
  private readonly pendingGenerations = new Set<number>();

  readonly isLoggingOut = signal(false);
  readonly outcome = signal<LogoutOutcome | null>(null);

  // Remove aviso antigo quando um novo fluxo de autenticação passa a ser apresentado.
  clearOutcome(): void {
    this.outcome.set(null);
  }

  // Usa somente a geração atual; toda tentativa termina localmente, inclusive em falha/timeout.
  logout(): void {
    const activeSession = this.session.currentSession();
    if (activeSession === null || this.pendingGenerations.has(activeSession.generation)) {
      return;
    }

    const generation = activeSession.generation;
    this.outcome.set(null);
    this.pendingGenerations.add(generation);
    this.isLoggingOut.set(true);

    if (this.apiBase === null) {
      this.finishAttempt(generation, 'uncertain');
      this.finishPending(generation);
      return;
    }

    const apiPath = this.apiBase.pathPrefix.toLowerCase().endsWith('/api')
      ? this.apiBase.pathPrefix
      : `${this.apiBase.pathPrefix}/api`;
    const logoutUrl = new URL(`${apiPath}/auth/logout`, this.apiBase.origin).toString();

    this.http
      .post<unknown>(logoutUrl, null, { observe: 'response' })
      .pipe(
        // Um logout sem resposta em tempo razoável limpa o acesso local e não anuncia confirmação.
        timeout({ first: 15_000 }),
        finalize(() => this.finishPending(generation)),
      )
      .subscribe({
        next: (response) => {
          this.finishAttempt(generation, response.status === 204 ? 'confirmed' : 'uncertain');
        },
        error: () => this.finishAttempt(generation, 'uncertain'),
      });
  }

  // Respostas atrasadas não apagam nem exibem resultado sobre uma sessão criada depois.
  private finishAttempt(generation: number, outcome: LogoutOutcome): void {
    const currentSession = this.session.currentSession();
    const newerSessionExists =
      currentSession !== null && currentSession.generation !== generation;

    this.session.endSessionForGeneration(generation, null);
    if (!newerSessionExists) {
      this.session.clearNotice();
      this.outcome.set(outcome);
    }
  }

  // Mantém o indicador coerente quando uma tentativa é encerrada ou cancelada.
  private finishPending(generation: number): void {
    this.pendingGenerations.delete(generation);
    this.isLoggingOut.set(this.pendingGenerations.size > 0);
  }
}
