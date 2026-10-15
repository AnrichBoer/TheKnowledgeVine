import { DOCUMENT } from '@angular/common';
import { TestBed } from '@angular/core/testing';
import { provideZonelessChangeDetection } from '@angular/core';
import { MatDialog } from '@angular/material/dialog';
import { Subject } from 'rxjs';
import { OPEN_DAY } from '../open-day.config';
import { OpenDayService } from './open-day';

describe('OpenDayService', () => {
  let service: OpenDayService;
  let dialog: jasmine.SpyObj<MatDialog>;
  let storage: jasmine.SpyObj<Storage>;
  let editing: jasmine.Spy;
  let closed: Subject<void>;
  let close: jasmine.Spy;
  beforeEach(() => {
    jasmine.clock().install();
    jasmine.clock().mockDate(new Date('2026-10-09T10:00:00+02:00'));
    dialog = jasmine.createSpyObj<MatDialog>('MatDialog', ['open']);
    storage = jasmine.createSpyObj<Storage>('Storage', ['getItem', 'setItem']);
    storage.getItem.and.returnValue(null);
    editing = jasmine.createSpy('matches').and.returnValue(false);
    closed = new Subject<void>();
    close = jasmine.createSpy('close');
    dialog.open.and.returnValue({ afterClosed: () => closed, close } as any);
    TestBed.configureTestingModule({
      providers: [
        provideZonelessChangeDetection(),
        { provide: MatDialog, useValue: dialog },
        {
          provide: DOCUMENT,
          useValue: { activeElement: { matches: editing }, defaultView: { localStorage: storage } },
        },
      ],
    });
    service = TestBed.inject(OpenDayService);
  });
  afterEach(() => {
    TestBed.resetTestingModule();
    jasmine.clock().uninstall();
  });
  it('schedules the popup only once', () => {
    service.schedule();
    service.schedule();
    jasmine.clock().tick(OPEN_DAY.delayMs);
    expect(dialog.open).toHaveBeenCalledTimes(1);
  });
  it('does not interrupt a parent entering a message', () => {
    editing.and.returnValue(true);
    service.schedule();
    jasmine.clock().tick(OPEN_DAY.delayMs);
    expect(dialog.open).not.toHaveBeenCalled();
    editing.and.returnValue(false);
    jasmine.clock().tick(OPEN_DAY.delayMs);
    expect(dialog.open).toHaveBeenCalledTimes(1);
  });
  it('remembers dismissal but allows deliberate reopening', () => {
    service.open();
    closed.next();
    expect(storage.setItem).toHaveBeenCalledWith(
      'knowledge-vine:' + OPEN_DAY.id + ':dismissed-at',
      String(Date.now()),
    );
    service.open();
    expect(dialog.open).toHaveBeenCalledTimes(2);
  });
  it('respects a stored dismissal on another visit', () => {
    storage.getItem.and.returnValue(String(Date.now()));
    service.schedule();
    jasmine.clock().tick(OPEN_DAY.delayMs);
    expect(dialog.open).not.toHaveBeenCalled();
  });
  it('works when browser storage is blocked', () => {
    storage.getItem.and.throwError('Storage blocked');
    storage.setItem.and.throwError('Storage blocked');
    service.schedule();
    jasmine.clock().tick(OPEN_DAY.delayMs);
    expect(dialog.open).toHaveBeenCalledTimes(1);
    expect(() => closed.next()).not.toThrow();
  });
  it('prevents two dialogs opening together', () => {
    service.open();
    service.open();
    expect(dialog.open).toHaveBeenCalledTimes(1);
  });
  it('removes the promotion and closes a dialog when the event ends', () => {
    jasmine.clock().mockDate(new Date(Date.parse(OPEN_DAY.endsAt) - 1000));
    service.schedule();
    service.open();
    jasmine.clock().tick(1000);
    expect(service.active()).toBeFalse();
    expect(close).toHaveBeenCalled();
  });
  it('never opens an expired event', () => {
    jasmine.clock().mockDate(new Date(OPEN_DAY.endsAt));
    service.schedule();
    service.open();
    expect(dialog.open).not.toHaveBeenCalled();
    expect(service.active()).toBeFalse();
  });
  it('suppresses promotion while visiting the portal and resumes on the public website', () => {
    service.setSuppressed(true); service.schedule();
    jasmine.clock().tick(OPEN_DAY.delayMs); service.open();
    expect(dialog.open).not.toHaveBeenCalled();
    service.setSuppressed(false); jasmine.clock().tick(OPEN_DAY.delayMs);
    expect(dialog.open).toHaveBeenCalledTimes(1);
  });
});
