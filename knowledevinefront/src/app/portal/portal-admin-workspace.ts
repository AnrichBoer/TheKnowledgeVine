import { CdkTrapFocus } from '@angular/cdk/a11y';
import { HttpErrorResponse } from '@angular/common/http';
import { Component, computed, DestroyRef, inject, signal, WritableSignal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { combineLatest } from 'rxjs';
import { AdminAccount, AdminDirectory, AdminStudent, InvitationPreview, InvitationResult, PortalAdminApi } from './portal-admin-api';
import { portalError, PortalRole } from './portal-auth';
import { PortalReloadSafety } from './portal-reload-safety';

type View = 'accounts' | 'teacher' | 'parent' | 'account' | 'students' | 'student';
@Component({ selector: 'app-portal-admin-workspace', imports: [RouterLink, ReactiveFormsModule, CdkTrapFocus], templateUrl: './portal-admin-workspace.html' })
export class PortalAdminWorkspace {
  readonly api = inject(PortalAdminApi);
  private readonly route = inject(ActivatedRoute); private readonly router = inject(Router);
  private readonly destroy = inject(DestroyRef); private sequence = 0;
  get routeId() { return this.route.snapshot.paramMap.get('id'); }
  readonly view = signal<View>('students'); readonly directory = signal<AdminDirectory>({ accounts: [], students: [], invitationPreview: false });
  readonly account = signal<AdminAccount | null>(null); readonly student = signal<AdminStudent | null>(null);
  readonly loading = signal(true); readonly busy = signal(false); readonly error = signal(''); readonly existingAccountId = signal('');
  readonly search = signal(''); readonly state = signal(''); readonly roleFilter = signal(''); readonly selectionSearch = signal('');
  readonly selectedChildren = signal<string[]>([]); readonly selectedParents = signal<string[]>([]);
  readonly preview = signal<InvitationPreview | null>(null); readonly confirmState = signal(false);
  readonly assignTo = signal<AdminAccount | null>(null);
  readonly form = inject(FormBuilder).nonNullable.group({
    firstName: ['', [Validators.required, Validators.maxLength(100), Validators.pattern(/\S/)]],
    lastName: ['', [Validators.required, Validators.maxLength(100), Validators.pattern(/\S/)]],
    email: ['', [Validators.required, Validators.email, Validators.maxLength(256)]], teacherUserId: [''], isActive: [true],
  });
  readonly accounts = computed(() => this.directory().accounts.filter(a => this.matches(a.firstName + ' ' + a.lastName + ' ' + a.email, this.search())
    && (!this.roleFilter() || a.roles.includes(this.roleFilter() as PortalRole))
    && (!this.state() || (this.state() === 'inactive' ? !a.isActive : this.state() === 'pending' ? a.isActive && !a.isReady : this.state() === 'ready' ? a.isActive && a.isReady : a.isActive))));
  readonly students = computed(() => this.directory().students.filter(s => this.matches(s.firstName + ' ' + s.lastName, this.search()) && (!this.state() || s.isActive === (this.state() === 'active'))));
  readonly teachers = computed(() => this.directory().accounts.filter(a => a.roles.includes('Teacher') && a.isActive && a.isReady));
  readonly parentOptions = computed(() => this.directory().accounts.filter(a => a.roles.includes('Parent') && (a.isActive || this.student()?.parentIds.includes(a.id)) && this.matches(a.firstName + ' ' + a.lastName + ' ' + a.email, this.selectionSearch())));
  readonly childOptions = computed(() => this.directory().students.filter(s => (s.isActive || this.account()?.studentIds.includes(s.id)) && this.matches(s.firstName + ' ' + s.lastName, this.selectionSearch())));
  readonly linkedStudents = computed(() => this.directory().students.filter(s => s.teacherUserId === this.account()?.id));
  constructor() {
    inject(PortalReloadSafety).protect(() => this.busy() || this.form.dirty
      || this.selectedParents().slice().sort().join() !== (this.student()?.parentIds ?? []).slice().sort().join()
      || this.selectedChildren().slice().sort().join() !== (this.account()?.studentIds ?? []).slice().sort().join(), this.destroy, this.form.events);
    combineLatest([this.route.paramMap, this.route.data, this.route.queryParamMap]).pipe(takeUntilDestroyed(this.destroy))
      .subscribe(() => { this.view.set(this.route.snapshot.data['view']); this.search.set(''); this.state.set(''); this.selectionSearch.set(''); void this.load(); });
  }
  async load() {
    const sequence = ++this.sequence;
    this.loading.set(true); this.error.set(''); this.confirmState.set(false); this.existingAccountId.set('');
    try {
      const data = await this.api.directory(); if (sequence !== this.sequence) return;
      this.directory.set(data);
      const id = this.route.snapshot.paramMap.get('id');
      this.account.set(data.accounts.find(a => a.id === id) ?? null); this.student.set(data.students.find(s => s.id === id) ?? null);
      const teacher = data.accounts.find(a => a.id === this.route.snapshot.queryParamMap.get('assignTo') && a.roles.includes('Teacher') && a.isActive && a.isReady);
      this.assignTo.set(teacher ?? null);
      if ((this.view() === 'account' && !this.account()) || (this.view() === 'student' && id && !this.student())) { this.error.set('This record was not found. Return to the list and refresh.'); return; }
      const student = this.student();
      this.form.reset({ firstName: student?.firstName ?? '', lastName: student?.lastName ?? '', email: '', teacherUserId: teacher?.id ?? student?.teacherUserId ?? '', isActive: student?.isActive ?? true });
      this.selectedParents.set(student?.parentIds ?? []); this.selectedChildren.set(this.account()?.roles.includes('Parent') ? this.account()!.studentIds : []);
    } catch (error) { if (sequence === this.sequence) this.error.set(portalError(error)); }
    finally { if (sequence === this.sequence) this.loading.set(false); }
  }
  name(item: { firstName: string; lastName: string }) { return `${item.firstName} ${item.lastName}`; }
  teacherName(id: string | null) { const teacher = this.directory().accounts.find(a => a.id === id); return teacher ? this.name(teacher) : 'Not assigned'; }
  canInvite(a: AdminAccount) { return a.isActive && !a.isReady && !a.roles.includes('Admin'); }
  toggle(ids: WritableSignal<string[]>, id: string) { ids.update(values => values.includes(id) ? values.filter(v => v !== id) : [...values, id]); }
  fieldError(name: 'firstName' | 'lastName' | 'email') { const control = this.form.controls[name]; return control.touched && control.invalid; }
  private matches(value: string, query: string) { return value.toLocaleLowerCase().includes(query.trim().toLocaleLowerCase()); }
  private invitation(result: InvitationResult) { this.api.flash(result.message, result.delivery === 'failed'); }
  private async action(work: () => Promise<void>) {
    if (this.busy()) return;
    this.busy.set(true); this.error.set(''); this.existingAccountId.set(''); this.api.notice.set(null);
    try { await work(); }
    catch (error) {
      this.error.set(portalError(error));
      if (error instanceof HttpErrorResponse && error.error?.existingAccountId) this.existingAccountId.set(error.error.existingAccountId);
    } finally { this.busy.set(false); }
  }
  async createAccount() {
    const controls = this.form.controls;
    if (controls.firstName.invalid || controls.lastName.invalid || controls.email.invalid) { this.form.markAllAsTouched(); return; }
    await this.action(async () => {
      const raw = this.form.getRawValue();
      const result = await this.api.createAccount({ firstName: raw.firstName.trim(), lastName: raw.lastName.trim(), email: raw.email.trim(), role: this.view() === 'teacher' ? 'Teacher' : 'Parent', studentIds: this.view() === 'parent' ? this.selectedChildren() : [] });
      this.invitation(result); await this.router.navigate(['/portal/admin/accounts', result.account.id]);
    });
  }
  async resend(account: AdminAccount) { await this.action(async () => { this.invitation(await this.api.resend(account)); await this.load(); }); }
  async saveStudent() {
    if (this.form.controls.firstName.invalid || this.form.controls.lastName.invalid) { this.form.markAllAsTouched(); return; }
    await this.action(async () => {
      const raw = this.form.getRawValue(); const old = this.student();
      const saved = await this.api.saveStudent(old?.id ?? null, { firstName: raw.firstName.trim(), lastName: raw.lastName.trim(), teacherUserId: raw.teacherUserId || null, isActive: raw.isActive, parentIds: this.selectedParents(), expectedUpdatedAt: old?.updatedAt ?? null, expectedParentIds: old?.parentIds ?? [] });
      this.api.flash('Student details and access links saved.'); await this.router.navigate(['/portal/admin/students', saved.id]); await this.load();
    });
  }
  async saveChildren() { const a = this.account(); if (!a) return; await this.action(async () => { await this.api.children(a, this.selectedChildren()); this.api.flash('Child links saved.'); await this.load(); }); }
  async setState() { const a = this.account(); if (!a) return; await this.action(async () => {
    await this.api.state(a, !a.isActive); this.api.flash(a.isActive ? 'Account deactivated. Teacher assignments have been cleared; child links and notes are retained.' : 'Account reactivated. Pending accounts need a fresh invitation before setup.'); await this.load();
  }); }
  async showPreview(a: AdminAccount) { await this.action(async () => this.preview.set(await this.api.preview(a.id))); }
}
