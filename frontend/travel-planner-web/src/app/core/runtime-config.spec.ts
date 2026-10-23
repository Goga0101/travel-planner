import { HttpClient, provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { apiBaseUrlInterceptor, loadRuntimeConfig, normalizeApiBaseUrl, runtimeConfig, setRuntimeConfigForTests } from './runtime-config';

describe('runtime config', () => {
  afterEach(() => setRuntimeConfigForTests({ apiBaseUrl: '' }));

  it('normalizes API base URLs and rejects unsafe values', () => {
    expect(normalizeApiBaseUrl('https://api.example.com/')).toBe('https://api.example.com');
    expect(normalizeApiBaseUrl('http://localhost:5080')).toBe('http://localhost:5080');
    expect(normalizeApiBaseUrl('javascript:alert(1)')).toBe('');
    expect(normalizeApiBaseUrl('not a url')).toBe('');
    expect(normalizeApiBaseUrl(undefined)).toBe('');
  });

  it('loads config.json and keeps defaults when it is missing', async () => {
    await loadRuntimeConfig((async () => new Response(JSON.stringify({ apiBaseUrl: 'https://api.example.com' }))) as typeof fetch);
    expect(runtimeConfig().apiBaseUrl).toBe('https://api.example.com');

    setRuntimeConfigForTests({ apiBaseUrl: '' });
    await loadRuntimeConfig((async () => new Response('', { status: 404 })) as typeof fetch);
    expect(runtimeConfig().apiBaseUrl).toBe('');
  });

  it('prefixes API calls only', () => {
    setRuntimeConfigForTests({ apiBaseUrl: 'https://api.example.com' });
    TestBed.configureTestingModule({
      providers: [provideHttpClient(withInterceptors([apiBaseUrlInterceptor])), provideHttpClientTesting()],
    });
    const http = TestBed.inject(HttpClient);
    const ctrl = TestBed.inject(HttpTestingController);

    http.get('/api/v1/client-config').subscribe();
    http.get('config.json').subscribe();
    ctrl.expectOne('https://api.example.com/api/v1/client-config').flush({});
    ctrl.expectOne('config.json').flush({});
    ctrl.verify();
  });
});
