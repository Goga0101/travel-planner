import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { RouterLink } from '@angular/router';
import { destinationPhoto } from '../../core/destination-photos';
import { I18nService } from '../../core/i18n/i18n.service';
import { TranslatePipe } from '../../core/i18n/translate.pipe';
import { SavedTripsService } from '../../core/local-lists';
import { IconComponent } from '../../shared/icon.component';
import { LocalDatePipe, MoneyPipe } from '../../shared/pipes';

/** Trips the visitor saved with the heart button. Stored only in this browser; prices are as of the time of saving. */
@Component({
  selector: 'app-saved-trips-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RouterLink, TranslatePipe, IconComponent, LocalDatePipe, MoneyPipe],
  template: `
    <div class="container page">
      <section class="page-head">
        <p class="eyebrow">{{ 'saved.eyebrow' | t }}</p>
        <h1 class="page-title">{{ 'saved.title' | t }}</h1>
        <p class="muted">{{ 'saved.note' | t }}</p>
      </section>

      @if (saved.trips().length) {
        <ul class="more-grid saved-grid">
          @for (t of saved.trips(); track t.id) {
            <li class="mini-card saved-card">
              <a [routerLink]="i18n.path('plans', t.id)" class="saved-link">
                @if (photo(t.code); as src) { <img [src]="src" alt="" loading="lazy" decoding="async" width="600" height="400" /> }
                <span class="mini-card-body">
                  <span class="mini-name">{{ t.city }}, {{ t.country }}</span>
                  <span class="small muted">{{ t.departureDate | ldate: 'd MMM' }} – {{ t.returnDate | ldate: 'd MMM y' }} · {{ 'unit.nights' | t: { count: t.nights } }}</span>
                  <span class="small">{{ 'saved.totalAtSave' | t: { amount: (t.total | money: t.currency) } }}</span>
                </span>
              </a>
              <button type="button" class="btn btn-ghost btn-sm saved-remove" (click)="saved.remove(t.id)" [attr.aria-label]="'saved.remove' | t: { city: t.city }">
                <app-icon name="x" [size]="16" /> {{ 'saved.removeShort' | t }}
              </button>
            </li>
          }
        </ul>
      } @else {
        <div class="card empty-state">
          <span class="icon-badge icon-badge-lg"><app-icon name="heart" [size]="28" /></span>
          <h2>{{ 'saved.emptyTitle' | t }}</h2>
          <p class="muted">{{ 'saved.emptyText' | t }}</p>
          <a class="btn btn-primary" [routerLink]="i18n.path()">{{ 'home.finalBtn' | t }} <app-icon name="arrowRight" [size]="16" /></a>
        </div>
      }
    </div>
  `,
})
export class SavedTripsPage {
  protected readonly saved = inject(SavedTripsService);
  protected readonly i18n = inject(I18nService);
  protected readonly photo = destinationPhoto;
}
