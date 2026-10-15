import { provideZonelessChangeDetection } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { portalNetworkInterceptor } from './portal-connection';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { Router } from '@angular/router';
import { PortalAuth, PortalSession } from './portal-auth';
import { PortalNoteMemory } from './portal-note-memory';

describe('PortalAuth', () => {
  let auth: PortalAuth;
  let http: HttpTestingController;
  let router: jasmine.SpyObj<Router>;
  const session: PortalSession = {
    id: 'fictional-id', name: 'Fictional Admin', email: 'fictional@example.test', roles: ['Admin'],
    serverTime: '2026-10-14T12:00:00Z', expiresAt: '2026-10-14T12:30:00Z',
  };
  beforeEach(() => {
    jasmine.clock().install(); jasmine.clock().mockDate(new Date('2026-10-14T12:00:00Z'));
    router = jasmine.createSpyObj<Router>('Router', ['navigateByUrl'], { url: '/portal/admin' });
    router.navigateByUrl.and.resolveTo(true);
    TestBed.configureTestingModule({ providers: [provideZonelessChangeDetection(), provideHttpClient(withInterceptors([portalNetworkInterceptor])), provideHttpClientTesting(), { provide: Router, useValue: router }] });
    auth = TestBed.inject(PortalAuth); http = TestBed.inject(HttpTestingController);
  });
  afterEach(() => { http.verify(); TestBed.resetTestingModule(); jasmine.clock().uninstall(); });

  it('refreshes the CSRF identity after login and never requests a remembered session', async () => {
    const pending = auth.login(session.email, 'Fictional-Password-42!');
    http.expectOne('/api/portal/auth/csrf').flush({ ready: true });
    await Promise.resolve();
    const login = http.expectOne('/api/portal/auth/login');
    expect(Object.keys(login.request.body).sort()).toEqual(['email', 'password']);
    login.flush(session);
    await Promise.resolve();
    http.expectOne('/api/portal/auth/csrf').flush({ ready: true });
    expect(await pending).toEqual(session);
    expect(auth.session()).toEqual(session);
  });

  it('shares a pending session lookup between route guards', async () => {
    const first = auth.loadSession(); const second = auth.loadSession();
    http.expectOne('/api/portal/auth/session').flush(session);
    expect(await first).toEqual(session); expect(await second).toEqual(session);
  });

  it('uses the remaining server lifetime even when the device clock is different', async () => {
    jasmine.clock().mockDate(new Date('2026-10-14T16:00:00Z'));
    const pending = auth.loadSession();
    http.expectOne('/api/portal/auth/session').flush(session); await pending;
    jasmine.clock().tick(29 * 60 * 1000);
    expect(auth.session()).toEqual(session);
    jasmine.clock().tick(60 * 1000 + 1);
    expect(auth.session()).toBeNull(); expect(auth.expired()).toBeTrue();
    expect(router.navigateByUrl).toHaveBeenCalledWith('/portal/login');
  });

  it('does not send unattended activity requests or renew from synthetic events', async () => {
    const pending = auth.loadSession();
    http.expectOne('/api/portal/auth/session').flush(session); await pending;
    document.dispatchEvent(new Event('keydown'));
    jasmine.clock().tick(20 * 60 * 1000);
    http.expectNone('/api/portal/auth/activity');
    expect(auth.session()).toEqual(session);
  });

  it('chooses only a destination supported by the account roles', () => {
    expect(auth.destination({ ...session, roles: ['Parent'] })).toBe('/portal/parent');
    expect(auth.destination({ ...session, roles: ['Parent', 'Teacher'] })).toBe('/portal/teacher');
    expect(auth.destination({ ...session, roles: [] })).toBe('/portal/login');
  });

  it('retains an unsaved note through idle expiry but clears it on explicit logout', async () => {
    const memory = TestBed.inject(PortalNoteMemory);
    const held = { ownerId: session.id, studentId: 'child', date: '2026-10-14', text: 'Unsaved words', baseline: '', expectedVersion: null };
    memory.pending.set(held);
    const loading = auth.loadSession(); http.expectOne('/api/portal/auth/session').flush(session); await loading;
    jasmine.clock().tick(30 * 60 * 1000 + 1);
    expect(memory.pending()).toEqual(held); expect(auth.expired()).toBeTrue();
    const logout = auth.logout(); http.expectOne('/api/portal/auth/csrf').flush({}); await Promise.resolve();
    http.expectOne('/api/portal/auth/logout').flush(null); await logout;
    expect(memory.pending()).toBeNull();
  });
});
