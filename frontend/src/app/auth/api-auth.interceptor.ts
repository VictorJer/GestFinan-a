import { HttpErrorResponse, HttpInterceptorFn, HttpRequest } from '@angular/common/http';
import { inject } from '@angular/core';
import { catchError, throwError } from 'rxjs';
import { NORMALIZED_API_BASE_URL, NormalizedApiBaseUrl } from '../api-base-url.token';
import { SessionService } from './session';

export type ProtectedApiErrorKind =
  | 'unauthorized'
  | 'forbidden'
  | 'network'
  | 'unavailable'
  | 'request-failed'
  | 'stale';

// Converte falhas protegidas em estados seguros, sem repassar payloads potencialmente sensíveis.
export class ProtectedApiRequestError extends Error {
  constructor(readonly kind: ProtectedApiErrorKind) {
    const messages: Record<ProtectedApiErrorKind, string> = {
      unauthorized: 'A sessão não está mais válida. Entre novamente.',
      forbidden: 'Sua sessão não tem permissão para esta operação.',
      network: 'Não foi possível conectar à API.',
      unavailable: 'A API está temporariamente indisponível.',
      'request-failed': 'Não foi possível concluir a operação.',
      stale: 'A solicitação pertence a uma sessão anterior.',
    };
    super(messages[kind]);
    this.name = 'ProtectedApiRequestError';
  }
}

// Distingue uma chamada protegida sem sessão de falhas HTTP reais, sem expor dados do request.
export class LocalSessionRequiredError extends Error {
  constructor() {
    super('É necessário entrar novamente para acessar esta operação.');
    this.name = 'LocalSessionRequiredError';
  }
}

// O token só sai para rotas da API configurada e protegidas pela sessão local atual.
export const apiAuthInterceptor: HttpInterceptorFn = (request, next) => {
  const apiBase = inject(NORMALIZED_API_BASE_URL);
  const target = parseAbsoluteHttpUrl(request.urlWithParams);
  if (apiBase === null || target === null || !isProtectedApiRequest(request, target, apiBase)) {
    return next(withoutBearerHeader(request));
  }

  const sessionService = inject(SessionService);
  const session = sessionService.currentSession();
  if (session === null) {
    return throwError(() => new LocalSessionRequiredError());
  }

  return next(
    request.clone({
      setHeaders: { Authorization: `Bearer ${session.accessToken}` },
    }),
  ).pipe(
    // Só classifica respostas da geração que enviou o request; falhas antigas não afetam sessão nova.
    catchError((error: unknown) => {
      if (!(error instanceof HttpErrorResponse)) {
        return throwError(() => error);
      }

      const currentSession = sessionService.currentSession();
      if (currentSession === null || currentSession.generation !== session.generation) {
        return throwError(() => new ProtectedApiRequestError('stale'));
      }

      if (error.status === 401) {
        if (!sessionService.endSessionForGeneration(session.generation, 'unauthorized')) {
          return throwError(() => new ProtectedApiRequestError('stale'));
        }

        return throwError(() => new ProtectedApiRequestError('unauthorized'));
      }

      if (error.status === 403) {
        sessionService.setNoticeForGeneration(session.generation, 'forbidden');
        return throwError(() => new ProtectedApiRequestError('forbidden'));
      }

      if (error.status === 0) {
        sessionService.setNoticeForGeneration(session.generation, 'network');
        return throwError(() => new ProtectedApiRequestError('network'));
      }

      if (error.status >= 500) {
        sessionService.setNoticeForGeneration(session.generation, 'unavailable');
        return throwError(() => new ProtectedApiRequestError('unavailable'));
      }

      sessionService.setNoticeForGeneration(session.generation, 'request-failed');
      return throwError(() => new ProtectedApiRequestError('request-failed'));
    }),
  );
};

// Recusa URLs relativas, esquemas não HTTP e destinos com credenciais embutidas.
function parseAbsoluteHttpUrl(value: string): URL | null {
  try {
    const url = new URL(value);
    if (
      (url.protocol !== 'http:' && url.protocol !== 'https:') ||
      url.username.length > 0 ||
      url.password.length > 0
    ) {
      return null;
    }

    return url;
  } catch {
    return null;
  }
}

// Compara origem e fronteira de segmento; um prefixo textual parecido não representa a mesma base.
function isProtectedApiRequest(
  request: HttpRequest<unknown>,
  target: URL,
  base: NormalizedApiBaseUrl,
): boolean {
  if (target.origin !== base.origin || request.method.toUpperCase() === 'OPTIONS') {
    return false;
  }

  const apiPath = base.pathPrefix.endsWith('/api')
    ? base.pathPrefix
    : `${base.pathPrefix}/api`;
  const protectedPathPrefix = `${apiPath}/`;
  if (!target.pathname.startsWith(protectedPathPrefix)) {
    return false;
  }

  const normalizedPath = target.pathname.replace(/\/+$/, '').toLowerCase();
  return normalizedPath !== `${apiPath}/auth/login`.toLowerCase();
}

// Impede que um Bearer previamente definido pelo chamador escape para login, URL externa ou página pública.
function withoutBearerHeader(request: HttpRequest<unknown>): HttpRequest<unknown> {
  const authorization = request.headers.get('Authorization');
  if (authorization === null || !/^Bearer(?:\s|$)/i.test(authorization)) {
    return request;
  }

  return request.clone({ headers: request.headers.delete('Authorization') });
}
