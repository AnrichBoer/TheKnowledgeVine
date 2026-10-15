import { HttpClient, HttpErrorResponse, HttpParams } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { PortalAuth } from './portal-auth';

export interface ParentChild { id: string; firstName: string; lastName: string; }
export interface ParentWindow { year: number; serverTime: string; nextYearAt: string; }
export interface ParentChildren extends ParentWindow { children: ParentChild[]; }
export interface ParentNote { id: string; updateDate: string; noteText: string; authorName: string; editorName: string; sharedAt: string; updatedAt: string; isEdited: boolean; }
export interface ParentFeed extends ParentWindow { child: ParentChild; notes: ParentNote[]; nextBefore: string | null; }

@Injectable({ providedIn: 'root' })
export class PortalParentApi {
  private readonly http = inject(HttpClient);
  private readonly auth = inject(PortalAuth);
  private readonly base = '/api/portal/parent/children';
  children() { return this.request(() => firstValueFrom(this.http.get<ParentChildren>(this.base))); }
  feed(child: string, before?: string) {
    return this.request(() => firstValueFrom(this.http.get<ParentFeed>(`${this.base}/${child}/updates`,
      { params: before ? new HttpParams().set('before', before) : undefined })));
  }
  private async request<T>(action: () => Promise<T>): Promise<T> {
    try { return await action(); }
    catch (error) {
      if (error instanceof HttpErrorResponse && error.status === 401) this.auth.requireSignIn();
      throw error;
    }
  }
}
