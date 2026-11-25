import { Component, inject } from '@angular/core';
import { RouterLink } from '@angular/router';
import { SessionService } from '../auth/session';

// Exibe a página pública e aponta somente para a próxima tela disponível no estado local.
@Component({
  imports: [RouterLink],
  selector: 'app-home',
  templateUrl: './home.html',
})
export class HomePage {
  readonly isAuthenticated = inject(SessionService).isAuthenticated;
}
