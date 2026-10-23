import { Injectable, computed, inject, signal } from '@angular/core';
import { Subscription } from 'rxjs';
import { ApiService } from './api.service';
import { ClientConfigDto } from './models';

/** Loads the non-secret client configuration and exposes it as signals. Reloaded when the language changes. */
@Injectable({ providedIn: 'root' })
export class ClientConfigService {
  private readonly api = inject(ApiService);
  private readonly state = signal<ClientConfigDto | null>(null);
  private request?: Subscription;

  readonly config = this.state.asReadonly();
  readonly isDemo = computed(() => this.state()?.dataMode === 'Demo');

  /** (Re)loads the configuration; the previous value stays visible until the new one arrives. */
  load(): void {
    this.request?.unsubscribe();
    this.request = this.api.getClientConfig().subscribe({ next: (c) => this.state.set(c), error: () => undefined });
  }
}
