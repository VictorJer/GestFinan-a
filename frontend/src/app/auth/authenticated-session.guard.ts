import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { SessionService } from './session';

// Autoriza somente a sessão local ainda válida e envia visitantes ao login sem preservar URL aberta.
export const authenticatedSessionGuard: CanActivateFn = () => {
  const session = inject(SessionService);
  const router = inject(Router);

  return session.currentSession() === null ? router.parseUrl('/login') : true;
};
