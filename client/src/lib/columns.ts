import type { WeeklyReport } from "./types";
import { formatDay, formatDayNum } from "./dates";

/** Columns shown in the summary table: days for a single week, weeks otherwise. */
export function reportColumns(report: WeeklyReport) {
  if (report.weeks.length === 1)
    return report.days.map((d) => ({ key: d, title: formatDay(d), sub: formatDayNum(d), kind: "day" as const }));
  return report.weeks.map((w) => ({ key: w.key, title: `Week of ${formatDayNum(w.start)}`, sub: w.label, kind: "week" as const }));
}
