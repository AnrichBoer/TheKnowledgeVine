import { DestroyRef, inject, Injectable, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { Observable } from 'rxjs';
import { PortalNoteMemory } from './portal-note-memory';

@Injectable({ providedIn: 'root' })
export class PortalReloadSafety {
  private readonly notes = inject(PortalNoteMemory);
  private readonly blockers = new Set<() => boolean>();
  private readonly revision = signal(0);
  readonly writes = signal(0);
  protect(blocked: () => boolean, destroy: DestroyRef, changes?: Observable<unknown>) {
    this.blockers.add(blocked); this.revision.update(n => n + 1);
    changes?.pipe(takeUntilDestroyed(destroy)).subscribe(() => this.revision.update(n => n + 1));
    destroy.onDestroy(() => { this.blockers.delete(blocked); this.revision.update(n => n + 1); });
  }
  canReload() {
    this.revision();
    return this.writes() === 0 && !this.notes.pending() && ![...this.blockers].some(blocked => blocked());
  }
}
