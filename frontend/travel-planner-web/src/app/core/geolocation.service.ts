import { Injectable, signal } from '@angular/core';
import { TranslationKey } from './i18n/en';

export interface DevicePosition {
  latitude: number;
  longitude: number;
  /** Radius in metres within which the device is (GPS accuracy). */
  accuracy: number;
  /** Direction of travel in degrees from north, when moving. */
  heading: number | null;
}

/**
 * The device's position while the visitor allows it, updated as they move (browser geolocation; needs HTTPS).
 * One watch is shared by every component that asks; it stops when the last one releases it.
 */
@Injectable({ providedIn: 'root' })
export class GeolocationService {
  readonly position = signal<DevicePosition | null>(null);
  readonly error = signal<TranslationKey | null>(null);
  readonly watching = signal(false);

  private watchId: number | null = null;
  private users = 0;

  get supported(): boolean {
    return typeof navigator !== 'undefined' && !!navigator.geolocation;
  }

  /** Starts following the device (asks for permission the first time). Call release() when done. */
  acquire(): void {
    this.users++;
    if (this.watchId !== null) return;
    if (!this.supported) {
      this.error.set('transit.gpsUnavailable');
      return;
    }
    this.error.set(null);
    this.watching.set(true);
    this.watchId = navigator.geolocation.watchPosition(
      (p) => {
        this.error.set(null);
        this.position.set({
          latitude: p.coords.latitude,
          longitude: p.coords.longitude,
          accuracy: Math.round(p.coords.accuracy),
          heading: p.coords.heading != null && !Number.isNaN(p.coords.heading) && (p.coords.speed ?? 0) > 0.5 ? p.coords.heading : null,
        });
      },
      (e) => {
        this.error.set(e.code === e.PERMISSION_DENIED ? 'transit.gpsDenied' : 'transit.gpsUnavailable');
        if (e.code === e.PERMISSION_DENIED) this.stop();
      },
      { enableHighAccuracy: true, maximumAge: 5000, timeout: 20000 },
    );
  }

  release(): void {
    this.users = Math.max(0, this.users - 1);
    if (this.users === 0) this.stop();
  }

  private stop(): void {
    if (this.watchId !== null && this.supported) navigator.geolocation.clearWatch(this.watchId);
    this.watchId = null;
    this.watching.set(false);
  }
}
