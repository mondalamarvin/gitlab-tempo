// Date helpers that work on ISO "yyyy-MM-dd" strings and UTC math, so the browser's
// own time zone never shifts a day.

const DAY_MS = 86_400_000;

export function parseIso(iso: string): Date {
  const [y, m, d] = iso.split("-").map(Number);
  return new Date(Date.UTC(y, m - 1, d));
}

export function toIso(date: Date): string {
  return date.toISOString().slice(0, 10);
}

export function addDays(iso: string, days: number): string {
  return toIso(new Date(parseIso(iso).getTime() + days * DAY_MS));
}

/** Monday of the week containing the date. */
export function mondayOf(iso: string): string {
  const dow = parseIso(iso).getUTCDay(); // 0 = Sunday
  return addDays(iso, -((dow + 6) % 7));
}

/** Today's date in the given IANA zone (defaults to Manila). */
export function todayIn(timeZone = "Asia/Manila"): string {
  return new Intl.DateTimeFormat("en-CA", { timeZone, year: "numeric", month: "2-digit", day: "2-digit" }).format(new Date());
}

const fmt = (iso: string, opts: Intl.DateTimeFormatOptions) =>
  new Intl.DateTimeFormat("en-US", { timeZone: "UTC", ...opts }).format(parseIso(iso));

export const formatDay = (iso: string) => fmt(iso, { weekday: "short" });
export const formatDayNum = (iso: string) => fmt(iso, { month: "short", day: "numeric" });

export function formatRange(from: string, to: string): string {
  const a = parseIso(from);
  const b = parseIso(to);
  if (a.getUTCFullYear() !== b.getUTCFullYear())
    return `${fmt(from, { month: "short", day: "numeric", year: "numeric" })} – ${fmt(to, { month: "short", day: "numeric", year: "numeric" })}`;
  if (a.getUTCMonth() !== b.getUTCMonth())
    return `${fmt(from, { month: "short", day: "numeric" })} – ${fmt(to, { month: "short", day: "numeric" })}, ${b.getUTCFullYear()}`;
  return `${fmt(from, { month: "short", day: "numeric" })} – ${b.getUTCDate()}, ${b.getUTCFullYear()}`;
}

export function formatHours(hours: number, blankZero = false): string {
  if (blankZero && hours === 0) return "–";
  return hours.toFixed(2);
}
