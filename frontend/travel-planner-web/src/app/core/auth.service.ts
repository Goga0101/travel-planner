import { HttpErrorResponse, HttpInterceptorFn } from '@angular/common/http';
import { Injectable, computed, inject, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { ApiService } from './api.service';
import { ClientConfigService } from './client-config.service';
import { TranslationKey } from './i18n/en';
import { AuthProvider, AuthResultDto, UserDto } from './models';

const STORAGE_KEY = 'tp.auth';

interface StoredSession {
  token: string;
  expiresAtUtc: string;
  user: UserDto;
}

/* Minimal typings of the two provider SDKs (loaded at runtime from Google / Facebook). */
interface GoogleId {
  initialize(options: { client_id: string; callback: (r: { credential: string }) => void; auto_select?: boolean; ux_mode?: 'popup';
    use_fedcm_for_button?: boolean }): void;
  renderButton(el: HTMLElement, options: Record<string, unknown>): void;
  disableAutoSelect(): void;
}
interface FacebookSdk {
  init(options: { appId: string; version: string; cookie?: boolean; xfbml?: boolean }): void;
  login(callback: (r: { authResponse?: { accessToken: string } | null; status: string }) => void, options: { scope: string }): void;
}
declare global {
  interface Window {
    google?: { accounts: { id: GoogleId } };
    FB?: FacebookSdk;
    fbAsyncInit?: () => void;
  }
}

/**
 * Sign-in with Google or Facebook. The provider's SDK gives the browser a token (Google: ID token, Facebook: access token);
 * the API verifies it with the provider and returns its own token, kept in this browser (localStorage) and sent as
 * "Authorization: Bearer" with API calls. Nothing else of the provider session is kept here.
 */
@Injectable({ providedIn: 'root' })
export class AuthService {
  private readonly api = inject(ApiService);
  private readonly config = inject(ClientConfigService);
  private readonly session = signal<StoredSession | null>(readSession());

  readonly user = computed(() => this.session()?.user ?? null);
  readonly signedIn = computed(() => this.session() !== null);
  /** Provider currently signing in, for a spinner on its button. */
  readonly busy = signal<AuthProvider | null>(null);
  /** Message key of the last failure, shown under the buttons. */
  readonly error = signal<TranslationKey | null>(null);

  readonly googleClientId = computed(() => this.config.config()?.googleClientId ?? null);
  readonly facebookAppId = computed(() => this.config.config()?.facebookAppId ?? null);
  readonly available = computed(() => !!this.googleClientId() || !!this.facebookAppId());

  private googleReady?: Promise<GoogleId>;
  private facebookReady?: Promise<FacebookSdk>;

  constructor() {
    // A stored sign-in is refreshed from the API (new photo or name); a token the API no longer accepts signs out.
    // Deferred: the request goes through authInterceptor, which needs this service fully constructed.
    if (this.session()) {
      queueMicrotask(() => this.api.getMe().subscribe({
        next: (user) => { const s = this.session(); if (s) this.store({ ...s, user }); },
        error: (e: HttpErrorResponse) => { if (e.status === 401) this.signOut(); },
      }));
    }
  }

  /** The token for API calls, or null (none or expired). */
  token(): string | null {
    const s = this.session();
    if (!s) return null;
    if (Date.parse(s.expiresAtUtc) <= Date.now()) {
      this.signOut();
      return null;
    }
    return s.token;
  }

  /** Deletes the account on the server (profile and provider links), then signs out here. */
  async deleteAccount(): Promise<boolean> {
    try {
      await firstValueFrom(this.api.deleteMe());
      this.signOut();
      return true;
    } catch {
      this.error.set('auth.deleteFailed');
      return false;
    }
  }

  signOut(): void {
    this.store(null);
    window.google?.accounts.id.disableAutoSelect();
  }

  /** Renders Google's own sign-in button (its look is set by Google's branding rules) into the element. */
  async renderGoogleButton(el: HTMLElement, lang: string, width: number): Promise<void> {
    const id = await this.loadGoogle();
    el.replaceChildren();
    id.renderButton(el, { type: 'standard', theme: 'outline', size: 'large', shape: 'pill', text: 'continue_with', logo_alignment: 'left',
      width: Math.round(width), locale: lang });
  }

  /** Loads the Facebook SDK early: FB.login must run straight from the click, or browsers block its popup. */
  prepareFacebook(): void {
    void this.loadFacebook().catch(() => undefined);
  }

  async signInWithFacebook(): Promise<void> {
    this.error.set(null);
    let fb: FacebookSdk;
    try { fb = await this.loadFacebook(); } catch { this.error.set('auth.loadFailed'); return; }
    fb.login((r) => {
      if (r.authResponse?.accessToken) void this.exchange('Facebook', r.authResponse.accessToken);
      else this.error.set('auth.cancelled');
    }, { scope: 'public_profile,email' });
  }

  private async exchange(provider: AuthProvider, token: string): Promise<void> {
    this.busy.set(provider);
    this.error.set(null);
    try {
      const r: AuthResultDto = await firstValueFrom(this.api.signIn(provider, token));
      this.store({ token: r.accessToken, expiresAtUtc: r.expiresAtUtc, user: r.user });
    } catch {
      this.error.set('auth.failed');
    } finally {
      this.busy.set(null);
    }
  }

  private loadGoogle(): Promise<GoogleId> {
    const clientId = this.googleClientId();
    if (!clientId) return Promise.reject(new Error('Google sign-in is off'));
    this.googleReady ??= loadScript('https://accounts.google.com/gsi/client').then(() => {
      const id = window.google!.accounts.id;
      id.initialize({ client_id: clientId, ux_mode: 'popup', auto_select: false, use_fedcm_for_button: true,
        callback: (r) => void this.exchange('Google', r.credential) });
      return id;
    });
    return this.googleReady;
  }

  private loadFacebook(): Promise<FacebookSdk> {
    const appId = this.facebookAppId();
    if (!appId) return Promise.reject(new Error('Facebook sign-in is off'));
    this.facebookReady ??= new Promise<FacebookSdk>((resolve, reject) => {
      window.fbAsyncInit = () => {
        window.FB!.init({ appId, version: 'v23.0', cookie: false, xfbml: false });
        resolve(window.FB!);
      };
      loadScript('https://connect.facebook.net/en_US/sdk.js').catch(reject);
    });
    return this.facebookReady;
  }

  private store(s: StoredSession | null): void {
    this.session.set(s);
    try {
      if (s) localStorage.setItem(STORAGE_KEY, JSON.stringify(s));
      else localStorage.removeItem(STORAGE_KEY);
    } catch {
      // Storage blocked: the sign-in lasts for this page only.
    }
  }
}

function readSession(): StoredSession | null {
  try {
    const s = JSON.parse(localStorage.getItem(STORAGE_KEY) ?? 'null') as StoredSession | null;
    return s?.token && Date.parse(s.expiresAtUtc) > Date.now() ? s : null;
  } catch {
    return null;
  }
}

function loadScript(src: string): Promise<void> {
  return new Promise((resolve, reject) => {
    const existing = document.querySelector<HTMLScriptElement>(`script[src="${src}"]`);
    if (existing) { resolve(); return; }
    const s = document.createElement('script');
    s.src = src;
    s.async = true;
    s.defer = true;
    s.onload = () => resolve();
    s.onerror = () => reject(new Error(`Could not load ${src}`));
    document.head.appendChild(s);
  });
}

/** Sends the sign-in token with this app's API calls (never to other sites). */
export const authInterceptor: HttpInterceptorFn = (req, next) => {
  if (!req.url.startsWith('/api/')) return next(req);
  const token = inject(AuthService).token();
  return next(token ? req.clone({ setHeaders: { Authorization: `Bearer ${token}` } }) : req);
};
