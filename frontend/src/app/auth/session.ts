import { computed, DestroyRef, inject, Injectable, signal } from '@angular/core';

const TOKEN_LIFETIME_SECONDS = 1800;
const CLOCK_AND_RESPONSE_TOLERANCE_MS = 5000;
const UTC_TIMESTAMP_PATTERN =
  /^(\d{4})-(\d{2})-(\d{2})T(\d{2}):(\d{2}):(\d{2})(?:\.\d+)?(?:Z|\+00:00)$/i;

/** Expõe ao transporte apenas o token da sessão atual e seu identificador de geração. */
export interface ActiveSession {
  readonly accessToken: string;
  readonly expiresAtUtc: string;
  readonly generation: number;
}

export type SessionNotice =
  | 'expired'
  | 'unauthorized'
  | 'forbidden'
  | 'network'
  | 'unavailable'
  | 'request-failed';

interface StoredSession extends ActiveSession {
  readonly expiresAtEpochMs: number;
}

interface ValidLoginResponse {
  readonly accessToken: string;
  readonly tokenType: string;
  readonly expiresIn: number;
  readonly expiresAtUtc: string;
}

// Mantém credenciais somente em memória e invalida timers/respostas de gerações anteriores.
@Injectable({ providedIn: 'root' })
export class SessionService {
  private readonly destroyRef = inject(DestroyRef);
  private readonly sessionState = signal<StoredSession | null>(null);
  private generationSequence = 0;
  private expirationTimer: ReturnType<typeof setTimeout> | undefined;

  readonly isAuthenticated = computed(() => this.sessionState() !== null);
  readonly expiresAtUtc = computed(() => this.sessionState()?.expiresAtUtc ?? null);
  // Mantém somente um motivo seguro de interface, nunca corpo HTTP ou credencial.
  readonly notice = signal<SessionNotice | null>(null);

  constructor() {
    // Descarta o timer quando o injector raiz termina, como ocorre ao recarregar a aplicação.
    this.destroyRef.onDestroy(() => this.cancelExpirationTimer());
  }

  // Só instala uma sessão após validar integralmente o contrato Bearer e sua expiração futura.
  startSession(response: unknown): number | null {
    const validated = this.validateLoginResponse(response, Date.now());
    if (validated === null) {
      return null;
    }

    this.cancelExpirationTimer();
    const generation = ++this.generationSequence;
    const session: StoredSession = {
      ...validated,
      generation,
    };
    this.sessionState.set(session);
    this.notice.set(null);
    this.scheduleExpiration(session);
    return generation;
  }

  // Entrega ao interceptor uma fotografia da geração ainda válida, limpando expiração já vencida.
  currentSession(): ActiveSession | null {
    this.expireIfNecessary();
    const session = this.sessionState();
    if (session === null) {
      return null;
    }

    return {
      accessToken: session.accessToken,
      expiresAtUtc: session.expiresAtUtc,
      generation: session.generation,
    };
  }

  // Uma resposta atrasada só pode encerrar a mesma geração que originou sua requisição.
  endSessionForGeneration(expectedGeneration: number, notice: SessionNotice | null = null): boolean {
    const session = this.sessionState();
    if (session === null || session.generation !== expectedGeneration) {
      return false;
    }

    this.cancelExpirationTimer();
    this.sessionState.set(null);
    this.notice.set(notice);
    return true;
  }

  // Publica avisos de chamadas protegidas somente enquanto a geração iniciadora continua ativa.
  setNoticeForGeneration(expectedGeneration: number, notice: SessionNotice): boolean {
    const session = this.currentSession();
    if (session === null || session.generation !== expectedGeneration) {
      return false;
    }

    this.notice.set(notice);
    return true;
  }

  // Permite dispensar apenas o texto seguro, sem alterar autorização ou duração da sessão.
  clearNotice(): void {
    this.notice.set(null);
  }

  // Rejeita respostas incompletas, tipos de token inesperados e durações incompatíveis com a API.
  private validateLoginResponse(response: unknown, now: number): Omit<StoredSession, 'generation'> | null {
    if (typeof response !== 'object' || response === null || Array.isArray(response)) {
      return null;
    }

    const candidate = response as Record<string, unknown>;
    const accessToken = candidate['accessToken'];
    const tokenType = candidate['tokenType'];
    const expiresIn = candidate['expiresIn'];
    const expiresAtUtc = candidate['expiresAtUtc'];

    if (
      typeof accessToken !== 'string' ||
      accessToken.length === 0 ||
      accessToken.trim() !== accessToken ||
      typeof tokenType !== 'string' ||
      tokenType.toLowerCase() !== 'bearer' ||
      typeof expiresIn !== 'number' ||
      !Number.isInteger(expiresIn) ||
      expiresIn !== TOKEN_LIFETIME_SECONDS ||
      typeof expiresAtUtc !== 'string'
    ) {
      return null;
    }

    const expiresAtEpochMs = this.parseUtcTimestamp(expiresAtUtc);
    if (expiresAtEpochMs === null) {
      return null;
    }

    const remainingLifetimeMs = expiresAtEpochMs - now;
    const reportedLifetimeMs = expiresIn * 1000;
    if (
      remainingLifetimeMs <= 0 ||
      remainingLifetimeMs > reportedLifetimeMs + CLOCK_AND_RESPONSE_TOLERANCE_MS
    ) {
      return null;
    }

    return { accessToken, expiresAtUtc, expiresAtEpochMs };
  }

  // Exige timestamp ISO com fuso UTC e rejeita datas que o parser normalizaria silenciosamente.
  private parseUtcTimestamp(value: string): number | null {
    const parts = UTC_TIMESTAMP_PATTERN.exec(value);
    const timestamp = Date.parse(value);
    if (parts === null || !Number.isFinite(timestamp)) {
      return null;
    }

    const parsed = new Date(timestamp);
    const [year, month, day, hour, minute, second] = parts.slice(1, 7).map(Number);
    if (
      parsed.getUTCFullYear() !== year ||
      parsed.getUTCMonth() + 1 !== month ||
      parsed.getUTCDate() !== day ||
      parsed.getUTCHours() !== hour ||
      parsed.getUTCMinutes() !== minute ||
      parsed.getUTCSeconds() !== second
    ) {
      return null;
    }

    return timestamp;
  }

  // O timer encerra a sessão mesmo sem tráfego e confere a geração antes de tocar o estado atual.
  private scheduleExpiration(session: StoredSession): void {
    const delay = Math.max(0, session.expiresAtEpochMs - Date.now());
    this.expirationTimer = setTimeout(() => {
      this.expirationTimer = undefined;
      const current = this.sessionState();
      if (current === null || current.generation !== session.generation) {
        return;
      }

      this.expireIfNecessary();
      const stillCurrent = this.sessionState();
      if (stillCurrent !== null && stillCurrent.generation === session.generation) {
        this.scheduleExpiration(stillCurrent);
      }
    }, delay);
  }

  // Confere a validade também em cada leitura para não depender da pontualidade do event loop.
  private expireIfNecessary(): void {
    const session = this.sessionState();
    if (session !== null && Date.now() >= session.expiresAtEpochMs) {
      this.endSessionForGeneration(session.generation, 'expired');
    }
  }

  // Cancela qualquer timer anterior antes de substituir ou encerrar uma sessão.
  private cancelExpirationTimer(): void {
    if (this.expirationTimer !== undefined) {
      clearTimeout(this.expirationTimer);
      this.expirationTimer = undefined;
    }
  }
}
