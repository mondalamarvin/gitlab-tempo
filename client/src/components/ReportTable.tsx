import { Fragment, useState } from "react";
import { ChevronRight, ExternalLink } from "lucide-react";
import { Table, TableBody, TableCell, TableFooter, TableHead, TableHeader, TableRow } from "@/components/ui/table";
import { formatHours } from "@/lib/dates";
import { reportColumns } from "@/lib/columns";
import type { PersonReport, WeeklyReport } from "@/lib/types";
import { cn } from "@/lib/utils";

function Avatar({ person }: { person: PersonReport }) {
  const initials = person.name
    .split(/\s+/)
    .slice(0, 2)
    .map((p) => p[0]?.toUpperCase())
    .join("");
  return person.avatarUrl ? (
    <img src={person.avatarUrl} alt="" className="bg-muted size-7 rounded-full object-cover" loading="lazy" />
  ) : (
    <span className="bg-muted text-muted-foreground grid size-7 place-items-center rounded-full text-[11px] font-semibold">{initials}</span>
  );
}

export function ReportTable({ report, showEmpty }: { report: WeeklyReport; showEmpty: boolean }) {
  const [open, setOpen] = useState<Set<string>>(new Set());
  const columns = reportColumns(report);
  const people = report.people.filter((p) => showEmpty || p.totalHours !== 0);
  const max = Math.max(1, ...people.map((p) => p.totalHours));
  const value = (src: { dayHours: Record<string, number>; weekHours: Record<string, number> }, key: string, kind: "day" | "week") =>
    (kind === "day" ? src.dayHours[key] : src.weekHours[key]) ?? 0;

  const toggle = (username: string) =>
    setOpen((prev) => {
      const next = new Set(prev);
      if (next.has(username)) next.delete(username);
      else next.add(username);
      return next;
    });

  if (people.length === 0)
    return <div className="text-muted-foreground py-16 text-center text-sm">No time was logged on issues in this period.</div>;

  return (
    <Table>
      <TableHeader>
        <TableRow className="hover:bg-transparent">
          <TableHead className="pl-4">Person</TableHead>
          {columns.map((c) => (
            <TableHead key={c.key} className="text-right">
              <div className="leading-tight">
                <div className="text-foreground">{c.title}</div>
                <div className="text-xs font-normal">{c.sub}</div>
              </div>
            </TableHead>
          ))}
          <TableHead className="w-48 pr-4 text-right">Total</TableHead>
        </TableRow>
      </TableHeader>

      <TableBody>
        {people.map((p) => {
          const isOpen = open.has(p.username);
          const empty = p.totalHours === 0;
          return (
            <Fragment key={p.username}>
              <TableRow
                className={cn("cursor-pointer", empty && "text-muted-foreground", isOpen && "bg-muted/40")}
                onClick={() => !empty && toggle(p.username)}
                aria-expanded={isOpen}
              >
                <TableCell className="pl-4">
                  <div className="flex items-center gap-3">
                    <ChevronRight className={cn("text-muted-foreground size-4 transition-transform", isOpen && "rotate-90", empty && "invisible")} />
                    <Avatar person={p} />
                    <div className="leading-tight">
                      <div className="text-foreground font-medium">{p.name}</div>
                      <div className="text-muted-foreground text-xs">@{p.username}</div>
                    </div>
                  </div>
                </TableCell>
                {columns.map((c) => {
                  const h = value(p, c.key, c.kind);
                  return (
                    <TableCell key={c.key} className={cn("tabular text-right", h === 0 && "text-muted-foreground/60")}>
                      {formatHours(h, true)}
                    </TableCell>
                  );
                })}
                <TableCell className="pr-4">
                  <div className="flex items-center justify-end gap-3">
                    <div className="bg-muted hidden h-1.5 w-20 overflow-hidden rounded-full sm:block">
                      <div className="bg-foreground/70 h-full rounded-full" style={{ width: `${Math.max(0, (p.totalHours / max) * 100)}%` }} />
                    </div>
                    <span className="tabular w-14 text-right font-semibold">{formatHours(p.totalHours)}</span>
                  </div>
                </TableCell>
              </TableRow>

              {isOpen && (
                <TableRow className="bg-muted/40 hover:bg-muted/40">
                  <TableCell colSpan={columns.length + 2} className="px-4 pt-0 pb-4 pl-[4.25rem] whitespace-normal">
                    <ul className="divide-border bg-background divide-y rounded-md border">
                      {p.issues.map((i) => (
                        <li key={`${i.projectPath}#${i.iid}`} className="flex items-center gap-3 px-3 py-2 text-sm">
                          <span className="text-muted-foreground w-40 shrink-0 truncate text-xs">{i.projectPath}</span>
                          <a
                            href={i.webUrl ?? undefined}
                            target="_blank"
                            rel="noreferrer"
                            className="group flex min-w-0 flex-1 items-center gap-1.5 hover:underline"
                            onClick={(e) => e.stopPropagation()}
                          >
                            <span className="text-muted-foreground shrink-0">#{i.iid}</span>
                            <span className="truncate">{i.title}</span>
                            <ExternalLink className="size-3 shrink-0 opacity-0 group-hover:opacity-60" />
                          </a>
                          <span className="text-muted-foreground shrink-0 text-xs">
                            {i.entries} {i.entries === 1 ? "entry" : "entries"}
                          </span>
                          <span className="tabular w-14 shrink-0 text-right font-medium">{formatHours(i.hours)}</span>
                        </li>
                      ))}
                    </ul>
                  </TableCell>
                </TableRow>
              )}
            </Fragment>
          );
        })}
      </TableBody>

      <TableFooter>
        <TableRow className="hover:bg-transparent">
          <TableCell className="pl-4 font-semibold">Team total</TableCell>
          {columns.map((c) => (
            <TableCell key={c.key} className="tabular text-right font-semibold">
              {formatHours(value(report.team, c.key, c.kind))}
            </TableCell>
          ))}
          <TableCell className="tabular pr-4 text-right text-base font-semibold">{formatHours(report.team.totalHours)}</TableCell>
        </TableRow>
      </TableFooter>
    </Table>
  );
}
