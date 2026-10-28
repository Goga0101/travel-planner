import { ChangeDetectionStrategy, Component, DestroyRef, ElementRef, inject, input, model, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { Subject, catchError, debounceTime, distinctUntilChanged, of, switchMap, tap } from 'rxjs';
import { ApiService } from '../../core/api.service';
import { TranslationKey } from '../../core/i18n/en';
import { I18nService } from '../../core/i18n/i18n.service';
import { TranslatePipe } from '../../core/i18n/translate.pipe';
import { PlaceKind, PlaceSuggestionDto } from '../../core/models';
import { IconComponent, IconName } from '../../shared/icon.component';

const KIND_ICONS: Record<PlaceKind, IconName> = { address: 'pin', stop: 'bus', city: 'globe', place: 'landmark' };

/**
 * A place anywhere in the world, typed in any language: suggestions appear while typing (addresses, stops, landmarks)
 * and "my location" uses the device's GPS.
 */
@Component({
  selector: 'app-place-input',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [TranslatePipe, IconComponent],
  host: { '(document:click)': 'onOutside($event)' },
  template: `
    <div class="place-input" [class.open]="open() && suggestions().length > 0">
      <label class="label" [for]="inputId()">
        <span class="place-dot" [attr.data-end]="end()"></span> {{ label() | t }}
      </label>
      <div class="place-box">
        <input [id]="inputId()" type="search" class="input" autocomplete="off" enterkeyhint="search" role="combobox"
               [attr.aria-expanded]="open() && suggestions().length > 0" [attr.aria-controls]="inputId() + '-list'"
               [attr.aria-activedescendant]="active() >= 0 ? inputId() + '-opt-' + active() : null"
               [placeholder]="placeholder() | t" [value]="text()"
               (input)="onInput($any($event.target).value)" (focus)="open.set(true)" (keydown)="onKey($event)" />
        @if (loading()) { <span class="spinner" aria-hidden="true"></span> }
        @if (gps()) {
          <button type="button" class="place-gps" (click)="locate()" [disabled]="locating()" [title]="'transit.myLocation' | t">
            @if (locating()) { <span class="spinner" aria-hidden="true"></span> } @else { <app-icon name="compass" [size]="18" /> }
            <span class="place-gps-text">{{ 'transit.myLocation' | t }}</span>
          </button>
        }
      </div>
      @if (gpsError()) { <p class="small field-error">{{ gpsError()! | t }}</p> }
      @if (open() && suggestions().length) {
        <ul class="place-list" role="listbox" [id]="inputId() + '-list'">
          @for (s of suggestions(); track $index; let i = $index) {
            <li role="option" [id]="inputId() + '-opt-' + i" [class.active]="i === active()" [attr.aria-selected]="i === active()"
                (mousedown)="$event.preventDefault()" (click)="pick(s)">
              <span class="place-kind" [attr.data-kind]="s.kind"><app-icon [name]="icons[s.kind]" [size]="15" /></span>
              <span class="place-text">
                <strong>{{ s.name }}</strong>
                @if (s.area) { <span class="small muted">{{ s.area }}</span> }
              </span>
            </li>
          }
        </ul>
      } @else if (open() && searched() && !loading() && text().length >= 2 && !value()) {
        <p class="place-empty small muted">{{ 'transit.noPlaces' | t }}</p>
      }
    </div>
  `,
})
export class PlaceInputComponent {
  readonly inputId = input.required<string>();
  readonly label = input.required<TranslationKey>();
  readonly placeholder = input.required<TranslationKey>();
  /** 'from' or 'to' (marker colour). */
  readonly end = input<'from' | 'to'>('from');
  readonly gps = input(false);
  /** Bias suggestions towards this point (e.g. the other end of the trip). */
  readonly near = input<{ latitude: number; longitude: number } | null>(null);
  readonly value = model<PlaceSuggestionDto | null>(null);

  private readonly api = inject(ApiService);
  private readonly i18n = inject(I18nService);
  private readonly host = inject(ElementRef<HTMLElement>);
  protected readonly icons = KIND_ICONS;

  protected readonly text = signal('');
  protected readonly suggestions = signal<PlaceSuggestionDto[]>([]);
  protected readonly loading = signal(false);
  protected readonly searched = signal(false);
  protected readonly open = signal(false);
  protected readonly active = signal(-1);
  protected readonly locating = signal(false);
  protected readonly gpsError = signal<TranslationKey | null>(null);
  private readonly queries = new Subject<string>();

  constructor() {
    this.queries
      .pipe(
        debounceTime(300),
        distinctUntilChanged(),
        tap((q) => {
          this.loading.set(q.length >= 2);
          this.searched.set(false);
        }),
        switchMap((q) =>
          q.length < 2 ? of([]) : this.api.findPlaces(q, this.near() ?? undefined).pipe(catchError(() => of([] as PlaceSuggestionDto[]))),
        ),
        takeUntilDestroyed(inject(DestroyRef)),
      )
      .subscribe((list) => {
        this.suggestions.set(list);
        this.active.set(list.length ? 0 : -1);
        this.loading.set(false);
        this.searched.set(true);
      });
  }

  /** Shows a place chosen elsewhere (swap, a link) without searching. */
  set(place: PlaceSuggestionDto | null): void {
    this.value.set(place);
    this.text.set(place?.name ?? '');
    this.suggestions.set([]);
  }

  protected onInput(text: string): void {
    this.text.set(text);
    this.open.set(true);
    if (this.value()) this.value.set(null);
    this.queries.next(text.trim());
  }

  protected onKey(e: KeyboardEvent): void {
    const list = this.suggestions();
    if (e.key === 'ArrowDown' && list.length) {
      e.preventDefault();
      this.open.set(true);
      this.active.set((this.active() + 1) % list.length);
    } else if (e.key === 'ArrowUp' && list.length) {
      e.preventDefault();
      this.active.set((this.active() - 1 + list.length) % list.length);
    } else if (e.key === 'Enter' && this.open() && this.active() >= 0 && list[this.active()]) {
      e.preventDefault();
      this.pick(list[this.active()]);
    } else if (e.key === 'Escape') {
      this.open.set(false);
    }
  }

  protected pick(place: PlaceSuggestionDto): void {
    this.value.set(place);
    this.text.set(place.name);
    this.open.set(false);
  }

  protected onOutside(e: Event): void {
    if (!this.host.nativeElement.contains(e.target as Node)) this.open.set(false);
  }

  protected locate(): void {
    this.gpsError.set(null);
    if (typeof navigator === 'undefined' || !navigator.geolocation) {
      this.gpsError.set('transit.gpsUnavailable');
      return;
    }
    this.locating.set(true);
    navigator.geolocation.getCurrentPosition(
      (pos) => {
        const { latitude, longitude } = pos.coords;
        const fallback: PlaceSuggestionDto = { name: this.i18n.t('transit.myLocation'), latitude, longitude, kind: 'address' };
        this.api.reversePlace(latitude, longitude).pipe(catchError(() => of(null))).subscribe((p) => {
          this.locating.set(false);
          // Keep the exact GPS point; the address only names it.
          this.pick(p ? { ...p, name: `${this.i18n.t('transit.myLocation')} · ${p.name}`, latitude, longitude } : fallback);
        });
      },
      (err) => {
        this.locating.set(false);
        this.gpsError.set(err.code === err.PERMISSION_DENIED ? 'transit.gpsDenied' : 'transit.gpsUnavailable');
      },
      { enableHighAccuracy: true, timeout: 15000, maximumAge: 60000 },
    );
  }
}
