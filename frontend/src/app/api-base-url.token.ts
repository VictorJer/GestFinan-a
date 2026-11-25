import { InjectionToken } from '@angular/core';

// Disponibiliza somente a origem pública da API, sem representar credencial ou sessão.
export const API_BASE_URL = new InjectionToken<string | null>('FINORA_API_BASE_URL');
