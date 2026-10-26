import { ChangeDetectionStrategy, Component, DestroyRef, computed, effect, inject, input, signal, untracked } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { ApiService } from '../../core/api.service';
import { TranslationKey } from '../../core/i18n/en';
import { I18nService } from '../../core/i18n/i18n.service';
import { TranslatePipe } from '../../core/i18n/translate.pipe';
import { AttractionDto, AttractionKind, TransitMode } from '../../core/models';
import { IconComponent, IconName } from '../../shared/icon.component';
import { DistancePipe } from '../../shared/pipes';

const KINDS: AttractionKind[] = ['museum', 'castle', 'worship', 'park', 'monument', 'heritage', 'viewpoint', 'sight'];

/**
 * The city's best-known sights (ranked by how many Wikipedia editions describe them) with a short description,
 * opening hours when known, and the metro/bus stops next to each, plus public-transport directions.
 */
@Component({
  selector: 'app-attractions',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [TranslatePipe, IconComponent, DistancePipe],
  template: `
    @if (attractions().length) {
      <section class="card attractions" id="sights" aria-labelledby="sights-title">
        <div class="section-head">
          <h2 id="sights-title"><app-icon name="compass" /> {{ 'sights.title' | t: { city: city() } }}</h2>
        </div>
        <p class="small muted">{{ 'sights.intro' | t }}</p>

        <div class="chip-filter" role="group" [attr.aria-label]="'sights.filter' | t">
          <button type="button" class="filter-chip" [class.active]="kind() === null" [attr.aria-pressed]="kind() === null" (click)="kind.set(null)">
            {{ 'sights.all' | t }} ({{ attractions().length }})
          </button>
          @for (k of kinds(); track k.kind) {
            <button type="button" class="filter-chip" [class.active]="kind() === k.kind" [attr.aria-pressed]="kind() === k.kind" (click)="kind.set(k.kind)">
              {{ kindKey(k.kind) | t }} ({{ k.count }})
            </button>
          }
        </div>

        <ul class="sight-grid">
          @for (a of visible(); track a.id; let i = $index) {
            <li class="sight-card">
              <div class="sight-media">
                @if (a.imageUrl) {
                  <img [src]="a.imageUrl" alt="" loading="lazy" decoding="async" referrerpolicy="no-referrer" />
                } @else {
                  <span class="sight-placeholder"><app-icon [name]="kindIcon(a.kind)" [size]="32" /></span>
                }
                <span class="sight-rank" aria-hidden="true">{{ i + 1 }}</span>
              </div>
              <div class="sight-body">
                <p class="sight-kind small">{{ kindKey(a.kind) | t }} · {{ 'sights.fromCentre' | t: { distance: (a.distanceFromCentreMeters | distance) } }}</p>
                <h3>{{ a.name }}</h3>
                @if (a.summary) {
                  <p class="sight-summary small" [attr.lang]="a.summaryLanguage">{{ a.summary }}</p>
                } @else if (a.shortDescription) {
                  <p class="sight-summary small">{{ a.shortDescription }}</p>
                }
                @if (a.openingHours) {
                  <p class="small sight-hours"><app-icon name="clock" [size]="14" /> <span lang="en">{{ a.openingHours }}</span></p>
                }
                @if (a.stops.length) {
                  <ul class="sight-stops small" [attr.aria-label]="'sights.getThere' | t">
                    @for (s of a.stops; track s.name) {
                      <li>
                        <app-icon [name]="modeIcon(s.mode)" [size]="14" />
                        <span><strong>{{ s.name }}</strong> · {{ modeKey(s.mode) | t }} · {{ s.distanceMeters | distance }}</span>
                        @if (s.lines.length) {
                          <span class="stop-lines">
                            @for (ln of s.lines.slice(0, 6); track ln) { <span class="line-badge line-badge-sm" [attr.data-mode]="s.mode">{{ ln }}</span> }
                          </span>
                        }
                      </li>
                    }
                  </ul>
                }
                <div class="hotel-links small sight-links">
                  @if (a.directionsUrl) {
                    <a [href]="a.directionsUrl" target="_blank" rel="noopener noreferrer nofollow"><app-icon name="train" [size]="14" /> {{ 'sights.route' | t }}</a>
                  }
                  @if (a.mapUrl) {
                    <a [href]="a.mapUrl" target="_blank" rel="noopener noreferrer nofollow"><app-icon name="pin" [size]="14" /> {{ 'nearby.map' | t }}</a>
                  }
                  @if (a.wikipediaUrl) {
                    <a [href]="a.wikipediaUrl" target="_blank" rel="noopener noreferrer"><app-icon name="info" [size]="14" /> Wikipedia</a>
                  }
                  @if (a.website) {
                    <a [href]="a.website" target="_blank" rel="noopener noreferrer nofollow"><app-icon name="globe" [size]="14" /> {{ 'nearby.website' | t }}</a>
                  }
                </div>
              </div>
            </li>
          }
        </ul>
        @if (filtered().length > pageSize) {
          <button type="button" class="btn btn-ghost btn-sm" (click)="showAll.set(!showAll())" [attr.aria-expanded]="showAll()">
            {{ (showAll() ? 'nearby.showFewer' : 'nearby.showAll') | t: { count: filtered().length } }}
          </button>
        }
        <p class="small muted source-note">
          {{ 'sights.source' | t }}
          <a href="https://www.wikipedia.org/" target="_blank" rel="noopener noreferrer">Wikipedia</a> (CC BY-SA),
          <a href="https://www.openstreetmap.org/copyright" target="_blank" rel="noopener noreferrer">© OpenStreetMap</a>, Geoapify.
        </p>
      </section>
    } @else if (loading()) {
      <div class="card skeleton" aria-busy="true"></div>
    }
  `,
})
export class AttractionsComponent {
  readonly destinationId = input.required<string>();
  readonly city = input.required<string>();

  private readonly api = inject(ApiService);
  private readonly destroyRef = inject(DestroyRef);
  private readonly i18n = inject(I18nService);

  protected readonly pageSize = 6;
  protected readonly attractions = signal<AttractionDto[]>([]);
  protected readonly loading = signal(false);
  protected readonly kind = signal<AttractionKind | null>(null);
  protected readonly showAll = signal(false);

  protected readonly kinds = computed(() =>
    KINDS.map((kind) => ({ kind, count: this.attractions().filter((a) => a.kind === kind).length })).filter((k) => k.count > 0),
  );
  protected readonly filtered = computed(() => {
    const k = this.kind();
    return k ? this.attractions().filter((a) => a.kind === k) : this.attractions();
  });
  protected readonly visible = computed(() => (this.showAll() ? this.filtered() : this.filtered().slice(0, this.pageSize)));

  constructor() {
    // Names and descriptions are localized by the API: reload on language change.
    effect(() => {
      const id = this.destinationId();
      this.i18n.lang();
      untracked(() => {
        this.loading.set(true);
        this.api
          .getAttractions(id)
          .pipe(takeUntilDestroyed(this.destroyRef))
          .subscribe({
            next: (a) => {
              this.attractions.set(a);
              this.loading.set(false);
            },
            // Sights are extra information: on failure the section simply stays hidden.
            error: () => this.loading.set(false),
          });
      });
    });
  }

  protected kindKey(kind: AttractionKind): TranslationKey {
    return `sightKind.${kind}`;
  }

  protected kindIcon(kind: AttractionKind): IconName {
    return kind === 'museum' ? 'info' : kind === 'park' ? 'sun' : 'compass';
  }

  protected modeKey(mode: TransitMode): TranslationKey {
    return `transit.${mode}` as TranslationKey;
  }

  protected modeIcon(mode: TransitMode): IconName {
    return mode === 'bus' || mode === 'trolleybus' ? 'bus' : 'train';
  }
}
