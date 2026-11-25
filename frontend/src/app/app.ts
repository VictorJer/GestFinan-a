import { Component, computed, effect, inject } from '@angular/core';
import { Router, RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { LogoutService } from './auth/logout';
import { SessionService } from './auth/session';

// Compõe navegação reativa e encaminha a saída pelo fluxo remoto de logout.
@Component({
  imports: [RouterLink, RouterLinkActive, RouterOutlet],
  selector: 'app-root',
  templateUrl: './app.html',
})
export class App {
  private readonly router = inject(Router);
  private readonly session = inject(SessionService);
  private readonly logoutService = inject(LogoutService);

  readonly isAuthenticated = this.session.isAuthenticated;
  readonly isLoggingOut = this.logoutService.isLoggingOut;
  readonly logoutOutcome = this.logoutService.outcome;
  readonly sessionNotice = this.session.notice;
  // Mantém as mensagens de navegação fechadas e fixas, sem refletir texto do servidor.
  readonly sessionNoticeMessage = computed(() => {
    switch (this.sessionNotice()) {
      case 'expired':
        return 'Sua sessão expirou. Entre novamente.';
      case 'unauthorized':
        return 'A sessão não está mais válida. Entre novamente.';
      case 'forbidden':
        return 'Sua sessão não tem permissão para esta operação.';
      case 'network':
        return 'Não foi possível conectar à API. Tente novamente.';
      case 'unavailable':
        return 'A API está temporariamente indisponível. Tente novamente mais tarde.';
      case 'request-failed':
        return 'Não foi possível concluir a operação.';
      default:
        return null;
    }
  });

  constructor() {
    // Redireciona para entrada quando sessão expira ou 401 a encerra, sem repetir autenticação.
    effect(() => {
      const notice = this.sessionNotice();
      const authenticationEnded = notice === 'expired' || notice === 'unauthorized';
      const logoutFinished = this.logoutOutcome() !== null;
      if (
        logoutFinished ||
        (authenticationEnded && this.router.url.startsWith('/area'))
      ) {
        void this.router.navigateByUrl('/login');
      }
    });
  }

  // Mantém o botão responsivo conectado ao mesmo serviço e à geração atual da sessão.
  logout(): void {
    this.logoutService.logout();
  }

  // Dispensa o aviso textual sem reabrir sessão nem alterar o estado de autorização.
  clearSessionNotice(): void {
    this.session.clearNotice();
  }
}
