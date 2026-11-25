import { provideHttpClient } from '@angular/common/http';
import { ApplicationConfig, provideBrowserGlobalErrorListeners } from '@angular/core';
import { provideRouter } from '@angular/router';
import { environment } from '../environments/environment';
import { API_BASE_URL } from './api-base-url.token';
import { routes } from './app.routes';

export const appConfig: ApplicationConfig = {
  providers: [
    provideBrowserGlobalErrorListeners(),
    // Compõe o cliente HTTP para serviços futuros sem enviar requests ao iniciar a aplicação.
    provideHttpClient(),
    // Expõe somente a origem pública do build, sem credenciais ou token de sessão.
    { provide: API_BASE_URL, useValue: environment.apiBaseUrl },
    provideRouter(routes),
  ]
};
