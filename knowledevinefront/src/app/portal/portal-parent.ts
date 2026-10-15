import { DatePipe, DOCUMENT } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import { Component, DestroyRef, effect, inject, OnInit, PendingTasks, signal, untracked } from '@angular/core';
import { RouterLink } from '@angular/router';
import { PortalAuth } from './portal-auth';
import { ParentChild, ParentNote, ParentWindow, PortalParentApi } from './portal-parent-api';
import { PortalConnection } from './portal-connection';
import { PortalReloadSafety } from './portal-reload-safety';

@Component({ selector: 'app-portal-parent', imports: [DatePipe, RouterLink], templateUrl: './portal-parent.html' })
export class PortalParent implements OnInit {
  readonly auth = inject(PortalAuth);
  private readonly api = inject(PortalParentApi);
  private readonly document = inject(DOCUMENT);
  private readonly destroy = inject(DestroyRef);
  private readonly pending = inject(PendingTasks);
  private readonly connection = inject(PortalConnection);
  readonly children = signal<ParentChild[]>([]);
  readonly selected = signal<ParentChild | null>(null);
  readonly notes = signal<ParentNote[]>([]);
  readonly year = signal<number | null>(null);
  readonly nextBefore = signal<string | null>(null);
  readonly loading = signal(false);
  readonly loadingMore = signal(false);
  readonly signingOut = signal(false);
  readonly error = signal('');
  private serial = 0;
  private owner = this.auth.session()?.id;
  private preferred?: string;
  private disposed = false;
  private yearTimer?: ReturnType<typeof setTimeout>;

  constructor() {
    inject(PortalReloadSafety).protect(() => this.signingOut(), this.destroy);
    let disconnected = false;
    effect(() => {
      const down = this.connection.disconnected();
      untracked(() => {
        if (down) { disconnected = true; this.clear(); this.error.set('Connect to the portal to read your child’s updates.'); }
        else if (disconnected) { disconnected = false; if (this.auth.session() && !this.loading()) this.pending.run(() => this.refresh()); }
      });
    });
    effect(() => {
      const session = this.auth.session();
      const owner = session?.roles.includes('Parent') ? session.id : undefined;
      if (owner !== this.owner) untracked(() => {
        this.owner = owner; this.preferred = undefined; this.clear();
        if (owner) this.pending.run(() => this.refresh());
      });
    });
    const foreground = () => {
      if (this.document.visibilityState === 'visible' && this.auth.session() && !this.loading() && !this.signingOut()) this.pending.run(() => this.refresh());
    };
    // Hidden tabs do not retain rendered school notes. Returning rechecks access.
    const visibility = () => this.document.visibilityState === 'hidden' ? this.clear() : foreground();
    this.document.addEventListener('visibilitychange', visibility);
    this.document.defaultView?.addEventListener('focus', foreground);
    this.document.defaultView?.addEventListener('pageshow', foreground);
    this.destroy.onDestroy(() => {
      this.disposed = true; this.clear();
      this.document.removeEventListener('visibilitychange', visibility);
      this.document.defaultView?.removeEventListener('focus', foreground);
      this.document.defaultView?.removeEventListener('pageshow', foreground);
    });
  }
  ngOnInit() { this.pending.run(() => this.refresh()); }
  name(child: ParentChild) { return `${child.firstName} ${child.lastName}`; }

  private clear() {
    ++this.serial; clearTimeout(this.yearTimer);
    this.children.set([]); this.selected.set(null); this.notes.set([]); this.nextBefore.set(null);
    this.year.set(null); this.loading.set(false); this.loadingMore.set(false); this.error.set('');
  }
  private current(serial: number, owner: string | undefined) {
    return !this.disposed && serial === this.serial && !!owner && this.auth.session()?.id === owner
      && !!this.auth.session()?.roles.includes('Parent');
  }
  private window(data: ParentWindow, requestedAt: number): boolean {
    this.year.set(data.year); clearTimeout(this.yearTimer);
    // Start before the request so network latency cannot extend last year's view.
    // Use elapsed time because the phone's wall clock may be wrong.
    const deadline = requestedAt + Math.max(0, Date.parse(data.nextYearAt) - Date.parse(data.serverTime));
    if (deadline <= performance.now()) {
      this.clear();
      this.yearTimer = setTimeout(() => { if (this.auth.session()) this.pending.run(() => this.refresh()); }, 1000);
      return false;
    }
    const tick = () => {
      const remaining = deadline - performance.now();
      if (remaining <= 0) { this.clear(); if (this.auth.session()) void this.refresh(); }
      else this.yearTimer = setTimeout(tick, Math.min(remaining, 3_600_000));
    };
    tick();
    return true;
  }
  async refresh() {
    const preferred = this.selected()?.id ?? this.preferred;
    this.clear(); const serial = this.serial; const owner = this.auth.session()?.id;
    if (!owner || this.signingOut()) return;
    this.loading.set(true);
    try {
      const requestedAt = performance.now();
      const data = await this.api.children();
      if (!this.current(serial, owner)) return;
      if (!this.window(data, requestedAt)) return;
      this.children.set(data.children);
      const child = data.children.find(c => c.id === preferred) ?? data.children[0];
      if (child) { this.selected.set(child); await this.read(serial, owner, child.id); }
    } catch (error) { if (this.current(serial, owner)) this.fail(error); }
    finally { if (this.current(serial, owner)) this.loading.set(false); }
  }
  async choose(id: string) {
    const child = this.children().find(c => c.id === id);
    if (!child || this.signingOut()) return;
    const serial = ++this.serial; const owner = this.auth.session()?.id;
    this.preferred = child.id;
    this.selected.set(child); this.notes.set([]); this.nextBefore.set(null); this.error.set('');
    this.loadingMore.set(false); this.loading.set(true);
    try { await this.read(serial, owner, id); }
    catch (error) { if (this.current(serial, owner)) this.fail(error); }
    finally { if (this.current(serial, owner)) this.loading.set(false); }
  }
  private async read(serial: number, owner: string | undefined, child: string, before?: string) {
    const requestedAt = performance.now();
    const data = await this.api.feed(child, before);
    if (!this.current(serial, owner)) return;
    // A page requested just before New Year cannot mix two years of private notes.
    if (before && data.year !== this.year()) { await this.refresh(); return; }
    if (!this.window(data, requestedAt)) return;
    this.selected.set(data.child); this.preferred = data.child.id;
    this.notes.update(notes => before ? [...notes, ...data.notes.filter(n => !notes.some(old => old.id === n.id))] : data.notes);
    this.nextBefore.set(data.nextBefore);
  }
  async more() {
    const child = this.selected(); const before = this.nextBefore();
    if (!child || !before || this.loading() || this.loadingMore() || this.signingOut()) return;
    const serial = this.serial; const owner = this.auth.session()?.id;
    this.loadingMore.set(true); this.error.set('');
    try { await this.read(serial, owner, child.id, before); }
    catch (error) { if (this.current(serial, owner)) this.fail(error); }
    finally { if (this.current(serial, owner)) this.loadingMore.set(false); }
  }
  private fail(error: unknown) {
    this.preferred = undefined; this.clear();
    if (error instanceof HttpErrorResponse && error.status === 401) return;
    this.error.set(error instanceof HttpErrorResponse && [403, 404].includes(error.status)
      ? 'Your access to this child changed. Refresh your children’s list or contact the school.'
      : 'We couldn’t load your updates. Check your connection and try again.');
  }
  async logout() {
    this.signingOut.set(true); this.preferred = undefined; this.clear();
    try { await this.auth.logout(); }
    catch { this.error.set('We couldn’t sign you out. Check your connection and try again.'); }
    finally { this.signingOut.set(false); }
  }
}
