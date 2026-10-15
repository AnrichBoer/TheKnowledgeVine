import { Component, DestroyRef, inject, signal } from '@angular/core';
import { RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { PortalAuth, portalError } from './portal-auth';
import { PortalReloadSafety } from './portal-reload-safety';

@Component({ selector: 'app-portal-admin', imports: [RouterLink, RouterLinkActive, RouterOutlet], templateUrl: './portal-admin.html', styleUrl: './portal-admin.scss' })
export class PortalAdmin {
  readonly auth = inject(PortalAuth);
  readonly error = signal(''); readonly busy = signal(false);
  constructor() { inject(PortalReloadSafety).protect(() => this.busy(), inject(DestroyRef)); }
  async logout() {
    if (this.busy()) return;
    this.busy.set(true); this.error.set('');
    try { await this.auth.logout(); } catch (error) { this.error.set(portalError(error)); }
    finally { this.busy.set(false); }
  }
}
