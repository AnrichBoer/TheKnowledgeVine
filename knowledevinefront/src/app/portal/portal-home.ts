import { Component, inject, signal } from '@angular/core';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { PortalAuth, portalError, PortalRole } from './portal-auth';

@Component({ selector: 'app-portal-home', imports: [RouterLink], templateUrl: './portal-home.html', styleUrl: './portal-home.scss' })
export class PortalHome {
  readonly auth = inject(PortalAuth);
  readonly role = inject(ActivatedRoute).snapshot.data['role'] as PortalRole;
  readonly error = signal(''); readonly signingOut = signal(false);
  readonly roleLabel = this.role === 'Admin' ? 'Administrator' : this.role;
  async logout() {
    if (this.signingOut()) return;
    this.signingOut.set(true); this.error.set('');
    try { await this.auth.logout(); }
    catch (error) { this.error.set(portalError(error)); }
    finally { this.signingOut.set(false); }
  }
}
