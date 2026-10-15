import { HttpClient, provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideZonelessChangeDetection } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { firstValueFrom } from 'rxjs';
import { PortalConnection, portalNetworkInterceptor } from './portal-connection';
import { PortalReloadSafety } from './portal-reload-safety';

describe('Portal network-only requests', () => {
  let http: HttpClient; let backend: HttpTestingController; let connection: PortalConnection; let safety: PortalReloadSafety;
  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideZonelessChangeDetection(), provideHttpClient(withInterceptors([portalNetworkInterceptor])), provideHttpClientTesting()] });
    http = TestBed.inject(HttpClient); backend = TestBed.inject(HttpTestingController); connection = TestBed.inject(PortalConnection); safety = TestBed.inject(PortalReloadSafety);
  });
  afterEach(() => backend.verify());
  it('bypasses service-worker caching for API reads without adding headers to external resources', async () => {
    const request = firstValueFrom(http.get('/api/portal/parent/children')); const read = backend.expectOne('/api/portal/parent/children');
    expect(read.request.headers.get('ngsw-bypass')).toBe('true'); expect(safety.writes()).toBe(0); read.flush({ children: [] }); await request;
    const outside = firstValueFrom(http.get('https://example.test/app.js')); const resource = backend.expectOne('https://example.test/app.js');
    expect(resource.request.headers.has('ngsw-bypass')).toBeFalse(); resource.flush({}); await outside;
  });
  it('rejects offline changes immediately and does not submit them when connectivity returns', async () => {
    connection.online.set(false); const result = firstValueFrom(http.put('/api/portal/teacher/students/child/note', { noteText: 'Fictional note' })).catch(e => e.status);
    expect(await result).toBe(0); backend.expectNone('/api/portal/teacher/students/child/note');
    window.dispatchEvent(new Event('online')); expect(connection.online()).toBeTrue(); backend.expectNone('/api/portal/teacher/students/child/note');
  });
  it('prevents reload during a write and releases it on success or error', async () => {
    let saving = firstValueFrom(http.post('/api/portal/admin/accounts', {})); expect(safety.canReload()).toBeFalse();
    backend.expectOne('/api/portal/admin/accounts').flush({}); await saving; expect(safety.canReload()).toBeTrue();
    saving = firstValueFrom(http.post('/api/portal/admin/accounts', {})).catch(() => ({}));
    backend.expectOne('/api/portal/admin/accounts').flush({}, { status: 503, statusText: 'Unavailable' }); await saving;
    expect(safety.writes()).toBe(0); expect(connection.disconnected()).toBeTrue();
  });
  it('clears a connection failure only after a successful response or reconnection', async () => {
    const failed = firstValueFrom(http.get('/api/portal/parent/children')).catch(() => null);
    backend.expectOne('/api/portal/parent/children').error(new ProgressEvent('error')); await failed; expect(connection.disconnected()).toBeTrue();
    const retry = firstValueFrom(http.get('/api/portal/parent/children')); backend.expectOne('/api/portal/parent/children').flush({ children: [] }); await retry;
    expect(connection.disconnected()).toBeFalse();
  });
});
