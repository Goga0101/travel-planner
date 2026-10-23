import { TestBed } from '@angular/core/testing';
import { ExternalLinkComponent, safeHttpsUrl } from './external-link.component';

describe('safeHttpsUrl', () => {
  it('accepts absolute https URLs', () => {
    expect(safeHttpsUrl('https://www.booking.com/hotel/x.html?aid=1')).toBe('https://www.booking.com/hotel/x.html?aid=1');
  });

  it.each([
    'javascript:alert(1)',
    'http://example.com',
    'data:text/html,hi',
    '//evil.example',
    '/relative/path',
    'https://user:pass@example.com',
    '',
    null,
    undefined,
  ])('rejects %s', (value) => {
    expect(safeHttpsUrl(value as string | null | undefined)).toBeNull();
  });
});

describe('ExternalLinkComponent', () => {
  it('renders a hardened link for safe URLs', () => {
    const fixture = TestBed.createComponent(ExternalLinkComponent);
    fixture.componentRef.setInput('href', 'https://www.aviasales.com/search/TBS2011BCN24111');
    fixture.componentRef.setInput('label', 'Book');
    fixture.detectChanges();
    const a = (fixture.nativeElement as HTMLElement).querySelector('a')!;
    expect(a.getAttribute('target')).toBe('_blank');
    expect(a.getAttribute('rel')).toBe('noopener noreferrer nofollow');
    expect(a.textContent).toContain('Book');
  });

  it('renders plain text (no anchor) for unsafe URLs', () => {
    const fixture = TestBed.createComponent(ExternalLinkComponent);
    fixture.componentRef.setInput('href', 'javascript:alert(1)');
    fixture.componentRef.setInput('label', 'Book');
    fixture.componentRef.setInput('fallback', 'No link');
    fixture.detectChanges();
    const el = fixture.nativeElement as HTMLElement;
    expect(el.querySelector('a')).toBeNull();
    expect(el.textContent).toContain('No link');
  });
});
