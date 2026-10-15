import { DOCUMENT } from '@angular/common';
import { DestroyRef, inject, Injectable, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { MatDialog, MatDialogRef } from '@angular/material/dialog';
import { OPEN_DAY, isOpenDayActive, shouldShowOpenDay } from '../open-day.config';
import { OpenDayDialog } from '../components/open-day-dialog/open-day-dialog';

@Injectable({ providedIn: 'root' })
export class OpenDayService {
  readonly event = OPEN_DAY;
  readonly active = signal(isOpenDayActive());
  private readonly document = inject(DOCUMENT);
  private readonly dialog = inject(MatDialog);
  private readonly destroyRef = inject(DestroyRef);
  private readonly storageKey = 'knowledge-vine:' + OPEN_DAY.id + ':dismissed-at';
  private displayTimer?: ReturnType<typeof setTimeout>;
  private expiryTimer?: ReturnType<typeof setTimeout>;
  private dialogRef?: MatDialogRef<OpenDayDialog>;
  private scheduled = false;
  private suppressed = false;

  setSuppressed(suppressed: boolean) {
    this.suppressed = suppressed;
    if (suppressed) { clearTimeout(this.displayTimer); this.dialogRef?.close(); }
    else if (this.scheduled && shouldShowOpenDay(Date.now(), this.readDismissal())) this.deferOpening();
  }

  constructor() {
    this.destroyRef.onDestroy(() => {
      clearTimeout(this.displayTimer);
      clearTimeout(this.expiryTimer);
      this.dialogRef?.close();
    });
  }

  schedule() {
    if (this.scheduled) return;
    this.scheduled = true;
    this.watchExpiry();
    if (!this.suppressed && shouldShowOpenDay(Date.now(), this.readDismissal())) this.deferOpening();
  }

  open() {
    this.active.set(isOpenDayActive());
    if (this.suppressed || !this.active() || this.dialogRef) return;
    clearTimeout(this.displayTimer);
    this.dialogRef = this.dialog.open(OpenDayDialog, {
      width: '960px',
      maxWidth: 'calc(100vw - 32px)',
      maxHeight: 'calc(100dvh - 32px)',
      panelClass: 'open-day-panel',
      backdropClass: 'open-day-backdrop',
      ariaLabelledBy: 'open-day-title',
      ariaDescribedBy: 'open-day-intro',
      autoFocus: '[data-dialog-close]',
      restoreFocus: true,
    });
    this.dialogRef
      .afterClosed()
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe(() => {
        this.dialogRef = undefined;
        try {
          this.document.defaultView?.localStorage.setItem(this.storageKey, String(Date.now()));
        } catch {
          /* The dialog still works when browser storage is unavailable. */
        }
      });
  }

  private readDismissal(): string | null {
    try {
      return this.document.defaultView?.localStorage.getItem(this.storageKey) ?? null;
    } catch {
      return null;
    }
  }

  private deferOpening() {
    this.displayTimer = setTimeout(() => {
      if (this.suppressed) return;
      if (!shouldShowOpenDay(Date.now(), this.readDismissal())) return;
      // Give parents time to finish an enquiry instead of interrupting their input.
      if (
        this.document.activeElement?.matches('input, textarea, select, [contenteditable="true"]')
      ) {
        this.deferOpening();
        return;
      }
      this.open();
    }, OPEN_DAY.delayMs);
  }

  private watchExpiry() {
    this.active.set(isOpenDayActive());
    if (!this.active()) {
      this.dialogRef?.close();
      return;
    }
    this.expiryTimer = setTimeout(
      () => this.watchExpiry(),
      Math.min(Date.parse(OPEN_DAY.endsAt) - Date.now(), 2_147_483_647),
    );
  }
}
