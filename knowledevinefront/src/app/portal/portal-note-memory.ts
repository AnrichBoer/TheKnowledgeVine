import { Injectable, signal } from '@angular/core';

export interface UnsavedTeacherNote {
  ownerId: string; studentId: string; date: string; text: string;
  expectedVersion: number | null; baseline: string;
}

// Deliberately memory-only: no localStorage, sessionStorage, IndexedDB or offline cache.
// A refresh/closed tab loses this recovery copy; Save draft persists work to the school.
@Injectable({ providedIn: 'root' })
export class PortalNoteMemory {
  readonly pending = signal<UnsavedTeacherNote | null>(null);
  clear() { this.pending.set(null); }
  resumeUrl(userId: string, roles: readonly string[]): string | null {
    const note = this.pending();
    if (!note) return null;
    if (note.ownerId !== userId || !roles.includes('Teacher')) { this.clear(); return null; }
    return `/portal/teacher/students/${encodeURIComponent(note.studentId)}?date=${encodeURIComponent(note.date)}`;
  }
}
