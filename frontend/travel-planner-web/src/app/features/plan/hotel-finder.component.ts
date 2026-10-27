import { ChangeDetectionStrategy, Component, DestroyRef, computed, inject, input, output, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { Subject, catchError, debounceTime, distinctUntilChanged, of, switchMap, tap } from 'rxjs';
import { ApiService } from '../../core/api.service';
import { TranslatePipe } from '../../core/i18n/translate.pipe';
import { HotelSearchResultDto } from '../../core/models';
import { SelectedStayService } from '../../core/selected-stay.service';
import { IconComponent } from '../../shared/icon.component';
import { DistancePipe } from '../../shared/pipes';

/**
 * "I already found my hotel": type (part of) its name, pick it from the matches in this city and it becomes the stay —
 * the airport route and the day plan then start there.
 */
@Component({
  selector: 'app-hotel-finder',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [TranslatePipe, IconComponent, DistancePipe],
  template: `
    <div class="hotel-finder">
      @if (stay(); as st) {
        <p class="finder-chosen">
          <app-icon name="checkCircle" [size]="16" />
          <span>{{ 'finder.chosen' | t }} <strong>{{ st.name }}</strong></span>
          <button type="button" class="text-link small" (click)="stays.clear(planId())"><app-icon name="x" [size]="13" /> {{ 'route.clearStay' | t }}</button>
        </p>
      }
      <label class="label" for="hotel-finder-input"><app-icon name="search" [size]="15" /> {{ 'finder.label' | t }}</label>
      <div class="finder-box">
        <input id="hotel-finder-input" type="search" class="input" autocomplete="off" [placeholder]="'finder.placeholder' | t"
               [value]="text()" (input)="onInput($any($event.target).value)" />
        @if (loading()) { <span class="spinner" aria-hidden="true"></span> }
      </div>
      <p class="small muted">{{ 'finder.hint' | t }}</p>

      @if (searched()) {
        @if (results().length) {
          <ul class="finder-results" aria-live="polite">
            @for (h of results(); track h.id) {
              @let selected = stays.isSelected(planId(), h.latitude, h.longitude);
              <li class="finder-hit" [class.is-selected]="selected">
                <span class="finder-icon"><app-icon name="bed" [size]="16" /></span>
                <span class="finder-text">
                  <strong>{{ h.name }}</strong>
                  <span class="small muted">@if (h.address) { {{ h.address }} · }{{ 'nearby.fromCentre' | t: { distance: (h.distanceFromCentreMeters | distance) } }}</span>
                </span>
                <span class="finder-actions">
                  <button type="button" class="btn btn-sm stay-select" [class.is-selected]="selected" [attr.aria-pressed]="selected" (click)="choose(h)">
                    <app-icon name="check" [size]="14" /> {{ (selected ? 'stay.selected' : 'stay.select') | t }}
                  </button>
                  @if (h.bookingUrl) {
                    <a class="text-link small" [href]="h.bookingUrl" target="_blank" rel="noopener noreferrer nofollow">Booking.com <app-icon name="external" [size]="12" /></a>
                  }
                  @if (h.mapUrl) {
                    <a class="text-link small" [href]="h.mapUrl" target="_blank" rel="noopener noreferrer nofollow">{{ 'days.map' | t }} <app-icon name="external" [size]="12" /></a>
                  }
                </span>
              </li>
            }
          </ul>
        } @else if (!loading()) {
          <p class="small muted">{{ 'finder.none' | t: { text: text() } }}</p>
        }
      }
    </div>
  `,
})
export class HotelFinderComponent {
  readonly planId = input.required<string>();
  /** Emitted when a hotel is chosen (not when unchosen). */
  readonly chosen = output<void>();

  private readonly api = inject(ApiService);
  protected readonly stays = inject(SelectedStayService);
  protected readonly text = signal('');
  protected readonly results = signal<HotelSearchResultDto[]>([]);
  protected readonly loading = signal(false);
  protected readonly searched = signal(false);
  protected readonly stay = computed(() => {
    this.stays.all();
    return this.stays.get(this.planId());
  });

  private readonly input$ = new Subject<string>();

  constructor() {
    this.input$
      .pipe(
        debounceTime(350),
        distinctUntilChanged(),
        tap((t) => {
          this.loading.set(t.length >= 2);
          if (t.length < 2) {
            this.results.set([]);
            this.searched.set(false);
          }
        }),
        switchMap((t) => (t.length < 2 ? of(null) : this.api.searchHotels(this.planId(), t).pipe(catchError(() => of([] as HotelSearchResultDto[]))))),
        takeUntilDestroyed(inject(DestroyRef)),
      )
      .subscribe((r) => {
        if (r === null) return;
        this.results.set(r);
        this.searched.set(true);
        this.loading.set(false);
      });
  }

  protected onInput(value: string): void {
    this.text.set(value);
    this.input$.next(value.trim());
  }

  protected choose(h: HotelSearchResultDto): void {
    const was = this.stays.isSelected(this.planId(), h.latitude, h.longitude);
    this.stays.toggle(this.planId(), { name: h.name, latitude: h.latitude, longitude: h.longitude });
    if (!was) this.chosen.emit();
  }
}
