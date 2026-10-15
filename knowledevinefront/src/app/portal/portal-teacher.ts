import { DatePipe } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import { afterNextRender, Component, computed, DestroyRef, ElementRef, HostListener, inject, Injector, signal, viewChild } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { combineLatest } from 'rxjs';
import { PortalAuth, portalError } from './portal-auth';
import { PortalNoteMemory } from './portal-note-memory';
import { PortalTeacherApi, TeacherEditor, TeacherNote, TeacherStudent } from './portal-teacher-api';
import { PortalConnection } from './portal-connection';
import { PortalReloadSafety } from './portal-reload-safety';

@Component({ selector: 'app-portal-teacher', imports: [RouterLink, DatePipe], templateUrl: './portal-teacher.html' })
export class PortalTeacher {
  readonly auth = inject(PortalAuth);
  readonly notes = inject(PortalNoteMemory);
  readonly connection = inject(PortalConnection);
  private readonly api = inject(PortalTeacherApi);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly destroy = inject(DestroyRef);
  private readonly injector = inject(Injector);
  private readonly shareButton = viewChild<ElementRef<HTMLButtonElement>>('confirmButton');
  private sequence = 0;
  private readonly ownerId = this.auth.session()?.id;
  readonly loading = signal(true); readonly busy = signal(false); readonly error = signal(''); readonly notice = signal('');
  readonly today = signal(''); readonly date = signal(''); readonly studentId = signal('');
  readonly students = signal<TeacherStudent[]>([]); readonly editor = signal<TeacherEditor | null>(null);
  readonly text = signal(''); readonly baseline = signal(''); readonly expectedVersion = signal<number | null>(null);
  readonly search = signal(''); readonly confirmShare = signal(false); readonly conflict = signal(false);
  readonly latest = signal<TeacherEditor | null>(null); readonly accessLost = signal(false);
  readonly recoveryOnly = signal('');
  readonly dirty = computed(() => this.text() !== this.baseline());
  readonly shared = computed(() => this.editor()?.note?.status === 'shared');
  readonly visibleStudents = computed(() => this.students().filter(s => this.name(s).toLowerCase().includes(this.search().trim().toLowerCase())));
  readonly sharedCount = computed(() => this.students().filter(s => s.status === 'shared').length);
  readonly draftCount = computed(() => this.students().filter(s => s.status === 'draft').length);
  readonly validNote = computed(() => this.text().length <= 2000 && (!this.shared() || !!this.text().trim()));
  constructor() {
    inject(PortalReloadSafety).protect(() => this.busy() || this.dirty() || !!this.recoveryOnly(), this.destroy);
    combineLatest([this.route.paramMap, this.route.queryParamMap]).pipe(takeUntilDestroyed(this.destroy)).subscribe(() => void this.load());
    this.destroy.onDestroy(() => this.sequence++);
  }
  name(student: TeacherStudent) { return `${student.firstName} ${student.lastName}`; }
  label(status: string) { return status === 'shared' ? 'Shared' : status === 'draft' ? 'Draft' : 'Not started'; }
  edited(note: TeacherNote) { return !!note.sharedAt && Date.parse(note.updatedAt) > Date.parse(note.sharedAt); }
  requestShare() {
    if (this.connection.disconnected()) { this.error.set('Connect to the portal before sharing. Nothing has been sent.'); return; }
    if (this.busy() || this.conflict() || this.accessLost() || !this.validNote() || !this.text().trim()) return;
    this.confirmShare.set(true);
    afterNextRender(() => this.shareButton()?.nativeElement.focus(), { injector: this.injector });
  }

  async load() {
    const sequence = ++this.sequence;
    const id = this.route.snapshot.paramMap.get('id') ?? '';
    const date = this.route.snapshot.queryParamMap.get('date') ?? undefined;
    this.loading.set(true); this.error.set(''); this.notice.set(''); this.latest.set(null); this.conflict.set(false);
    this.confirmShare.set(false); this.accessLost.set(false); this.recoveryOnly.set(''); this.studentId.set(id);
    try {
      if (id) {
        const data = await this.api.editor(id, date); if (sequence !== this.sequence) return;
        this.accept(data);
        const held = this.notes.pending();
        if (held && held.ownerId === this.auth.session()?.id && held.studentId === id && held.date === data.date) {
          this.text.set(held.text); this.baseline.set(held.baseline); this.expectedVersion.set(held.expectedVersion);
          if ((data.note?.version ?? null) !== held.expectedVersion) { this.conflict.set(true); this.latest.set(data); }
          this.notice.set('Your unsaved text has been restored in this tab. Review it before saving.');
        } else if (held) this.notes.clear();
      } else {
        const data = await this.api.worklist(date); if (sequence !== this.sequence) return;
        this.students.set(data.students); this.today.set(data.today); this.date.set(data.date); this.editor.set(null);
      }
    } catch (error) {
      if (sequence === this.sequence) {
        this.error.set(portalError(error));
        const held = this.notes.pending();
        if (error instanceof HttpErrorResponse && (error.status === 404 || error.status === 403)
          && held?.ownerId === this.ownerId && held?.studentId === id) {
          this.recoveryOnly.set(held.text); this.accessLost.set(true);
        }
      }
    }
    finally { if (sequence === this.sequence) this.loading.set(false); }
  }
  private accept(data: TeacherEditor) {
    this.editor.set(data); this.today.set(data.today); this.date.set(data.date);
    this.text.set(data.note?.noteText ?? ''); this.baseline.set(data.note?.noteText ?? '');
    this.expectedVersion.set(data.note?.version ?? null);
  }
  input(value: string) {
    this.text.set(value); this.confirmShare.set(false); this.notice.set(''); this.remember();
  }
  private remember() {
    const ownerId = this.ownerId;
    if (!ownerId || !this.studentId() || !this.editor()) return;
    if (!this.dirty()) { this.notes.clear(); return; }
    this.notes.pending.set({ ownerId, studentId: this.studentId(), date: this.date(), text: this.text(),
      expectedVersion: this.expectedVersion(), baseline: this.baseline() });
  }
  async save(share: boolean) {
    if (this.busy() || !this.editor() || this.accessLost() || this.conflict() || !this.validNote() || (share && !this.text().trim())) return;
    if (this.connection.disconnected()) { this.remember(); this.error.set('Connect to the portal before saving. Your unsaved text is kept in this tab.'); return; }
    this.remember(); this.busy.set(true); this.error.set(''); this.notice.set(''); this.confirmShare.set(false);
    const wasShared = this.shared();
    try {
      const saved = await this.api.save(this.studentId(), { updateDate: this.date(), noteText: this.text(), share, expectedVersion: this.expectedVersion() });
      if (this.auth.expired() || this.auth.session()?.id !== this.ownerId) return;
      this.accept(saved); this.notes.clear(); this.latest.set(null);
      this.notice.set(wasShared ? 'Correction saved. This note remains shared with parents.' : share ? 'Note shared with parents.' : 'Draft saved. It is private to the school.');
    } catch (error) {
      this.error.set(portalError(error));
      if (error instanceof HttpErrorResponse) {
        this.conflict.set(error.status === 409); this.accessLost.set(error.status === 404 || error.status === 403);
      }
    } finally { this.busy.set(false); }
  }
  async compareLatest() {
    if (this.busy()) return;
    this.busy.set(true); this.error.set('');
    try { this.latest.set(await this.api.editor(this.studentId(), this.date())); }
    catch (error) {
      this.error.set(portalError(error));
      if (error instanceof HttpErrorResponse && (error.status === 404 || error.status === 403)) this.accessLost.set(true);
    } finally { this.busy.set(false); }
  }
  resolve(useMine: boolean) {
    const latest = this.latest(); if (!latest || this.busy()) return;
    if (!useMine && !window.confirm('Replace your unsaved text with the latest saved note?')) return;
    const mine = this.text(); this.accept(latest);
    if (useMine) this.text.set(mine);
    this.latest.set(null); this.conflict.set(false); this.error.set(''); this.remember();
    this.notice.set(useMine ? 'Your text is ready for review against the latest version. Save when you are happy with it.' : 'Latest saved note loaded.');
  }
  async changeDate(value: string) {
    if (!value || value === this.date() || this.busy()) return;
    if (!/^\d{4}-\d{2}-\d{2}$/.test(value) || value > this.today()) { this.error.set('Choose today or an earlier school date.'); return; }
    if (!this.mayLeave()) return;
    await this.router.navigate([], { relativeTo: this.route, queryParams: { date: value } });
  }
  async selectDate(event: Event) {
    const input = event.target as HTMLInputElement;
    const value = input.value; input.value = this.date();
    await this.changeDate(value);
  }
  mayLeave(): boolean {
    if (this.auth.expired()) { this.remember(); return true; }
    if (!this.auth.session()) return true;
    if (this.busy()) return false;
    if ((this.dirty() || this.recoveryOnly()) && !window.confirm('Leave this note? Your unsaved changes will be discarded.')) return false;
    this.notes.clear(); return true;
  }
  async logout() {
    if (!this.mayLeave()) return;
    this.busy.set(true); this.error.set('');
    try { await this.auth.logout(); } catch (error) { this.remember(); this.error.set(portalError(error)); }
    finally { this.busy.set(false); }
  }
  @HostListener('window:beforeunload', ['$event']) beforeUnload(event: BeforeUnloadEvent) {
    if (this.dirty() || this.recoveryOnly()) { event.preventDefault(); event.returnValue = ''; }
  }
}
