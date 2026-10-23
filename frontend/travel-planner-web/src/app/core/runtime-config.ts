import { HttpInterceptorFn } from '@angular/common/http';

/**
 * Deployment settings loaded at startup from `config.json` (next to index.html), so the API address can change
 * without rebuilding the app. Empty `apiBaseUrl` = same origin (dev proxy / nginx).
 */
export interface RuntimeConfig {
  apiBaseUrl: string;
}

const config: RuntimeConfig = { apiBaseUrl: '' };

export function runtimeConfig(): Readonly<RuntimeConfig> {
  return config;
}

/** Normalizes and validates the configured API base URL (absolute http(s) URL or empty). */
export function normalizeApiBaseUrl(value: unknown): string {
  if (typeof value !== 'string' || value.trim() === '') return '';
  try {
    const url = new URL(value.trim());
    if (url.protocol !== 'https:' && url.protocol !== 'http:') return '';
    return url.origin + url.pathname.replace(/\/+$/, '');
  } catch {
    return '';
  }
}

/** Resolved relative to the document base href, so it works under a sub-path such as /travel-planner/. */
export async function loadRuntimeConfig(fetchFn: typeof fetch = fetch): Promise<void> {
  try {
    const response = await fetchFn('config.json', { cache: 'no-store' });
    if (!response.ok) return;
    const json = (await response.json()) as Partial<RuntimeConfig>;
    config.apiBaseUrl = normalizeApiBaseUrl(json.apiBaseUrl);
  } catch {
    // No config file: keep same-origin defaults.
  }
}

/** Prefixes relative API calls with the configured API origin. Must run after interceptors that match '/api/'. */
export const apiBaseUrlInterceptor: HttpInterceptorFn = (req, next) => {
  const base = config.apiBaseUrl;
  if (!base || !(req.url.startsWith('/api/') || req.url.startsWith('/health'))) return next(req);
  return next(req.clone({ url: base + req.url }));
};

export function setRuntimeConfigForTests(value: Partial<RuntimeConfig>): void {
  config.apiBaseUrl = normalizeApiBaseUrl(value.apiBaseUrl);
}
