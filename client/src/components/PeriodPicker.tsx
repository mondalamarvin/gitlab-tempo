import { ChevronLeft, ChevronRight, CalendarDays } from "lucide-react";
import { Button } from "@/components/ui/button";
import { addDays, formatRange, mondayOf, todayIn } from "@/lib/dates";
import { cn } from "@/lib/utils";
import { useAppDispatch, useAppSelector } from "@/store/store";
import { periodChanged } from "@/store/settingsSlice";

const PRESETS = [
  { label: "This week", weeksBack: 0, weeks: 1 },
  { label: "Last week", weeksBack: 1, weeks: 1 },
  { label: "Last 4 weeks", weeksBack: 3, weeks: 4 },
  { label: "Last 8 weeks", weeksBack: 7, weeks: 8 },
];

export function PeriodPicker() {
  const dispatch = useAppDispatch();
  const { from, weeks } = useAppSelector((s) => s.settings);
  const thisMonday = mondayOf(todayIn());
  const to = addDays(from, weeks * 7 - 3); // Friday of the last week

  const set = (nextFrom: string, nextWeeks = weeks) => dispatch(periodChanged({ from: nextFrom, weeks: nextWeeks }));

  return (
    <div className="flex flex-wrap items-center gap-2">
      <div className="bg-muted flex items-center rounded-lg p-1">
        {PRESETS.map((p) => {
          const presetFrom = addDays(thisMonday, -7 * p.weeksBack);
          const active = presetFrom === from && p.weeks === weeks;
          return (
            <button
              key={p.label}
              type="button"
              onClick={() => set(presetFrom, p.weeks)}
              className={cn(
                "rounded-md px-3 py-1.5 text-sm font-medium transition-colors",
                active ? "bg-background text-foreground shadow-sm" : "text-muted-foreground hover:text-foreground",
              )}
            >
              {p.label}
            </button>
          );
        })}
      </div>

      <div className="flex items-center gap-1">
        <Button variant="outline" size="icon" aria-label="Previous period" onClick={() => set(addDays(from, -7 * weeks))}>
          <ChevronLeft />
        </Button>
        <label className="border-input relative flex h-9 cursor-pointer items-center gap-2 rounded-md border px-3 text-sm font-medium shadow-xs">
          <CalendarDays className="text-muted-foreground size-4" />
          <span className="tabular">{formatRange(from, to)}</span>
          <input
            type="date"
            aria-label="Pick a week"
            className="absolute inset-0 cursor-pointer opacity-0"
            value={from}
            onChange={(e) => e.target.value && set(e.target.value)}
          />
        </label>
        <Button
          variant="outline"
          size="icon"
          aria-label="Next period"
          disabled={addDays(from, 7 * weeks) > thisMonday}
          onClick={() => set(addDays(from, 7 * weeks))}
        >
          <ChevronRight />
        </Button>
      </div>
    </div>
  );
}
