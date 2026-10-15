import { Component, inject } from '@angular/core';
import { MatDialogRef } from '@angular/material/dialog';
import { MatIconModule } from '@angular/material/icon';
import { Router } from '@angular/router';
import { OPEN_DAY } from '../../open-day.config';

@Component({
  selector: 'app-open-day-dialog',
  imports: [MatIconModule],
  templateUrl: './open-day-dialog.html',
  styleUrl: './open-day-dialog.scss',
})
export class OpenDayDialog {
  readonly event = OPEN_DAY;
  private readonly dialogRef = inject(MatDialogRef<OpenDayDialog>);
  private readonly router = inject(Router);

  close() {
    this.dialogRef.close();
  }
  enquire() {
    this.close();
    void this.router.navigate(['/contact'], { queryParams: { enquiry: 'open-day' } });
  }
}
