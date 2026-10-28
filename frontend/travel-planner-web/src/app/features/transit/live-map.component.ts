import {
  AfterViewInit, ChangeDetectionStrategy, Component, DestroyRef, ElementRef, NgZone, computed, effect, inject, input, signal, untracked, viewChild,
} from '@angular/core';
import * as L from 'leaflet';
import { Subscription, catchError, of } from 'rxjs';
import { ApiService } from '../../core/api.service';
import { DevicePosition, GeolocationService } from '../../core/geolocation.service';
import { TranslationKey } from '../../core/i18n/en';
import { I18nService } from '../../core/i18n/i18n.service';
import { TranslatePipe } from '../../core/i18n/translate.pipe';
import { PlaceSuggestionDto, TransitItineraryDto, TransitLegDto, VehicleDto } from '../../core/models';
import { IconComponent } from '../../shared/icon.component';

export type ModeGroup = 'bus' | 'tram' | 'metro' | 'rail' | 'ferry';

export function modeGroup(mode: string): ModeGroup {
  switch (mode.toUpperCase()) {
    case 'TRAM': case 'CABLE_CAR': case 'FUNICULAR': return 'tram';
    case 'SUBWAY': case 'METRO': return 'metro';
    case 'FERRY': return 'ferry';
    case 'BUS': case 'COACH': case 'TROLLEYBUS': case 'ODM': return 'bus';
    default: return 'rail';
  }
}

/** Google encoded polyline → [lat, lon] points (precision 5 for vehicles, 7 for journey legs). */
export function decodePolyline(s: string, precision = 5): [number, number][] {
  const factor = 10 ** precision;
  const out: [number, number][] = [];
  let i = 0, lat = 0, lon = 0;
  while (i < s.length) {
    for (let k = 0; k < 2; k++) {
      let result = 0, shift = 0, b: number;
      do {
        b = s.charCodeAt(i++) - 63;
        result |= (b & 31) << shift;
        shift += 5;
      } while (b >= 32 && i < s.length);
      const d = result & 1 ? ~(result >> 1) : result >> 1;
      if (k === 0) lat += d; else lon += d;
    }
    out.push([lat / factor, lon / factor]);
  }
  return out;
}

/** The point at a fraction (0–1) of the way along a line, by distance. */
export function pointAlong(points: [number, number][], fraction: number): [number, number] {
  if (points.length === 1) return points[0];
  const lengths: number[] = [];
  let total = 0;
  for (let i = 1; i < points.length; i++) {
    const dy = points[i][0] - points[i - 1][0];
    const dx = (points[i][1] - points[i - 1][1]) * Math.cos((points[i][0] * Math.PI) / 180);
    const len = Math.hypot(dx, dy);
    lengths.push(len);
    total += len;
  }
  let target = Math.min(Math.max(fraction, 0), 1) * total;
  for (let i = 0; i < lengths.length; i++) {
    if (target <= lengths[i] || i === lengths.length - 1) {
      const f = lengths[i] ? Math.min(target / lengths[i], 1) : 0;
      return [points[i][0] + (points[i + 1][0] - points[i][0]) * f, points[i][1] + (points[i + 1][1] - points[i][1]) * f];
    }
    target -= lengths[i];
  }
  return points[points.length - 1];
}

interface Running {
  vehicle: VehicleDto;
  shapes: [number, number][][];
  marker?: L.Marker;
}

/** Where a vehicle is at Unix time t (seconds): moving along a stretch, waiting at its next stop, or not running. */
export function positionAt(v: VehicleDto, shapes: [number, number][][], t: number): { at: [number, number]; next: string } | null {
  const s = v.stretches;
  for (let i = 0; i < s.length; i++) {
    if (t < s[i].departure) {
      // Between two stretches (or just before the first) the vehicle waits at the stop.
      return i > 0 || s[i].departure - t < 90 ? { at: shapes[i][0], next: s[i].to } : null;
    }
    if (t <= s[i].arrival) {
      const span = s[i].arrival - s[i].departure;
      return { at: pointAlong(shapes[i], span > 0 ? (t - s[i].departure) / span : 1), next: s[i].to };
    }
  }
  return null;
}

const REFRESH_MS = 30_000;
const MIN_ZOOM = 11;
const MODES: ModeGroup[] = ['bus', 'tram', 'metro', 'rail', 'ferry'];

/**
 * Map with the trip's start and destination and every public-transport vehicle in view moving in (near) real time.
 * Vehicles come from operators' live feeds where published (solid) or the timetable (dashed outline).
 */
@Component({
  selector: 'app-live-map',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [TranslatePipe, IconComponent],
  template: `
    @if (vehicles()) {
    <div class="live-map-tools">
      <div class="chips" role="group" [attr.aria-label]="'transit.filter' | t">
        @for (m of modes; track m) {
          <button type="button" class="chip-toggle mode-chip" [attr.data-mode]="m" [class.active]="shown().has(m)" [attr.aria-pressed]="shown().has(m)"
                  (click)="toggleMode(m)">
            <span class="mode-dot" [attr.data-mode]="m"></span> {{ modeKey(m) | t }}
            @if (counts()[m]) { <span class="chip-count">{{ counts()[m] }}</span> }
          </button>
        }
        @if (lines().length) {
          <button type="button" class="chip-toggle" [class.active]="onlyMine()" [attr.aria-pressed]="onlyMine()" (click)="onlyMine.set(!onlyMine())">
            <app-icon name="sparkles" [size]="14" /> {{ 'transit.onlyMyLines' | t }}
          </button>
        }
      </div>
    </div>
    }
    <div class="live-map-wrap" [class.is-static]="!vehicles()">
      <div #map class="live-map" role="application" [attr.aria-label]="'transit.mapLabel' | t"></div>
      @if (vehicles()) {
      <div class="live-status" aria-live="polite">
        @switch (status()) {
          @case ('loading') { <span class="spinner" aria-hidden="true"></span> {{ 'transit.vehiclesLoading' | t }} }
          @case ('zoom') { <app-icon name="search" [size]="14" /> {{ 'transit.zoomIn' | t }} }
          @case ('none') { {{ 'transit.noVehicles' | t }} }
          @case ('error') { {{ 'transit.vehiclesError' | t }} }
          @case ('ok') {
            <span class="live-pulse" aria-hidden="true"></span>
            {{ 'transit.vehiclesCount' | t: { count: visible() } }}
            @if (live()) { · {{ 'transit.liveShare' | t: { count: live() } }} }
          }
        }
      </div>
      }
      <button type="button" class="live-recenter" (click)="fit()" [title]="'transit.recenter' | t"><app-icon name="pin" [size]="18" /></button>
      @if (geo.supported) {
        @let trackLabel = (tracking() ? (following() ? 'transit.trackFollow' : 'transit.trackResume') : 'transit.trackMe') | t;
        <button type="button" class="live-track" [class.is-on]="tracking()" [class.is-following]="tracking() && following()" (click)="toggleTracking()"
                [attr.aria-pressed]="tracking()" [title]="trackLabel">
          <app-icon name="compass" [size]="18" /> <span class="live-track-text">{{ trackLabel }}</span>
        </button>
      }
    </div>
    <ul class="live-legend small">
      @if (vehicles()) {
        <li><span class="legend-vehicle is-live"></span> {{ 'transit.legendLive' | t }}</li>
        <li><span class="legend-vehicle"></span> {{ 'transit.legendScheduled' | t }}</li>
      }
      @if (route()) {
        <li><span class="legend-walk"></span> {{ 'transit.legendWalk' | t }}</li>
        <li><span class="stop-pin is-board legend-pin">№</span> {{ 'transit.legendBoard' | t }}</li>
      }
      <li><span class="legend-end" data-end="from"></span> {{ 'transit.from' | t }}</li>
      <li><span class="legend-end" data-end="to"></span> {{ 'transit.to' | t }}</li>
    </ul>
  `,
})
export class LiveMapComponent implements AfterViewInit {
  readonly from = input<PlaceSuggestionDto | null>(null);
  readonly to = input<PlaceSuggestionDto | null>(null);
  /** Lines of the suggested journeys ("397", "M1"): highlighted, and optionally the only ones shown. */
  readonly lines = input<string[]>([]);
  /** The chosen journey: drawn step by step (walks dotted, rides in the line's colour) with where to board and get off. */
  readonly route = input<TransitItineraryDto | null>(null);
  /** False: a plain route map (the day plan), without the moving vehicles. */
  readonly vehicles = input(true);
  /** Start with the visitor's moving position shown and followed (they chose "from my location"). */
  readonly track = input(false);

  protected readonly geo = inject(GeolocationService);
  protected readonly tracking = signal(false);
  /** The map keeps the visitor in view until they move it themselves. */
  protected readonly following = signal(true);
  private meLayer = L.layerGroup();
  private meMarker?: L.Marker;
  private meCircle?: L.Circle;

  private readonly api = inject(ApiService);
  private readonly zone = inject(NgZone);
  private readonly i18n = inject(I18nService);
  private readonly mapEl = viewChild.required<ElementRef<HTMLDivElement>>('map');

  protected readonly modes = MODES;
  protected readonly shown = signal<ReadonlySet<ModeGroup>>(new Set(MODES));
  protected readonly onlyMine = signal(false);
  protected readonly status = signal<'loading' | 'zoom' | 'none' | 'error' | 'ok'>('loading');
  protected readonly visible = signal(0);
  protected readonly live = signal(0);
  protected readonly counts = signal<Partial<Record<ModeGroup, number>>>({});

  private map?: L.Map;
  private ends = L.layerGroup();
  private routeLayer = L.layerGroup();
  private routeBounds: L.LatLngBounds | null = null;
  private vehiclesLayer = L.layerGroup();
  private running = new Map<string, Running>();
  /** Server clock minus device clock, in seconds (phones are often a little off). */
  private skew = 0;
  private sub?: Subscription;
  private refreshTimer?: ReturnType<typeof setInterval>;
  private tickTimer?: ReturnType<typeof setInterval>;
  private moveTimer?: ReturnType<typeof setTimeout>;
  /** The chosen route's lines, or every suggested journey's lines when none is chosen. */
  private readonly mineSet = computed(() => {
    const route = this.route();
    const lines = route ? route.legs.filter((l) => l.mode !== 'WALK' && l.line).map((l) => l.line!) : this.lines();
    return new Set(lines.map((l) => l.toLowerCase()));
  });

  constructor() {
    effect(() => {
      const from = this.from();
      const to = this.to();
      untracked(() => this.drawEnds(from, to));
    });
    effect(() => {
      const route = this.route();
      untracked(() => {
        // With a route on the map, its own lines are what matters; the filter brings the rest back.
        this.onlyMine.set(!!route);
        this.drawRoute(route);
      });
    });
    effect(() => {
      this.shown();
      this.onlyMine();
      this.mineSet();
      untracked(() => this.tick());
    });
    effect(() => {
      if (this.track()) untracked(() => this.startTracking());
    });
    effect(() => {
      const p = this.geo.position();
      const on = this.tracking();
      untracked(() => this.drawMe(on ? p : null));
    });
    inject(DestroyRef).onDestroy(() => {
      if (this.tracking()) this.geo.release();
      clearInterval(this.refreshTimer);
      clearInterval(this.tickTimer);
      clearTimeout(this.moveTimer);
      this.sub?.unsubscribe();
      this.map?.remove();
    });
  }

  ngAfterViewInit(): void {
    // Leaflet's own events and the 1-second animation need no change detection.
    this.zone.runOutsideAngular(() => {
      const map = L.map(this.mapEl().nativeElement, { zoomControl: true, attributionControl: true, tap: false } as L.MapOptions);
      L.tileLayer('https://tile.openstreetmap.org/{z}/{x}/{y}.png', {
        maxZoom: 19,
        attribution: '&copy; <a href="https://www.openstreetmap.org/copyright">OpenStreetMap</a> · <a href="https://transitous.org">Transitous</a>',
      }).addTo(map);
      this.vehiclesLayer.addTo(map);
      this.routeLayer.addTo(map);
      this.ends.addTo(map);
      map.setView([41.7151, 44.8271], 13);
      // Far out, vehicles are dots (labels would pile up); close in, they show their line.
      const far = () => map.getContainer().classList.toggle('is-far', map.getZoom() < 14);
      map.on('zoomend', far);
      far();
      this.map = map;
      this.meLayer.addTo(map);
      this.drawMe(this.tracking() ? this.geo.position() : null);
      // Moving the map by hand stops following; the button brings it back.
      map.on('dragstart', () => this.set(() => this.following.set(false)));
      // The route may already be known when the map is created (it draws the start and destination too).
      this.drawRoute(this.route());
      if (this.vehicles()) {
        map.on('moveend', () => {
          clearTimeout(this.moveTimer);
          this.moveTimer = setTimeout(() => this.load(), 400);
        });
        this.refreshTimer = setInterval(() => this.load(), REFRESH_MS);
        this.tickTimer = setInterval(() => this.tick(), 1000);
      }
      // The container may get its final size after the first layout (tabs, fonts).
      setTimeout(() => map.invalidateSize(), 200);
    });
  }

  fit(): void {
    const map = this.map;
    if (!map) return;
    const pts = [this.from(), this.to()].filter((p): p is PlaceSuggestionDto => !!p).map((p) => L.latLng(p.latitude, p.longitude));
    const bounds = this.routeBounds ? L.latLngBounds(pts).extend(this.routeBounds) : L.latLngBounds(pts);
    if (pts.length === 2 || this.routeBounds) map.fitBounds(bounds, { padding: [40, 40], maxZoom: 16 });
    else if (pts.length === 1) map.setView(pts[0], 15);
  }

  protected toggleTracking(): void {
    if (!this.tracking()) {
      this.startTracking();
    } else if (!this.following()) {
      this.following.set(true);
      const p = this.geo.position();
      if (p && this.map) this.map.setView([p.latitude, p.longitude], Math.max(this.map.getZoom(), 16));
    } else {
      this.tracking.set(false);
      this.geo.release();
    }
  }

  private startTracking(): void {
    if (this.tracking()) return;
    this.tracking.set(true);
    this.following.set(true);
    this.geo.acquire();
  }

  /** The blue dot (with the GPS accuracy circle and the direction of travel) where the visitor is now. */
  private drawMe(p: DevicePosition | null): void {
    if (!this.map) return;
    if (!p) {
      this.meLayer.clearLayers();
      this.meMarker = this.meCircle = undefined;
      return;
    }
    const at = L.latLng(p.latitude, p.longitude);
    const heading = p.heading != null ? `<span class="me-heading" style="transform:rotate(${Math.round(p.heading)}deg)"></span>` : '';
    const icon = L.divIcon({ className: 'me-marker', html: `<span class="me-dot">${heading}</span>`, iconSize: [22, 22], iconAnchor: [11, 11] });
    if (!this.meMarker) {
      this.meCircle = L.circle(at, { radius: p.accuracy, color: '#2563eb', weight: 1, opacity: 0.4, fillColor: '#3b82f6', fillOpacity: 0.12, interactive: false })
        .addTo(this.meLayer);
      this.meMarker = L.marker(at, { icon, zIndexOffset: 2000, keyboard: false })
        .bindTooltip(escapeHtml(this.i18n.t('transit.youAreHere')))
        .addTo(this.meLayer);
      if (this.following()) this.map.setView(at, Math.max(this.map.getZoom(), 16));
      return;
    }
    this.meMarker.setLatLng(at).setIcon(icon);
    this.meCircle?.setLatLng(at).setRadius(p.accuracy);
    if (this.following()) this.map.panTo(at, { animate: true });
  }

  private drawRoute(route: TransitItineraryDto | null): void {
    if (!this.map) return;
    this.routeLayer.clearLayers();
    this.routeBounds = null;
    if (!route) {
      this.drawEnds(this.from(), this.to());
      return;
    }
    const all: L.LatLng[] = [];
    for (const leg of route.legs) {
      const p = leg.path;
      if (!p) continue;
      const points = p.shape ? decodePolyline(p.shape, p.precision) : [[p.fromLatitude, p.fromLongitude], [p.toLatitude, p.toLongitude]] as [number, number][];
      points.forEach((pt) => all.push(L.latLng(pt[0], pt[1])));
      if (leg.mode === 'WALK') {
        L.polyline(points, { color: '#334155', weight: 4, opacity: 0.85, dashArray: '1 8', lineCap: 'round' }).addTo(this.routeLayer);
        continue;
      }
      const color = lineColor(leg);
      L.polyline(points, { color: '#fff', weight: 9, opacity: 0.9 }).addTo(this.routeLayer);
      L.polyline(points, { color, weight: 5, opacity: 0.95 }).addTo(this.routeLayer);
      const name = escapeHtml(`${this.modeName(leg.mode)} ${leg.line ?? ''}`.trim());
      this.stopPin(p.fromLatitude, p.fromLongitude, color, leg.line ?? '•',
        `<strong>${escapeHtml(this.i18n.t('transit.boardAt', { time: leg.departureTime }))}</strong> · ${name}<br>${escapeHtml(leg.from)}`, true);
      this.stopPin(p.toLatitude, p.toLongitude, color, '⏏',
        `<strong>${escapeHtml(this.i18n.t('transit.getOffAt', { time: leg.arrivalTime }))}</strong><br>${escapeHtml(leg.to)}`, false);
    }
    this.routeBounds = all.length ? L.latLngBounds(all) : null;
    // Redraws the start and destination (without the straight placeholder line) and fits the map to the route.
    this.drawEnds(this.from(), this.to());
  }

  /** A stop of the route: boarding stops carry the line number and keep their label open. */
  private stopPin(lat: number, lon: number, color: string, label: string, html: string, board: boolean): void {
    L.marker([lat, lon], {
      icon: L.divIcon({
        className: 'stop-pin-marker',
        html: `<span class="stop-pin${board ? ' is-board' : ''}" style="--line:${color}">${escapeHtml(label.slice(0, 5))}</span>`,
        iconSize: [34, 24],
        iconAnchor: [17, 12],
      }),
      zIndexOffset: board ? 900 : 800,
      keyboard: false,
    })
      .bindTooltip(html, { permanent: board, direction: 'top', offset: [0, -12], className: 'stop-tip' })
      .addTo(this.routeLayer);
  }

  private modeName(mode: string): string {
    return this.i18n.t(this.modeKey(modeGroup(mode)));
  }

  private drawEnds(from: PlaceSuggestionDto | null, to: PlaceSuggestionDto | null): void {
    if (!this.map) return;
    this.ends.clearLayers();
    for (const [p, end] of [[from, 'from'], [to, 'to']] as const) {
      if (!p) continue;
      L.marker([p.latitude, p.longitude], {
        icon: L.divIcon({ className: 'end-marker', html: `<span data-end="${end}"></span>`, iconSize: [26, 26], iconAnchor: [13, 13] }),
        title: p.name,
        zIndexOffset: 1000,
        keyboard: false,
      }).bindTooltip(escapeHtml(p.name)).addTo(this.ends);
    }
    if (from && to && !this.route()) {
      L.polyline([[from.latitude, from.longitude], [to.latitude, to.longitude]], { color: '#0f766e', weight: 2, dashArray: '6 8', opacity: 0.6 })
        .addTo(this.ends);
    }
    this.fit();
  }

  private load(): void {
    const map = this.map;
    if (!map) return;
    if (map.getZoom() < MIN_ZOOM) {
      this.set(() => this.status.set('zoom'));
      this.clearVehicles();
      return;
    }
    const b = map.getBounds().pad(0.1);
    this.sub?.unsubscribe();
    if (!this.running.size) this.set(() => this.status.set('loading'));
    this.sub = this.api
      .getVehicles(b.getSouth(), b.getWest(), b.getNorth(), b.getEast())
      .pipe(catchError(() => of(null)))
      .subscribe((r) => {
        if (!r) {
          this.set(() => this.status.set('error'));
          return;
        }
        if (r.tooLarge) {
          this.clearVehicles();
          this.set(() => this.status.set('zoom'));
          return;
        }
        this.skew = r.serverTime - Date.now() / 1000;
        const seen = new Set<string>();
        for (const v of r.vehicles) {
          seen.add(v.id);
          const known = this.running.get(v.id);
          const shapes = v.stretches.map((s) => decodePolyline(s.shape));
          if (known) {
            known.vehicle = v;
            known.shapes = shapes;
          } else {
            this.running.set(v.id, { vehicle: v, shapes });
          }
        }
        for (const [id, r2] of this.running) {
          if (!seen.has(id)) {
            r2.marker?.remove();
            this.running.delete(id);
          }
        }
        this.tick();
      });
  }

  private clearVehicles(): void {
    this.vehiclesLayer.clearLayers();
    this.running.clear();
    this.set(() => {
      this.visible.set(0);
      this.live.set(0);
      this.counts.set({});
    });
  }

  /** Moves every vehicle to where it is now. */
  private tick(): void {
    if (!this.map || this.status() === 'zoom' && !this.running.size) return;
    const t = Date.now() / 1000 + this.skew;
    const shown = this.shown();
    const mine = this.mineSet();
    const onlyMine = this.onlyMine();
    let visible = 0, live = 0;
    const counts: Partial<Record<ModeGroup, number>> = {};
    for (const r of this.running.values()) {
      const v = r.vehicle;
      const group = modeGroup(v.mode);
      const pos = positionAt(v, r.shapes, t);
      const isMine = !!v.line && mine.has(v.line.toLowerCase());
      if (pos) counts[group] = (counts[group] ?? 0) + 1;
      if (!pos || !shown.has(group) || (onlyMine && !isMine)) {
        if (r.marker) {
          r.marker.remove();
          r.marker = undefined;
        }
        continue;
      }
      visible++;
      if (v.realTime) live++;
      if (!r.marker) {
        r.marker = L.marker(pos.at, { icon: this.icon(v, group, isMine), keyboard: false, zIndexOffset: isMine ? 500 : 0 })
          .bindPopup(() => this.popup(r))
          .addTo(this.vehiclesLayer);
      } else {
        r.marker.setLatLng(pos.at);
      }
    }
    this.set(() => {
      this.visible.set(visible);
      this.live.set(live);
      this.counts.set(counts);
      if (this.status() !== 'zoom' && this.status() !== 'error') this.status.set(visible ? 'ok' : this.running.size ? 'ok' : 'none');
    });
  }

  private icon(v: VehicleDto, group: ModeGroup, mine: boolean): L.DivIcon {
    const label = escapeHtml((v.line ?? '').slice(0, 5));
    const color = v.color && /^[0-9a-f]{6}$/i.test(v.color) && v.color.toLowerCase() !== 'ffffff' && v.color.toLowerCase() !== 'fefefe' ? `#${v.color}` : '';
    const style = color ? ` style="--line:${color}"` : '';
    return L.divIcon({
      className: 'vehicle-marker',
      html: `<span class="vehicle${v.realTime ? ' is-live' : ''}${mine ? ' is-mine' : ''}" data-mode="${group}"${style}>${label || '•'}</span>`,
      iconSize: [30, 20],
      iconAnchor: [15, 10],
    });
  }

  private popup(r: Running): string {
    const v = r.vehicle;
    const t = Date.now() / 1000 + this.skew;
    const pos = positionAt(v, r.shapes, t);
    const mode = this.i18n.t(this.modeKey(modeGroup(v.mode)));
    const state = this.i18n.t(v.realTime ? 'transit.legendLive' : 'transit.legendScheduled');
    return `<strong>${escapeHtml(mode)} ${escapeHtml(v.line ?? '')}</strong><br>` +
      (pos ? `${escapeHtml(this.i18n.t('transit.nextStop'))}: ${escapeHtml(pos.next)}<br>` : '') +
      `<span class="muted">${escapeHtml(state)}</span>`;
  }

  protected toggleMode(m: ModeGroup): void {
    const next = new Set(this.shown());
    if (next.has(m)) next.delete(m); else next.add(m);
    this.shown.set(next);
  }

  protected modeKey(m: ModeGroup): TranslationKey {
    return `transit.mode.${m}`;
  }

  /** Signals changed from Leaflet callbacks (outside Angular) must re-enter the zone to refresh the view. */
  private set(fn: () => void): void {
    this.zone.run(fn);
  }
}

const MODE_COLORS: Record<ModeGroup, string> = { bus: '#16a34a', tram: '#dc2626', metro: '#2563eb', rail: '#7c3aed', ferry: '#0891b2' };

/** The operator's line colour when it is a usable one, otherwise the colour of the kind of transport. */
function lineColor(leg: TransitLegDto): string {
  const c = leg.color?.toLowerCase();
  return c && /^[0-9a-f]{6}$/.test(c) && c !== 'ffffff' && c !== 'fefefe' ? `#${c}` : MODE_COLORS[modeGroup(leg.mode)];
}

function escapeHtml(s: string): string {
  return s.replace(/[&<>"']/g, (c) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' })[c]!);
}
