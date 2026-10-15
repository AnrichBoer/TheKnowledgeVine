export const OPEN_DAY = {
  id: 'open-day-2026-10-31',
  title: 'Open Day',
  dateLabel: 'Saturday, 31 October 2026',
  shortDate: '31 October 2026',
  timeLabel: '10:00–13:00',
  timezoneLabel: 'South African time',
  startsAt: '2026-10-31T10:00:00+02:00',
  endsAt: '2026-10-31T13:00:00+02:00',
  address: '2 Geelvink Street, Atlasville, Boksburg',
  directionsUrl:
    'https://www.google.com/maps/search/?api=1&query=2%20Geelvink%20Street%2C%20Atlasville%2C%20Boksburg',
  artwork: 'assets/images/open-day-invitation.webp',
  enquiryMessage:
    'Hello! I would like to find out more about the Knowledge Vine Montessori open day on 31 October 2026, from 10:00 to 13:00.',
  delayMs: 1800,
  dismissalDays: 7,
} as const;

export function isOpenDayActive(now = Date.now()): boolean {
  return now < Date.parse(OPEN_DAY.endsAt);
}

export function shouldShowOpenDay(now: number, dismissedAt: string | null): boolean {
  if (!isOpenDayActive(now)) return false;
  if (dismissedAt === null) return true;
  const timestamp = Number(dismissedAt);
  if (!Number.isFinite(timestamp) || timestamp <= 0 || timestamp > now) return true;
  return now - timestamp >= OPEN_DAY.dismissalDays * 24 * 60 * 60 * 1000;
}
