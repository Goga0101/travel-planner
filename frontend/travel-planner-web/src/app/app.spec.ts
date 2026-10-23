import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { App } from './app';

describe('App shell', () => {
  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [App],
      providers: [provideRouter([]), provideHttpClient(), provideHttpClientTesting()],
    }).compileComponents();
  });

  it('shows the demo banner when the server runs in demo mode', async () => {
    const fixture = TestBed.createComponent(App);
    const http = TestBed.inject(HttpTestingController);
    fixture.detectChanges();
    http.expectOne('/api/v1/client-config').flush({
      dataMode: 'Demo', providers: [], supportedCurrencies: ['EUR'], minDurationDays: 2, maxDurationDays: 30,
      maxTravelers: 9, maxSearchWindowDays: 60, maxManualDestinations: 10,
    });
    await fixture.whenStable();
    fixture.detectChanges();
    // Georgian is the default language.
    expect((fixture.nativeElement as HTMLElement).querySelector('.demo-banner')?.textContent).toContain('დემო რეჟიმი');
  });

  it('hides the demo banner in live mode', async () => {
    const fixture = TestBed.createComponent(App);
    const http = TestBed.inject(HttpTestingController);
    fixture.detectChanges();
    http.expectOne('/api/v1/client-config').flush({
      dataMode: 'Live', providers: [], supportedCurrencies: ['EUR'], minDurationDays: 2, maxDurationDays: 30,
      maxTravelers: 9, maxSearchWindowDays: 60, maxManualDestinations: 10,
    });
    await fixture.whenStable();
    fixture.detectChanges();
    expect((fixture.nativeElement as HTMLElement).querySelector('.demo-banner')).toBeNull();
  });
});
