import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { DestroyRef, inject, Injectable, signal } from '@angular/core';
import { Router } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { PortalAuth, PortalRole } from './portal-auth';

export interface AdminAccount { id: string; firstName: string; lastName: string; email: string; roles: PortalRole[]; isActive: boolean; isReady: boolean; version: string; studentIds: string[]; }
export interface AdminStudent { id: string; firstName: string; lastName: string; teacherUserId: string | null; isActive: boolean; updatedAt: string; parentIds: string[]; }
export interface AdminDirectory { accounts: AdminAccount[]; students: AdminStudent[]; invitationPreview: boolean; }
export interface InvitationResult { account: AdminAccount; delivery: 'sent' | 'preview' | 'failed'; message: string; }
export interface InvitationPreview { text: string; setupUrl: string; }
export interface StudentInput { firstName: string; lastName: string; teacherUserId: string | null; isActive: boolean; parentIds: string[]; expectedUpdatedAt: string | null; expectedParentIds: string[]; }

@Injectable({ providedIn: 'root' })
export class PortalAdminApi {
  private readonly http = inject(HttpClient);
  private readonly auth = inject(PortalAuth);
  private readonly router = inject(Router);
  private readonly base = '/api/portal/admin';
  readonly notice = signal<{ message: string; error: boolean } | null>(null);
  private noticeTimer?: ReturnType<typeof setTimeout>;
  constructor() { inject(DestroyRef).onDestroy(() => clearTimeout(this.noticeTimer)); }
  directory() { return this.request(() => firstValueFrom(this.http.get<AdminDirectory>(`${this.base}/directory`))); }
  createAccount(input: { firstName: string; lastName: string; email: string; role: 'Parent' | 'Teacher'; studentIds: string[] }) {
    return this.mutate(() => firstValueFrom(this.http.post<InvitationResult>(`${this.base}/accounts`, input)));
  }
  resend(account: AdminAccount) { return this.mutate(() => firstValueFrom(this.http.post<InvitationResult>(`${this.base}/accounts/${account.id}/resend`, { version: account.version }))); }
  state(account: AdminAccount, isActive: boolean) { return this.mutate(() => firstValueFrom(this.http.put<AdminAccount>(`${this.base}/accounts/${account.id}/state`, { version: account.version, isActive }))); }
  children(account: AdminAccount, studentIds: string[]) { return this.mutate(() => firstValueFrom(this.http.put<AdminAccount>(`${this.base}/accounts/${account.id}/children`, { version: account.version, studentIds, expectedStudentIds: account.studentIds }))); }
  saveStudent(id: string | null, input: StudentInput) { return this.mutate(() => firstValueFrom(id
    ? this.http.put<AdminStudent>(`${this.base}/students/${id}`, input)
    : this.http.post<AdminStudent>(`${this.base}/students`, input))); }
  preview(id: string) { return this.request(() => firstValueFrom(this.http.get<InvitationPreview>(`${this.base}/accounts/${id}/invitation-preview`))); }
  flash(message: string, error = false) {
    clearTimeout(this.noticeTimer); this.notice.set({ message, error });
    if (!error) this.noticeTimer = setTimeout(() => this.notice.set(null), 10000);
  }
  private async mutate<T>(action: () => Promise<T>) { return this.request(async () => { await this.auth.prepareMutation(); return action(); }); }
  private async request<T>(action: () => Promise<T>): Promise<T> {
    try { return await action(); }
    catch (error) {
      if (error instanceof HttpErrorResponse && error.status === 401) {
        await this.auth.loadSession().catch(() => null);
        this.auth.expired.set(true);
        await this.router.navigateByUrl('/portal/login');
      }
      throw error;
    }
  }
}
