import { inject, InjectionToken } from '@angular/core';

// Disponibiliza somente a origem pública da API, sem representar credencial ou sessão.
export const API_BASE_URL = new InjectionToken<string | null>('FINORA_API_BASE_URL');

export interface NormalizedApiBaseUrl {
  readonly origin: string;
  readonly pathPrefix: string;
}

// Aceita somente URL HTTP(S) absoluta e remove a barra final sem perder um prefixo de caminho.
export function normalizeApiBaseUrl(value: string | null): NormalizedApiBaseUrl | null {
  if (value === null || value.length === 0 || value.trim() !== value) {
    return null;
  }

  try {
    const url = new URL(value);
    if (
      (url.protocol !== 'http:' && url.protocol !== 'https:') ||
      url.username.length > 0 ||
      url.password.length > 0 ||
      url.search.length > 0 ||
      url.hash.length > 0
    ) {
      return null;
    }

    return Object.freeze({
      origin: url.origin,
      pathPrefix: url.pathname.replace(/\/+$/, ''),
    });
  } catch {
    return null;
  }
}

// Normaliza a configuração uma vez no injector raiz para a mesma regra proteger todos os requests.
export const NORMALIZED_API_BASE_URL = new InjectionToken<NormalizedApiBaseUrl | null>(
  'FINORA_NORMALIZED_API_BASE_URL',
  {
    providedIn: 'root',
    factory: () => normalizeApiBaseUrl(inject(API_BASE_URL)),
  },
);
