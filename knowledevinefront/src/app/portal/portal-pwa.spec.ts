import { provideZonelessChangeDetection, DestroyRef, createEnvironmentInjector, EnvironmentInjector } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { SwUpdate } from '@angular/service-worker';
import { Subject } from 'rxjs';
import { PortalPwa, PORTAL_RELOAD } from './portal-pwa';
import { PortalReloadSafety } from './portal-reload-safety';
import { PortalNoteMemory } from './portal-note-memory';
import { PortalConnection } from './portal-connection';
import { PortalTools } from './portal-tools';

describe('Portal installation and safe app updates', () => {
  let versions: Subject<any>; let broken: Subject<any>; let updates: any; let reload: jasmine.Spy; let pwa: PortalPwa;
  beforeEach(() => {
    versions = new Subject(); broken = new Subject(); reload = jasmine.createSpy('reload');
    updates = { isEnabled: true, versionUpdates: versions, unrecoverable: broken, checkForUpdate: jasmine.createSpy().and.resolveTo(false) };
    TestBed.configureTestingModule({ imports: [PortalTools], providers: [provideZonelessChangeDetection(), { provide: SwUpdate, useValue: updates }, { provide: PORTAL_RELOAD, useValue: reload }] });
    pwa = TestBed.inject(PortalPwa);
  });
  function ready() { versions.next({ type: 'VERSION_READY', currentVersion: { hash: 'old' }, latestVersion: { hash: 'new' } }); }
  it('offers an update without activating or reloading it automatically', () => {
    ready(); expect(pwa.updateReady()).toBeTrue(); expect(reload).not.toHaveBeenCalled();
    pwa.applyUpdate(); expect(reload).toHaveBeenCalledTimes(1);
  });
  it('does not reload an unsaved teacher note, including during reauthentication', () => {
    ready(); TestBed.inject(PortalNoteMemory).pending.set({ ownerId: 'fictional', studentId: 'child', date: '2026-10-15', text: 'Keep these words.', baseline: '', expectedVersion: null });
    pwa.applyUpdate(); expect(reload).not.toHaveBeenCalled();
    TestBed.inject(PortalNoteMemory).clear(); pwa.applyUpdate(); expect(reload).toHaveBeenCalled();
  });
  it('waits for mutations, dirty forms and memory-only setup links before reloading', () => {
    ready(); const safety = TestBed.inject(PortalReloadSafety); safety.writes.set(1); pwa.applyUpdate(); expect(reload).not.toHaveBeenCalled();
    safety.writes.set(0); let blocked = true; const scope = createEnvironmentInjector([], TestBed.inject(EnvironmentInjector));
    safety.protect(() => blocked, scope.get(DestroyRef)); pwa.applyUpdate(); expect(reload).not.toHaveBeenCalled();
    blocked = false; pwa.applyUpdate(); expect(reload).toHaveBeenCalledTimes(1);
    blocked = true; scope.destroy(); pwa.applyUpdate(); expect(reload).toHaveBeenCalledTimes(2);
  });
  it('keeps a safe update pending while offline', () => {
    ready(); TestBed.inject(PortalConnection).online.set(false); pwa.applyUpdate(); expect(reload).not.toHaveBeenCalled();
    TestBed.inject(PortalConnection).online.set(true); pwa.applyUpdate(); expect(reload).toHaveBeenCalled();
  });
  it('shows a recoverable update failure without discarding work', () => {
    versions.next({ type: 'VERSION_INSTALLATION_FAILED' }); expect(pwa.updateFailed()).toBeTrue(); expect(reload).not.toHaveBeenCalled();
    broken.next({ reason: 'asset no longer available' }); expect(pwa.updateReady()).toBeTrue(); expect(reload).not.toHaveBeenCalled();
  });
  it('throttles foreground update checks and never polls account activity', async () => {
    await pwa.checkForUpdate(); await pwa.checkForUpdate(); expect(updates.checkForUpdate).toHaveBeenCalledTimes(1);
    TestBed.inject(PortalConnection).online.set(false); await pwa.checkForUpdate(); expect(updates.checkForUpdate).toHaveBeenCalledTimes(1);
  });
  it('consumes a supported install prompt once and leaves declined installation optional', async () => {
    const event = new Event('beforeinstallprompt', { cancelable: true });
    const prompt = jasmine.createSpy().and.resolveTo(); Object.assign(event, { prompt, userChoice: Promise.resolve({ outcome: 'dismissed' }) });
    window.dispatchEvent(event); expect(event.defaultPrevented).toBeTrue(); expect(pwa.installAvailable()).toBeTrue();
    await pwa.install(); await pwa.install(); expect(prompt).toHaveBeenCalledTimes(1); expect(pwa.installAvailable()).toBeFalse(); expect(pwa.installNotice()).toContain('browser');
  });
  it('does not invoke installation automatically or while offline', async () => {
    const prompt = jasmine.createSpy().and.resolveTo(); const event = new Event('beforeinstallprompt');
    Object.assign(event, { prompt, userChoice: Promise.resolve({ outcome: 'accepted' }) }); window.dispatchEvent(event);
    expect(prompt).not.toHaveBeenCalled(); TestBed.inject(PortalConnection).online.set(false); await pwa.install(); expect(prompt).not.toHaveBeenCalled();
  });
  it('hides install help after the browser reports installation', () => {
    pwa.helpOpen.set(true); window.dispatchEvent(new Event('appinstalled'));
    expect(pwa.standalone()).toBeTrue(); expect(pwa.helpOpen()).toBeFalse();
  });
  it('shows accessible phone instructions and preserves the generic public login wording', async () => {
    pwa.helpOpen.set(true); const f = TestBed.createComponent(PortalTools); f.detectChanges(); await f.whenStable();
    const dialog = f.nativeElement.querySelector('[role="dialog"]'); expect(dialog.getAttribute('aria-modal')).toBe('true');
    expect(dialog.textContent).toContain('iPhone'); expect(dialog.textContent).toContain('Android'); expect(dialog.textContent).toContain('30 minutes');
    expect(dialog.textContent).not.toContain('Admin'); expect(dialog.textContent).not.toContain('Keep me signed in');
  });
});
