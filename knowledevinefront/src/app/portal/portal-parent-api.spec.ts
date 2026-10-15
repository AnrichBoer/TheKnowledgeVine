import { provideZonelessChangeDetection } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { PortalAuth } from './portal-auth';
import { PortalParentApi } from './portal-parent-api';

describe('Parent API boundary', () => {
  let api: PortalParentApi; let http: HttpTestingController; let auth: jasmine.SpyObj<PortalAuth>;
  beforeEach(() => {
    auth = jasmine.createSpyObj('PortalAuth', ['requireSignIn', 'prepareMutation']);
    TestBed.configureTestingModule({ providers: [provideZonelessChangeDetection(), provideHttpClient(), provideHttpClientTesting(), { provide: PortalAuth, useValue: auth }] });
    api = TestBed.inject(PortalParentApi); http = TestBed.inject(HttpTestingController);
  });
  afterEach(() => http.verify());
  it('reads children and cursor pages without mutation or session renewal', async () => {
    const children = api.children(); const list = http.expectOne('/api/portal/parent/children'); expect(list.request.method).toBe('GET'); list.flush({ children: [] }); await children;
    const feed = api.feed('child', '2026-10-15'); http.expectOne('/api/portal/parent/children/child/updates?before=2026-10-15').flush({ notes: [] }); await feed;
    expect(auth.prepareMutation).not.toHaveBeenCalled();
  });
  it('requires reauthentication after an expired or revoked session', async () => {
    const result = api.feed('child').catch(e => e.status);
    http.expectOne('/api/portal/parent/children/child/updates').flush({}, { status: 401, statusText: 'Unauthorized' });
    expect(await result).toBe(401); expect(auth.requireSignIn).toHaveBeenCalled();
  });
});
