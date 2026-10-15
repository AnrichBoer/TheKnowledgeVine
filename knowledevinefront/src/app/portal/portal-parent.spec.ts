import { HttpErrorResponse } from '@angular/common/http';
import { provideZonelessChangeDetection, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { PortalAuth } from './portal-auth';
import { PortalParent } from './portal-parent';
import { ParentChildren, ParentFeed, ParentNote, PortalParentApi } from './portal-parent-api';
import { PortalConnection } from './portal-connection';
import { PortalReloadSafety } from './portal-reload-safety';

describe('Parent daily feed', () => {
  const child = { id: 'child', firstName: 'Willow', lastName: 'Fictional' };
  const sibling = { id: 'sibling', firstName: 'Oliver', lastName: 'Fictional' };
  const window = { year: 2026, serverTime: '2026-10-15T08:00:00Z', nextYearAt: '2026-12-31T22:00:00Z' };
  const note: ParentNote = { id: 'note', updateDate: '2026-10-15', noteText: 'First paragraph.\n\nSecond paragraph.', authorName: 'Original Teacher', editorName: 'Replacement Teacher', sharedAt: '2026-10-15T08:00:00Z', updatedAt: '2026-10-15T09:00:00Z', isEdited: true };
  let api: jasmine.SpyObj<PortalParentApi>; let auth: any; let data: ParentFeed;
  beforeEach(async () => {
    auth = { session: signal({ id: 'parent', name: 'Fictional Parent', roles: ['Parent'] }), logout: jasmine.createSpy().and.resolveTo() };
    data = { ...window, child, notes: [note], nextBefore: null };
    api = jasmine.createSpyObj('PortalParentApi', ['children', 'feed']);
    api.children.and.resolveTo({ ...window, children: [child, sibling] });
    api.feed.and.callFake(async id => ({ ...structuredClone(data), child: id === sibling.id ? sibling : child }));
    await TestBed.configureTestingModule({ imports: [PortalParent], providers: [provideZonelessChangeDetection(), provideRouter([]), { provide: PortalParentApi, useValue: api }, { provide: PortalAuth, useValue: auth }] }).compileComponents();
  });
  async function render() { const f = TestBed.createComponent(PortalParent); f.detectChanges(); await f.whenStable(); f.detectChanges(); return f; }
  function deferred<T>() { let resolve!: (value: T) => void; const promise = new Promise<T>(r => resolve = r); return { promise, resolve }; }

  it('renders a read-only feed, author, full paragraphs and South African correction time', async () => {
    const f = await render();
    expect(f.nativeElement.textContent).toContain('Willow’s days at school');
    expect(f.nativeElement.querySelector('.note-copy').textContent).toBe(note.noteText);
    expect(f.nativeElement.textContent).toContain('Original Teacher'); expect(f.nativeElement.textContent).toContain('Replacement Teacher');
    expect(f.nativeElement.textContent).toContain('11:00');
    expect(f.nativeElement.querySelector('textarea, form, input')).toBeNull();
    expect(f.nativeElement.textContent).not.toContain('Draft');
  });
  it('hides the correction label until a shared note has actually been edited', async () => {
    data.notes = [{ ...note, isEdited: false }]; const f = await render();
    expect(f.nativeElement.textContent).not.toContain('Edited'); expect(f.nativeElement.textContent).not.toContain('Replacement Teacher');
  });
  it('shows a helpful state when no children are linked', async () => {
    api.children.and.resolveTo({ ...window, children: [] }); const f = await render();
    expect(f.nativeElement.textContent).toContain('No children linked yet'); expect(api.feed).not.toHaveBeenCalled();
  });
  it('clears the private feed when connectivity is lost and fetches fresh data on return', async () => {
    const f = await render(); const c = f.componentInstance; const connection = TestBed.inject(PortalConnection);
    connection.online.set(false); f.detectChanges(); expect(c.notes()).toEqual([]); expect(c.children()).toEqual([]); expect(c.error()).toContain('Connect');
    connection.online.set(true); f.detectChanges(); await f.whenStable(); expect(c.notes().length).toBe(1); expect(api.children.calls.count()).toBe(2);
  });
  it('shows the selected child’s empty current-year feed', async () => {
    data.notes = []; const f = await render(); expect(f.nativeElement.textContent).toContain('Little moments are on their way');
    expect(f.nativeElement.textContent).toContain('Willow’s teacher shares a note for 2026');
  });
  it('clears the previous child immediately and ignores an older response after switching again', async () => {
    const f = await render(); const c = f.componentInstance; const slow = deferred<ParentFeed>();
    api.feed.and.returnValue(slow.promise); const switching = c.choose(sibling.id);
    expect(c.notes()).toEqual([]); expect(c.selected()?.id).toBe(sibling.id);
    api.feed.and.resolveTo(data); await c.choose(child.id);
    slow.resolve({ ...data, child: sibling, notes: [{ ...note, noteText: 'Stale sibling response.' }] }); await switching;
    expect(c.selected()?.id).toBe(child.id); expect(c.notes()[0].noteText).toBe(note.noteText);
  });
  it('rechecks the child list on refresh and switches to an enrolled sibling after access is removed', async () => {
    const f = await render(); const c = f.componentInstance;
    api.children.and.resolveTo({ ...window, children: [sibling] }); await c.refresh();
    expect(c.selected()?.id).toBe(sibling.id); expect(api.feed).toHaveBeenCalledWith(sibling.id, undefined);
  });
  it('clears notes and child names on access failure, including during pagination', async () => {
    data.nextBefore = '2026-10-15'; const f = await render(); const c = f.componentInstance;
    api.feed.and.rejectWith(new HttpErrorResponse({ status: 404 })); await c.more();
    expect(c.notes()).toEqual([]); expect(c.children()).toEqual([]); expect(c.selected()).toBeNull(); expect(c.error()).toContain('access');
  });
  it('clears stale private content on connection failure and offers a retry', async () => {
    const f = await render(); const c = f.componentInstance;
    api.children.and.rejectWith(new HttpErrorResponse({ status: 0 })); await c.refresh(); f.detectChanges();
    expect(c.notes()).toEqual([]); expect(f.nativeElement.textContent).toContain('Check your connection');
    expect(f.nativeElement.textContent).not.toContain('Willow');
  });
  it('appends earlier pages without duplicate notes', async () => {
    data.nextBefore = '2026-10-15'; const f = await render(); const c = f.componentInstance;
    const earlier = { ...note, id: 'earlier', updateDate: '2026-10-14' };
    api.feed.and.resolveTo({ ...data, notes: [note, earlier], nextBefore: null }); await c.more();
    expect(api.feed).toHaveBeenCalledWith(child.id, '2026-10-15'); expect(c.notes().map(n => n.id)).toEqual(['note', 'earlier']); expect(c.nextBefore()).toBeNull();
  });
  it('does not mix calendar years when loading an earlier page across New Year', async () => {
    data.nextBefore = '2026-10-15'; const f = await render(); const c = f.componentInstance;
    const nextWindow = { year: 2027, serverTime: '2026-12-31T22:00:00Z', nextYearAt: '2027-12-31T22:00:00Z' };
    api.children.and.resolveTo({ ...nextWindow, children: [child] });
    api.feed.and.resolveTo({ ...data, ...nextWindow, notes: [], nextBefore: null }); await c.more();
    expect(c.year()).toBe(2027); expect(c.notes()).toEqual([]); expect(api.children.calls.count()).toBe(2);
  });
  it('clears displayed notes and ignores an in-flight response after expiry', async () => {
    const f = await render(); const c = f.componentInstance; const slow = deferred<ParentFeed>();
    api.feed.and.returnValue(slow.promise); const loading = c.choose(sibling.id);
    auth.session.set(null); f.detectChanges(); slow.resolve(data); await loading;
    expect(c.notes()).toEqual([]); expect(c.children()).toEqual([]); expect(c.selected()).toBeNull();
  });
  it('clears notes before a sign-out request finishes and keeps them cleared if it fails', async () => {
    const f = await render(); const c = f.componentInstance; const slow = deferred<void>(); auth.logout.and.returnValue(slow.promise);
    const leaving = c.logout(); expect(c.notes()).toEqual([]); expect(c.signingOut()).toBeTrue(); expect(TestBed.inject(PortalReloadSafety).canReload()).toBeFalse(); slow.resolve(); await leaving;
    expect(auth.logout).toHaveBeenCalled(); expect(c.signingOut()).toBeFalse();
    auth.logout.and.rejectWith(new Error('offline')); await c.logout(); expect(c.notes()).toEqual([]); expect(c.error()).toContain('sign you out');
  });
  it('clears the previous account before loading another family', async () => {
    const f = await render(); const c = f.componentInstance; const slow = deferred<ParentChildren>(); api.children.and.returnValue(slow.promise);
    auth.session.set({ id: 'another', roles: ['Parent'] }); f.detectChanges(); expect(c.notes()).toEqual([]);
    slow.resolve({ ...window, children: [] }); await f.whenStable(); expect(c.children()).toEqual([]);
  });
  it('clears a hidden tab and rechecks the selected child when returning without renewing the session', async () => {
    const f = await render(); const c = f.componentInstance; await c.choose(sibling.id);
    const visibility = spyOnProperty(document, 'visibilityState', 'get').and.returnValue('hidden');
    document.dispatchEvent(new Event('visibilitychange')); expect(c.notes()).toEqual([]); expect(c.children()).toEqual([]);
    visibility.and.returnValue('visible'); document.dispatchEvent(new Event('visibilitychange')); await f.whenStable();
    expect(c.selected()?.id).toBe(sibling.id); expect(api.children.calls.count()).toBe(2);
  });
  it('removes last year’s notes at the server-defined boundary without relying on the phone clock', async () => {
    const f = await render(); const c = f.componentInstance; let now = 0; spyOn(performance, 'now').and.callFake(() => now);
    jasmine.clock().install();
    try {
      api.children.and.resolveTo({ ...window, children: [child], serverTime: '2026-12-31T21:59:59.950Z' });
      api.feed.and.resolveTo({ ...data, serverTime: '2026-12-31T21:59:59.950Z' }); await c.refresh();
      expect(c.notes().length).toBe(1);
      const waiting = deferred<ParentChildren>(); api.children.and.returnValue(waiting.promise);
      now = 51; jasmine.clock().tick(51); expect(c.notes()).toEqual([]); expect(c.year()).toBeNull();
      waiting.resolve({ year: 2027, serverTime: '2026-12-31T22:00:00Z', nextYearAt: '2027-12-31T22:00:00Z', children: [] });
      await Promise.resolve(); await Promise.resolve(); expect(c.year()).toBe(2027);
    } finally { f.destroy(); jasmine.clock().uninstall(); }
  });
  it('does not restore the previous year when its response arrives after the year boundary', async () => {
    const f = await render(); const c = f.componentInstance; let now = 0;
    spyOn(performance, 'now').and.callFake(() => now);
    api.children.and.callFake(async () => { now = 51; return { ...window, serverTime: '2026-12-31T21:59:59.950Z', children: [child] }; });
    api.feed.calls.reset(); await c.refresh();
    expect(c.notes()).toEqual([]); expect(c.children()).toEqual([]); expect(c.year()).toBeNull(); expect(api.feed).not.toHaveBeenCalled();
    f.destroy();
  });
});
