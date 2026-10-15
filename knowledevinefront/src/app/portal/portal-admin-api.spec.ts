import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideZonelessChangeDetection, signal } from '@angular/core';
import { Router } from '@angular/router';
import { PortalAuth } from './portal-auth';
import { PortalAdminApi } from './portal-admin-api';

describe('Admin API client', () => {
  let http: HttpTestingController; let api: PortalAdminApi; let auth: jasmine.SpyObj<PortalAuth>; let router: jasmine.SpyObj<Router>;
  beforeEach(() => {
    auth = jasmine.createSpyObj('PortalAuth', ['prepareMutation', 'loadSession'], { expired: signal(false) }); auth.prepareMutation.and.resolveTo(); auth.loadSession.and.resolveTo(null);
    router = jasmine.createSpyObj('Router', ['navigateByUrl']); router.navigateByUrl.and.resolveTo(true);
    TestBed.configureTestingModule({ providers: [provideZonelessChangeDetection(), provideHttpClient(), provideHttpClientTesting(), { provide: PortalAuth, useValue: auth }, { provide: Router, useValue: router }] });
    http = TestBed.inject(HttpTestingController); api = TestBed.inject(PortalAdminApi);
  });
  afterEach(() => http.verify());
  it('prepares CSRF before creating an account', async () => {
    const input = { firstName: 'Fictional', lastName: 'Teacher', email: 'teacher@example.test', role: 'Teacher' as const, studentIds: [] };
    const pending = api.createAccount(input); await Promise.resolve(); await Promise.resolve();
    expect(auth.prepareMutation).toHaveBeenCalled(); const request = http.expectOne('/api/portal/admin/accounts'); expect(request.request.body).toEqual(input); request.flush({}); await pending;
  });
  it('returns to sign-in when an admin session has expired', async () => {
    const pending = api.directory().catch(error => error); http.expectOne('/api/portal/admin/directory').flush({}, { status: 401, statusText: 'Unauthorized' }); await pending;
    expect(auth.loadSession).toHaveBeenCalled(); expect(auth.expired()).toBeTrue(); expect(router.navigateByUrl).toHaveBeenCalledWith('/portal/login');
  });
});
