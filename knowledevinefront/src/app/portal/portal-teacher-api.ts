import { HttpClient, HttpErrorResponse, HttpParams } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { PortalAuth } from './portal-auth';

export interface TeacherStudent { id: string; firstName: string; lastName: string; status: 'not_started' | 'draft' | 'shared'; version: number | null; }
export interface TeacherNote { id: string; noteText: string; status: 'draft' | 'shared'; version: number; authorName: string; editorName: string; createdAt: string; updatedAt: string; sharedAt: string | null; }
export interface TeacherWorklist { today: string; date: string; students: TeacherStudent[]; }
export interface TeacherEditor { today: string; date: string; student: TeacherStudent; note: TeacherNote | null; }
export interface TeacherNoteInput { updateDate: string; noteText: string; share: boolean; expectedVersion: number | null; }

@Injectable({ providedIn: 'root' })
export class PortalTeacherApi {
  private readonly http = inject(HttpClient);
  private readonly auth = inject(PortalAuth);
  private readonly base = '/api/portal/teacher/students';
  worklist(date?: string) { return this.request(() => firstValueFrom(this.http.get<TeacherWorklist>(this.base, { params: date ? new HttpParams().set('date', date) : undefined }))); }
  editor(id: string, date?: string) { return this.request(() => firstValueFrom(this.http.get<TeacherEditor>(`${this.base}/${id}/note`, { params: date ? new HttpParams().set('date', date) : undefined }))); }
  save(id: string, input: TeacherNoteInput) {
    return this.request(async () => {
      await this.auth.prepareMutation();
      return firstValueFrom(this.http.put<TeacherEditor>(`${this.base}/${id}/note`, input));
    });
  }
  private async request<T>(action: () => Promise<T>): Promise<T> {
    try { return await action(); }
    catch (error) {
      if (error instanceof HttpErrorResponse && error.status === 401) this.auth.requireSignIn();
      throw error;
    }
  }
}
