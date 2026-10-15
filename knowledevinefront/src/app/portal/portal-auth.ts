import { DOCUMENT } from '@angular/common';
import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { DestroyRef, inject, Injectable, signal } from '@angular/core';
import { Router } from '@angular/router';
import { firstValueFrom, Subject, takeUntil } from 'rxjs';
import { PortalNoteMemory } from './portal-note-memory';

export type PortalRole = 'Admin' | 'Teacher' | 'Parent';
export interface PortalSession {
  id: string; name: string; email: string; roles: PortalRole[]; expiresAt: string; serverTime: string;
}

@Injectable({ providedIn: 'root' })
export class PortalAuth {
  private readonly http = inject(HttpClient);
  private readonly document = inject(DOCUMENT);
  private readonly router = inject(Router);
  private readonly notes = inject(PortalNoteMemory);
  private readonly destroy = inject(DestroyRef);
  readonly session = signal<PortalSession | null>(null);
  readonly expired = signal(false);
  private pending?: Promise<PortalSession | null>;
  private expiryTimer?: ReturnType<typeof setTimeout>;
  private activityTimer?: ReturnType<typeof setTimeout>;
  private renewing = false;
  private lastRenewal = 0;
  private deadline = 0;
  private changingSession = false;
  private readonly cancelRenewal = new Subject<void>();
  private readonly base = '/api/portal/auth';

  constructor() {
    const observe = (event: Event) => {
      if (!this.changingSession && event.isTrusted && this.document.visibilityState === 'visible' && this.session()) this.noteActivity();
    };
    for (const event of ['pointerdown', 'keydown', 'input', 'wheel'])
      this.document.addEventListener(event, observe, { passive: true });
    this.destroy.onDestroy(() => {
      for (const event of ['pointerdown', 'keydown', 'input', 'wheel']) this.document.removeEventListener(event, observe);
      clearTimeout(this.expiryTimer); clearTimeout(this.activityTimer);
      this.cancelRenewal.next(); this.cancelRenewal.complete();
    });
  }

  async loadSession(): Promise<PortalSession | null> {
    if (this.pending) return this.pending;
    this.pending = firstValueFrom(this.http.get<PortalSession>(`${this.base}/session`))
      .then(session => { this.accept(session); return session; })
      .catch((error: HttpErrorResponse) => {
        if (error.status === 401) { this.clear(); return null; }
        throw error;
      }).finally(() => { this.pending = undefined; });
    return this.pending;
  }

  async login(email: string, password: string): Promise<PortalSession> {
    await this.csrf();
    const session = await firstValueFrom(this.http.post<PortalSession>(`${this.base}/login`, { email, password }));
    this.accept(session);
    this.expired.set(false);
    await this.csrf(); // The request token is bound to the newly signed-in identity.
    return session;
  }

  async logout(): Promise<void> {
    this.changingSession = true;
    clearTimeout(this.activityTimer); this.cancelRenewal.next();
    try {
      await this.csrf();
      await firstValueFrom(this.http.post(`${this.base}/logout`, {}));
    } catch (error) {
      if (!(error instanceof HttpErrorResponse) || error.status !== 401) { this.changingSession = false; throw error; }
    }
    this.clear(); this.expired.set(false);
    this.notes.clear();
    this.changingSession = false;
    await this.router.navigateByUrl('/portal/login');
  }

  async recover(email: string): Promise<string> {
    await this.csrf();
    const response = await firstValueFrom(this.http.post<{ message: string }>(`${this.base}/forgot-password`, { email }));
    return response.message;
  }

  async setPassword(userId: string, token: string, password: string, initial: boolean): Promise<void> {
    await this.csrf();
    await firstValueFrom(this.http.post(`${this.base}/${initial ? 'setup-password' : 'reset-password'}`, { userId, token, password }));
    this.clear();
    this.notes.clear();
  }

  async access(role: PortalRole): Promise<void> {
    await firstValueFrom(this.http.get(`/api/portal/access/${role.toLowerCase()}`));
  }

  destination(session = this.session()): string {
    const role = (['Admin', 'Teacher', 'Parent'] as PortalRole[]).find(role => session?.roles.includes(role));
    return role ? `/portal/${role.toLowerCase()}` : '/portal/login';
  }

  private csrf() { return firstValueFrom(this.http.get(`${this.base}/csrf`)); }
  async prepareMutation(): Promise<void> { await this.csrf(); }
  requireSignIn() { this.expire(); }

  private accept(session: PortalSession) {
    this.session.set(session);
    this.lastRenewal = Date.now();
    this.deadline = Date.now() + Math.max(0, Date.parse(session.expiresAt) - Date.parse(session.serverTime));
    clearTimeout(this.expiryTimer);
    this.expiryTimer = setTimeout(() => this.expire(), Math.max(0, this.deadline - Date.now()));
  }

  private clear() {
    this.session.set(null);
    this.cancelRenewal.next();
    clearTimeout(this.expiryTimer); clearTimeout(this.activityTimer);
  }

  private expire() {
    this.clear(); this.expired.set(true);
    if (this.router.url.startsWith('/portal/') && !this.router.url.startsWith('/portal/login'))
      void this.router.navigateByUrl('/portal/login');
  }

  private noteActivity() {
    clearTimeout(this.activityTimer);
    const remaining = this.deadline - Date.now();
    if (remaining <= 0) { this.expire(); return; }
    // Coalesce bursts, but renew near expiry immediately. No unattended polling/keepalive.
    const wait = remaining < 5000 ? 0 : Math.max(0, 2000 - (Date.now() - this.lastRenewal));
    this.activityTimer = setTimeout(() => void this.renew(), wait);
  }

  private async renew() {
    if (this.changingSession || this.renewing || !this.session() || this.document.visibilityState !== 'visible') return;
    this.renewing = true;
    try {
      const session = await firstValueFrom(this.http.post<PortalSession>(`${this.base}/activity`, {}).pipe(takeUntil(this.cancelRenewal)));
      this.accept(session);
    } catch (error) {
      if (error instanceof HttpErrorResponse && error.status === 401) this.expire();
      // Keep the existing expiry when a request fails; connectivity cannot extend a session.
    } finally { this.renewing = false; }
  }
}

export function portalError(error: unknown): string {
  if (error instanceof HttpErrorResponse) {
    if (error.status === 0) return 'We could not reach the school portal. Check your connection and try again.';
    if (error.status === 429) return 'Too many attempts. Please wait a few minutes and try again.';
    if (error.error?.message) return error.error.message;
    if (error.status === 400) return 'Please check your details and try again.';
  }
  return 'Something went wrong. Please try again.';
}
