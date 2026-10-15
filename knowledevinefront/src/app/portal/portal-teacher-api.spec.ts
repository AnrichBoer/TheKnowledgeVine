import { provideZonelessChangeDetection } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { PortalAuth } from './portal-auth';
import { PortalTeacherApi } from './portal-teacher-api';
import { PortalNoteMemory } from './portal-note-memory';

describe('Teacher API and memory boundary', () => {
  let api: PortalTeacherApi; let http: HttpTestingController; let auth: jasmine.SpyObj<PortalAuth>;
  beforeEach(() => {
    auth = jasmine.createSpyObj('PortalAuth', ['prepareMutation', 'requireSignIn']); auth.prepareMutation.and.resolveTo();
    TestBed.configureTestingModule({ providers: [provideZonelessChangeDetection(), provideHttpClient(), provideHttpClientTesting(), { provide: PortalAuth, useValue: auth }] });
    api = TestBed.inject(PortalTeacherApi); http = TestBed.inject(HttpTestingController);
  });
  afterEach(() => http.verify());
  it('sends the selected date and optimistic version with CSRF preparation', async () => {
    const input = { updateDate: '2026-10-15', noteText: 'Fictional note', share: true, expectedVersion: 3 };
    const saving = api.save('child', input); await Promise.resolve();
    const request = http.expectOne('/api/portal/teacher/students/child/note'); expect(request.request.body).toEqual(input); request.flush({ note: {} }); await saving;
    expect(auth.prepareMutation).toHaveBeenCalled();
  });
  it('requests reauthentication on 401 and leaves the held note intact', async () => {
    const memory = TestBed.inject(PortalNoteMemory); memory.pending.set({ ownerId: 'teacher', studentId: 'child', date: '2026-10-15', text: 'Keep this', baseline: '', expectedVersion: null });
    const result = api.editor('child', '2026-10-15').catch(error => error.status);
    http.expectOne('/api/portal/teacher/students/child/note?date=2026-10-15').flush({}, { status: 401, statusText: 'Unauthorized' });
    expect(await result).toBe(401); expect(auth.requireSignIn).toHaveBeenCalled(); expect(memory.pending()?.text).toBe('Keep this');
  });
  it('returns only an internal recovery URL for the same teacher and clears it for another account', () => {
    const memory = TestBed.inject(PortalNoteMemory); memory.pending.set({ ownerId: 'teacher', studentId: 'child', date: '2026-10-15', text: 'Keep this', baseline: '', expectedVersion: null });
    expect(memory.resumeUrl('teacher', ['Teacher'])).toBe('/portal/teacher/students/child?date=2026-10-15');
    expect(memory.resumeUrl('other', ['Teacher'])).toBeNull(); expect(memory.pending()).toBeNull();
  });
});
