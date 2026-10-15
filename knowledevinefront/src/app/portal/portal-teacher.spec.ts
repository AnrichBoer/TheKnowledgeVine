import { HttpErrorResponse } from '@angular/common/http';
import { provideZonelessChangeDetection, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { ActivatedRoute, convertToParamMap, provideRouter, Router } from '@angular/router';
import { BehaviorSubject } from 'rxjs';
import { PortalAuth } from './portal-auth';
import { PortalNoteMemory } from './portal-note-memory';
import { PortalTeacher } from './portal-teacher';
import { PortalTeacherApi, TeacherEditor } from './portal-teacher-api';
import { PortalReloadSafety } from './portal-reload-safety';

describe('Teacher daily notes', () => {
  let api: jasmine.SpyObj<PortalTeacherApi>;
  let auth: any;
  let route: any;
  let data: TeacherEditor;
  beforeEach(async () => {
    data = { today: '2026-10-15', date: '2026-10-15', student: { id: 'child', firstName: 'Fictional', lastName: 'Child', status: 'not_started', version: null }, note: null };
    auth = { session: signal({ id: 'teacher', name: 'Fictional Teacher', roles: ['Teacher'] }), expired: signal(false), logout: jasmine.createSpy().and.resolveTo() };
    route = { snapshot: { paramMap: convertToParamMap({ id: 'child' }), queryParamMap: convertToParamMap({ date: '2026-10-15' }) }, paramMap: new BehaviorSubject(convertToParamMap({ id: 'child' })), queryParamMap: new BehaviorSubject(convertToParamMap({ date: '2026-10-15' })) };
    api = jasmine.createSpyObj('PortalTeacherApi', ['editor', 'worklist', 'save']);
    api.editor.and.callFake(async () => structuredClone(data));
    api.worklist.and.resolveTo({ today: data.today, date: data.date, students: [data.student, { ...data.student, id: 'second', firstName: 'Other', status: 'shared', version: 1 }] });
    await TestBed.configureTestingModule({ imports: [PortalTeacher], providers: [provideZonelessChangeDetection(), provideRouter([]), { provide: PortalTeacherApi, useValue: api }, { provide: PortalAuth, useValue: auth }, { provide: ActivatedRoute, useValue: route }] }).compileComponents();
    spyOn(TestBed.inject(Router), 'navigate').and.resolveTo(true);
  });
  async function render() {
    const f = TestBed.createComponent(PortalTeacher); f.detectChanges(); await f.whenStable(); f.detectChanges(); return f;
  }
  function saved(status: 'draft' | 'shared' = 'draft', version = 1, text = 'Saved text'): TeacherEditor {
    return { ...data, note: { id: 'note', status, version, noteText: text, authorName: 'Original Teacher', editorName: 'Current Teacher', createdAt: '2026-10-15T07:00:00Z', updatedAt: '2026-10-15T09:00:00Z', sharedAt: status === 'shared' ? '2026-10-15T08:00:00Z' : null } };
  }
  it('shows selected-date statuses and filters assigned children by name', async () => {
    route.snapshot.paramMap = convertToParamMap({});
    const f = await render(); const c = f.componentInstance;
    expect(f.nativeElement.textContent).toContain('Not started'); expect(f.nativeElement.textContent).toContain('Shared');
    c.search.set('fictional'); expect(c.visibleStudents().map(s => s.id)).toEqual(['child']); expect(c.sharedCount()).toBe(1);
  });
  it('shows a friendly empty assignment state', async () => {
    route.snapshot.paramMap = convertToParamMap({}); api.worklist.and.resolveTo({ today: data.today, date: data.date, students: [] });
    const f = await render(); expect(f.nativeElement.textContent).toContain('No students assigned yet');
  });
  it('holds typed text only in memory and clears the recovery copy after a saved draft', async () => {
    const f = await render(); const c = f.componentInstance; c.input('A little discovery.');
    expect(TestBed.inject(PortalNoteMemory).pending()).toEqual(jasmine.objectContaining({ ownerId: 'teacher', studentId: 'child', text: 'A little discovery.', expectedVersion: null }));
    api.save.and.resolveTo(saved('draft', 1, c.text())); await c.save(false);
    expect(api.save).toHaveBeenCalledWith('child', { updateDate: data.date, noteText: 'A little discovery.', share: false, expectedVersion: null });
    expect(c.dirty()).toBeFalse(); expect(TestBed.inject(PortalNoteMemory).pending()).toBeNull();
  });
  it('blocks app updates and offline submissions while keeping typed text only in the tab', async () => {
    const f = await render(); const c = f.componentInstance; c.input('Keep this discovery.');
    expect(TestBed.inject(PortalReloadSafety).canReload()).toBeFalse(); c.connection.online.set(false);
    await c.save(true); c.requestShare(); expect(api.save).not.toHaveBeenCalled(); expect(c.confirmShare()).toBeFalse();
    expect(c.text()).toBe('Keep this discovery.'); c.connection.online.set(true); expect(api.save).not.toHaveBeenCalled();
    api.save.and.resolveTo(saved('draft', 1, c.text())); await c.save(false); expect(TestBed.inject(PortalReloadSafety).canReload()).toBeTrue();
  });
  it('preserves typed text and expected version after a connection failure', async () => {
    data = saved('draft', 4); const f = await render(); const c = f.componentInstance; c.input('Unsaved work.');
    api.save.and.rejectWith(new HttpErrorResponse({ status: 0 })); await c.save(false);
    expect(c.text()).toBe('Unsaved work.'); expect(c.expectedVersion()).toBe(4); expect(c.dirty()).toBeTrue();
    expect(c.error()).toContain('connection'); expect(TestBed.inject(PortalNoteMemory).pending()?.text).toBe('Unsaved work.');
  });
  it('blocks blank sharing and enforces the character cap', async () => {
    const f = await render(); const c = f.componentInstance; c.input(' \n '); await c.save(true); expect(api.save).not.toHaveBeenCalled();
    c.input('a'.repeat(2001)); await c.save(false); expect(api.save).not.toHaveBeenCalled();
    expect(f.nativeElement.querySelector('textarea').maxLength).toBe(2000);
  });
  it('keeps corrections shared and shows original author plus South African edit time', async () => {
    data = saved('shared', 2); const f = await render(); const c = f.componentInstance;
    expect(f.nativeElement.textContent).toContain('Save correction'); expect(f.nativeElement.textContent).not.toContain('Save draft');
    expect(f.nativeElement.textContent).toContain('Original Teacher'); expect(f.nativeElement.textContent).toContain('11:00');
    c.input('A corrected note.'); api.save.and.resolveTo(saved('shared', 3, c.text())); await c.save(true);
    expect(api.save).toHaveBeenCalledWith('child', jasmine.objectContaining({ share: true, expectedVersion: 2 }));
  });
  it('requires explicit comparison/resolution after a conflict and preserves both texts', async () => {
    data = saved('draft', 1); const f = await render(); const c = f.componentInstance; c.input('My unsaved text.');
    api.save.and.rejectWith(new HttpErrorResponse({ status: 409, error: { message: 'Note changed' } })); await c.save(false);
    api.editor.and.resolveTo(saved('shared', 2, 'Newer saved text.')); await c.compareLatest();
    expect(c.text()).toBe('My unsaved text.'); expect(c.expectedVersion()).toBe(1); expect(c.latest()?.note?.noteText).toBe('Newer saved text.');
    api.save.calls.reset(); await c.save(true); expect(api.save).not.toHaveBeenCalled();
    c.resolve(true); expect(c.text()).toBe('My unsaved text.'); expect(c.expectedVersion()).toBe(2); expect(c.shared()).toBeTrue();
    expect(c.conflict()).toBeFalse(); expect(TestBed.inject(PortalNoteMemory).pending()?.expectedVersion).toBe(2);
  });
  it('restores the same teacher’s unsaved note after reauthentication and detects a newer saved version', async () => {
    TestBed.inject(PortalNoteMemory).pending.set({ ownerId: 'teacher', studentId: 'child', date: data.date, text: 'Recovered words.', baseline: '', expectedVersion: null });
    data = saved('draft', 1, 'Saved in another window.'); const f = await render(); const c = f.componentInstance;
    expect(c.text()).toBe('Recovered words.'); expect(c.conflict()).toBeTrue(); expect(c.latest()?.note?.noteText).toBe('Saved in another window.');
  });
  it('does not restore another account’s private text', async () => {
    TestBed.inject(PortalNoteMemory).pending.set({ ownerId: 'someone-else', studentId: 'child', date: data.date, text: 'Private words.', baseline: '', expectedVersion: null });
    const f = await render(); expect(f.componentInstance.text()).toBe(''); expect(TestBed.inject(PortalNoteMemory).pending()).toBeNull();
  });
  it('retains only the teacher’s previously typed copy when access is lost during reauthentication', async () => {
    TestBed.inject(PortalNoteMemory).pending.set({ ownerId: 'teacher', studentId: 'child', date: data.date, text: 'My retained words.', baseline: '', expectedVersion: null });
    api.editor.and.rejectWith(new HttpErrorResponse({ status: 404 }));
    const f = await render(); expect(f.componentInstance.recoveryOnly()).toBe('My retained words.');
    expect(f.nativeElement.querySelector('#recovered-note').readOnly).toBeTrue();
    expect(f.nativeElement.textContent).not.toContain('Fictional Child'); expect(api.save).not.toHaveBeenCalled();
  });
  it('allows expiry navigation while a save is failing without discarding the recovery copy', async () => {
    const f = await render(); const c = f.componentInstance; c.input('Keep these words.'); c.busy.set(true);
    auth.expired.set(true); auth.session.set(null);
    expect(c.mayLeave()).toBeTrue(); expect(TestBed.inject(PortalNoteMemory).pending()?.text).toBe('Keep these words.');
  });
  it('requires confirmation before leaving or changing date with unsaved work', async () => {
    const f = await render(); const c = f.componentInstance; c.input('Keep these words.'); spyOn(window, 'confirm').and.returnValue(false);
    expect(c.mayLeave()).toBeFalse(); await c.changeDate('2026-10-14');
    expect(TestBed.inject(Router).navigate).not.toHaveBeenCalled(); expect(c.text()).toBe('Keep these words.');
    const input = document.createElement('input'); input.value = '2026-10-14';
    await c.selectDate({ target: input } as unknown as Event); expect(input.value).toBe('2026-10-15');
  });
  it('retains unsaved text but prevents saving when assignment has changed', async () => {
    const f = await render(); const c = f.componentInstance; c.input('Unsent note.');
    api.save.and.rejectWith(new HttpErrorResponse({ status: 404, error: { message: 'Assignment changed' } })); await c.save(false);
    expect(c.accessLost()).toBeTrue(); expect(c.text()).toBe('Unsent note.'); api.save.calls.reset(); await c.save(false); expect(api.save).not.toHaveBeenCalled();
  });
});
