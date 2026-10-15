import { provideZonelessChangeDetection, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { ActivatedRoute, convertToParamMap, provideRouter, Router } from '@angular/router';
import { BehaviorSubject } from 'rxjs';
import { AdminAccount, AdminDirectory, PortalAdminApi } from './portal-admin-api';
import { PortalAdminWorkspace } from './portal-admin-workspace';
import { HttpErrorResponse } from '@angular/common/http';

describe('Admin workspace', () => {
  let api: jasmine.SpyObj<PortalAdminApi>;
  let data: AdminDirectory;
  let route: { snapshot: any; paramMap: BehaviorSubject<any>; data: BehaviorSubject<any>; queryParamMap: BehaviorSubject<any> };
  const account = (id: string, role: 'Parent' | 'Teacher', ready: boolean, active = true): AdminAccount => ({ id, firstName: id, lastName: 'Fictional', email: `${id}@example.test`, roles: [role], isReady: ready, isActive: active, version: 'v1', studentIds: [] });
  beforeEach(async () => {
    data = { invitationPreview: true, accounts: [account('ready-teacher', 'Teacher', true), account('pending-teacher', 'Teacher', false), account('inactive-teacher', 'Teacher', true, false), account('parent', 'Parent', false)], students: [{ id: 'child', firstName: 'Fictional', lastName: 'Child', teacherUserId: null, isActive: true, updatedAt: '2026-10-14T12:00:00Z', parentIds: [] }] };
    route = { snapshot: { data: { view: 'students' }, paramMap: convertToParamMap({}), queryParamMap: convertToParamMap({}) }, paramMap: new BehaviorSubject(convertToParamMap({})), data: new BehaviorSubject({ view: 'students' }), queryParamMap: new BehaviorSubject(convertToParamMap({})) };
    api = jasmine.createSpyObj('PortalAdminApi', ['directory', 'createAccount', 'resend', 'state', 'children', 'saveStudent', 'preview', 'flash'], { notice: signal(null) });
    api.directory.and.callFake(async () => structuredClone(data));
    await TestBed.configureTestingModule({ imports: [PortalAdminWorkspace], providers: [provideZonelessChangeDetection(), provideRouter([]), { provide: ActivatedRoute, useValue: route }, { provide: PortalAdminApi, useValue: api }] }).compileComponents();
    spyOn(TestBed.inject(Router), 'navigate').and.resolveTo(true);
  });
  async function render(view: string, id?: string) {
    route.snapshot.data = { view }; route.snapshot.paramMap = convertToParamMap(id ? { id } : {});
    const fixture = TestBed.createComponent(PortalAdminWorkspace); fixture.detectChanges(); await fixture.whenStable(); fixture.detectChanges(); return fixture;
  }
  it('creates a pending teacher without assigning students or entering a password', async () => {
    const fixture = await render('teacher'); const component = fixture.componentInstance;
    component.form.patchValue({ firstName: 'Fictional', lastName: 'Teacher', email: 'teacher@example.test' });
    api.createAccount.and.resolveTo({ account: data.accounts[1], delivery: 'preview', message: 'Preview saved' });
    await component.createAccount();
    expect(api.createAccount).toHaveBeenCalledWith({ firstName: 'Fictional', lastName: 'Teacher', email: 'teacher@example.test', role: 'Teacher', studentIds: [] });
    expect(fixture.nativeElement.querySelector('input[type=password]')).toBeNull();
    expect(fixture.nativeElement.querySelector('fieldset')).toBeNull();
  });
  it('creates a parent with the selected children while their setup is pending', async () => {
    const fixture = await render('parent'); const component = fixture.componentInstance;
    component.form.patchValue({ firstName: 'Fictional', lastName: 'Parent', email: 'parent@example.test' }); component.selectedChildren.set(['child']);
    api.createAccount.and.resolveTo({ account: data.accounts[3], delivery: 'preview', message: 'Preview saved' });
    await component.createAccount();
    expect(api.createAccount).toHaveBeenCalledWith({ firstName: 'Fictional', lastName: 'Parent', email: 'parent@example.test', role: 'Parent', studentIds: ['child'] });
  });
  it('does not submit blank names or invalid email addresses', async () => {
    const fixture = await render('parent'); await fixture.componentInstance.createAccount(); fixture.detectChanges();
    expect(api.createAccount).not.toHaveBeenCalled(); expect(fixture.nativeElement.textContent).toContain('Enter a valid email address.');
    fixture.componentInstance.form.patchValue({ firstName: '   ', lastName: 'Family', email: 'parent@example.test' });
    await fixture.componentInstance.createAccount(); expect(api.createAccount).not.toHaveBeenCalled();
  });
  it('offers only active, setup-complete teachers in the student selector', async () => {
    const fixture = await render('student');
    const selector = fixture.nativeElement.querySelector('select[formcontrolname=teacherUserId]');
    expect(selector.textContent).toContain('ready-teacher'); expect(selector.textContent).not.toContain('pending-teacher'); expect(selector.textContent).not.toContain('inactive-teacher');
  });
  it('keeps assignment disabled for pending teachers', async () => {
    const fixture = await render('account', 'pending-teacher');
    const button = [...fixture.nativeElement.querySelectorAll('button')].find((b: any) => b.textContent.trim() === 'Assign students') as HTMLButtonElement;
    expect(button.disabled).toBeTrue(); expect(fixture.nativeElement.textContent).toContain('must complete password setup');
  });
  it('lets pending parents save child links', async () => {
    const fixture = await render('account', 'parent'); const component = fixture.componentInstance;
    component.selectedChildren.set(['child']); api.children.and.resolveTo(data.accounts[3]); await component.saveChildren();
    expect(api.children).toHaveBeenCalledWith(jasmine.objectContaining({ id: 'parent', isReady: false }), ['child']);
  });
  it('reports delivery failure while retaining the saved account for retry', async () => {
    const fixture = await render('teacher'); const component = fixture.componentInstance;
    component.form.patchValue({ firstName: 'Fictional', lastName: 'Teacher', email: 'teacher@example.test' });
    api.createAccount.and.resolveTo({ account: data.accounts[1], delivery: 'failed', message: 'Account saved, invitation failed' }); await component.createAccount();
    expect(api.flash).toHaveBeenCalledWith('Account saved, invitation failed', true);
    expect(TestBed.inject(Router).navigate).toHaveBeenCalledWith(['/portal/admin/accounts', 'pending-teacher']);
  });
  it('shows the existing account link without clearing the entered details on a duplicate', async () => {
    const fixture = await render('teacher'); const component = fixture.componentInstance;
    component.form.patchValue({ firstName: 'Fictional', lastName: 'Teacher', email: 'teacher@example.test' });
    api.createAccount.and.rejectWith(new HttpErrorResponse({ status: 409, error: { message: 'Already exists', existingAccountId: 'ready-teacher' } }));
    await component.createAccount(); fixture.detectChanges();
    expect(component.existingAccountId()).toBe('ready-teacher'); expect(component.form.controls.email.value).toBe('teacher@example.test'); expect(fixture.nativeElement.textContent).toContain('Open existing account');
  });
  it('preserves unsaved student fields on a conflicting save', async () => {
    const fixture = await render('student', 'child'); const component = fixture.componentInstance;
    component.form.patchValue({ firstName: 'Unsaved name' }); component.selectedParents.set(['parent']);
    api.saveStudent.and.rejectWith(new HttpErrorResponse({ status: 409, error: { message: 'Student changed; refresh' } }));
    await component.saveStudent(); expect(component.form.controls.firstName.value).toBe('Unsaved name'); expect(component.selectedParents()).toEqual(['parent']);
    expect(api.saveStudent).toHaveBeenCalledWith('child', jasmine.objectContaining({ expectedUpdatedAt: data.students[0].updatedAt, expectedParentIds: [] }));
  });
  it('labels inactive accounts separately from completed setup', async () => {
    const fixture = await render('accounts'); const component = fixture.componentInstance;
    component.state.set('inactive'); fixture.detectChanges(); expect(component.accounts().map(a => a.id)).toEqual(['inactive-teacher']); expect(fixture.nativeElement.textContent).toContain('Inactive');
  });
});
