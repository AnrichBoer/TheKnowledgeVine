import { OPEN_DAY, isOpenDayActive, shouldShowOpenDay } from './open-day.config';

describe('Open day display rules', () => {
  const now = Date.parse('2026-10-09T10:00:00+02:00');
  const day = 24 * 60 * 60 * 1000;
  it('shows to a first-time visitor', () => expect(shouldShowOpenDay(now, null)).toBeTrue());
  it('respects a recent dismissal', () =>
    expect(shouldShowOpenDay(now, String(now - 6 * day))).toBeFalse());
  it('can show again after seven days', () =>
    expect(shouldShowOpenDay(now, String(now - 7 * day))).toBeTrue());
  it('ignores corrupt dismissal values', () =>
    expect(shouldShowOpenDay(now, 'invalid')).toBeTrue());
  it('recovers from a browser clock correction', () =>
    expect(shouldShowOpenDay(now, String(now + day))).toBeTrue());
  it('remains available during the open day', () =>
    expect(isOpenDayActive(Date.parse('2026-10-31T12:59:59+02:00'))).toBeTrue());
  it('expires at 13:00 SAST, independent of the browser timezone', () => {
    expect(isOpenDayActive(Date.parse('2026-10-31T11:00:00Z'))).toBeFalse();
    expect(shouldShowOpenDay(Date.parse(OPEN_DAY.endsAt), null)).toBeFalse();
  });
});
