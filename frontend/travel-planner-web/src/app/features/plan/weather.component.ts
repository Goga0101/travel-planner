import { ChangeDetectionStrategy, Component, DestroyRef, effect, inject, input, signal, untracked } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { toIsoDate } from '../../core/format';
import { TranslatePipe } from '../../core/i18n/translate.pipe';
import { WeatherOutlook, WeatherService } from '../../core/weather';
import { IconComponent } from '../../shared/icon.component';
import { LocalDatePipe } from '../../shared/pipes';

/** Daily weather for the trip dates: a forecast when close enough, otherwise last year's weather on the same dates. */
@Component({
  selector: 'app-weather',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [TranslatePipe, IconComponent, LocalDatePipe],
  template: `
    @if (outlook(); as w) {
      @if (w.days.length) {
        <section class="card weather" aria-labelledby="weather-title">
          <div class="section-head">
            <h2 id="weather-title"><app-icon name="sun" /> {{ 'weather.title' | t }}</h2>
            <span class="small muted">{{ (w.kind === 'forecast' ? 'weather.forecast' : 'weather.lastYear') | t }}</span>
          </div>
          <ol class="weather-days">
            @for (d of w.days; track d.date) {
              <li class="weather-day">
                <span class="small muted">{{ d.date | ldate: 'EEE d MMM' }}</span>
                <app-icon [name]="d.icon" [size]="26" />
                <span class="temp"><strong>{{ d.maxC }}°</strong> <span class="muted">{{ d.minC }}°</span></span>
                @if (d.precipitation !== null) {
                  <span class="small muted">{{ (w.kind === 'forecast' ? 'weather.rainChance' : 'weather.rainMm') | t: { value: d.precipitation } }}</span>
                }
              </li>
            }
          </ol>
          <p class="small muted source-note">{{ 'weather.source' | t }}
            <a href="https://open-meteo.com/" target="_blank" rel="noopener noreferrer">Open-Meteo</a></p>
        </section>
      }
    }
  `,
})
export class WeatherComponent {
  readonly latitude = input.required<number>();
  readonly longitude = input.required<number>();
  readonly start = input.required<string>();
  readonly end = input.required<string>();

  private readonly weather = inject(WeatherService);
  private readonly destroyRef = inject(DestroyRef);
  protected readonly outlook = signal<WeatherOutlook | null>(null);

  constructor() {
    effect(() => {
      const [lat, lon, start, end] = [this.latitude(), this.longitude(), this.start(), this.end()];
      untracked(() =>
        this.weather
          .outlook(lat, lon, start.slice(0, 10), end.slice(0, 10), toIsoDate(new Date()))
          .pipe(takeUntilDestroyed(this.destroyRef))
          // Weather is a nice-to-have: on failure the section simply stays hidden.
          .subscribe({ next: (w) => this.outlook.set(w), error: () => this.outlook.set(null) }),
      );
    });
  }
}
