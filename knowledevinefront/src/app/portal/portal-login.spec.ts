import { provideZonelessChangeDetection, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { ActivatedRoute, convertToParamMap, provideRouter, Router } from '@angular/router';
import { PortalAuth, PortalSession } from './portal-auth';
import { PortalLogin } from './portal-login';
import { PortalNoteMemory } from './portal-note-memory';

describe('Login with an unsaved teacher note', () => {
  let auth: any;
  const session: PortalSession = { id: 'teacher', name: 'Fictional Teacher', email: 'teacher@example.test', roles: ['Teacher'], serverTime: '2026-10-15T08:00:00Z', expiresAt: '2026-10-15T08:30:00Z' };
  beforeEach(async () => {
    auth = { login: jasmine.createSpy().and.resolveTo(session), destination: jasmine.createSpy().and.returnValue('/portal/parent'), expired: signal(true) };
    await TestBed.configureTestingModule({ imports: [PortalLogin], providers: [provideZonelessChangeDetection(), provideRouter([]), { provide: PortalAuth, useValue: auth }, { provide: ActivatedRoute, useValue: { snapshot: { queryParamMap: convertToParamMap({}) } } }] }).compileComponents();
    spyOn(TestBed.inject(Router), 'navigateByUrl').and.resolveTo(true);
    TestBed.inject(PortalNoteMemory).pending.set({ ownerId: 'teacher', studentId: 'child', date: '2026-10-15', text: 'Private words', baseline: '', expectedVersion: null });
  });
  it('returns the same teacher to their note without showing its private text on login', async () => {
    const f = TestBed.createComponent(PortalLogin); f.detectChanges();
    expect(f.nativeElement.textContent).toContain('held in this tab'); expect(f.nativeElement.textContent).not.toContain('Private words');
    f.componentInstance.form.setValue({ email: session.email, password: 'Fictional-Existing-Password-42!' });
    await f.componentInstance.submit();
    expect(TestBed.inject(Router).navigateByUrl).toHaveBeenCalledWith('/portal/teacher/students/child?date=2026-10-15');
    expect(f.componentInstance.form.controls.password.value).toBe('');
  });
  it('discards the private recovery copy when a different account signs in', async () => {
    auth.login.and.resolveTo({ ...session, id: 'another-user', roles: ['Parent'] });
    const f = TestBed.createComponent(PortalLogin); f.componentInstance.form.setValue({ email: 'other@example.test', password: 'Fictional-Existing-Password-42!' });
    await f.componentInstance.submit();
    expect(TestBed.inject(PortalNoteMemory).pending()).toBeNull(); expect(TestBed.inject(Router).navigateByUrl).toHaveBeenCalledWith('/portal/parent');
  });
});
