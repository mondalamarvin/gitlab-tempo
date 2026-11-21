import { Clock, Users, Gauge, ListChecks } from "lucide-react";
import { Card } from "@/components/ui/card";
import { formatHours } from "@/lib/dates";
import type { WeeklyReport } from "@/lib/types";

export function SummaryCards({ report }: { report: WeeklyReport }) {
  const t = report.team;
  const items = [
    { icon: Clock, label: report.ownTimeOnly ? "Your total" : "Team total", value: `${formatHours(t.totalHours)} h`, hint: report.weeks.length > 1 ? `${report.weeks.length} weeks` : "this week" },
    {
      icon: Users,
      label: "People with time",
      value: String(t.peopleWithTime),
      hint: report.membersIncluded ? `of ${t.peopleListed} members` : "logged time",
    },
    {
      icon: Gauge,
      label: "Avg per person",
      value: `${formatHours(t.averageHoursPerPerson)} h`,
      hint: report.weeks.length > 1 ? `≈ ${formatHours(t.averageHoursPerPerson / report.weeks.length)} h per week` : "people with time",
    },
    { icon: ListChecks, label: "Issues worked on", value: String(t.issueCount), hint: "across all projects" },
  ];

  return (
    <div className="grid grid-cols-2 gap-3 lg:grid-cols-4">
      {items.map(({ icon: Icon, label, value, hint }) => (
        <Card key={label} className="gap-1 py-4">
          <div className="text-muted-foreground flex items-center gap-2 px-4 text-xs font-medium tracking-wide uppercase">
            <Icon className="size-3.5" />
            {label}
          </div>
          <div className="tabular px-4 text-2xl font-semibold tracking-tight">{value}</div>
          <div className="text-muted-foreground px-4 text-xs">{hint}</div>
        </Card>
      ))}
    </div>
  );
}
