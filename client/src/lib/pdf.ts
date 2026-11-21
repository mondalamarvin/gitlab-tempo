import { jsPDF } from "jspdf";
import autoTable from "jspdf-autotable";
import type { WeeklyReport } from "./types";
import { formatHours, formatRange } from "./dates";
import { reportColumns } from "./columns";

export interface PdfOptions {
  includeIssues: boolean;
  includeEmptyMembers: boolean;
}

type Rgb = [number, number, number];
const INK: Rgb = [23, 23, 23];
const MUTED: Rgb = [115, 115, 115];
const LINE: Rgb = [229, 229, 229];
const HEAD_FILL: Rgb = [245, 245, 245];

export function exportReportPdf(report: WeeklyReport, options: PdfOptions) {
  const columns = reportColumns(report);
  const landscape = columns.length > 6;
  const doc = new jsPDF({ orientation: landscape ? "landscape" : "portrait", unit: "pt", format: "a4" });
  const pageWidth = doc.internal.pageSize.getWidth();
  const margin = 40;

  // Header
  doc.setTextColor(...INK);
  doc.setFont("helvetica", "bold");
  doc.setFontSize(18);
  doc.text("Weekly Time Report", margin, 56);

  doc.setFont("helvetica", "normal");
  doc.setFontSize(11);
  doc.setTextColor(...MUTED);
  doc.text(`${report.group.name} (${report.group.fullPath})`, margin, 74);
  doc.text(`${formatRange(report.from, report.to)}  ·  Mon–Fri  ·  ${report.timeZone}  ·  Issues only`, margin, 90);

  // Summary strip
  const t = report.team;
  const stats: [string, string][] = [
    ["Team total", `${formatHours(t.totalHours)} h`],
    ["People with time", `${t.peopleWithTime}${report.membersIncluded ? ` of ${t.peopleListed}` : ""}`],
    ["Avg per person", `${formatHours(t.averageHoursPerPerson)} h`],
    ["Issues worked on", String(t.issueCount)],
  ];
  const statWidth = (pageWidth - margin * 2) / stats.length;
  const statTop = 108;
  doc.setDrawColor(...LINE);
  doc.roundedRect(margin, statTop, pageWidth - margin * 2, 48, 4, 4, "S");
  stats.forEach(([label, value], i) => {
    const x = margin + statWidth * i + 14;
    doc.setFontSize(8.5);
    doc.setTextColor(...MUTED);
    doc.text(label.toUpperCase(), x, statTop + 18);
    doc.setFontSize(14);
    doc.setFont("helvetica", "bold");
    doc.setTextColor(...INK);
    doc.text(value, x, statTop + 37);
    doc.setFont("helvetica", "normal");
  });

  // Hours table
  const people = report.people.filter((p) => options.includeEmptyMembers || p.totalHours !== 0);
  const valueOf = (map: { dayHours: Record<string, number>; weekHours: Record<string, number> }, c: (typeof columns)[number]) =>
    (c.kind === "day" ? map.dayHours[c.key] : map.weekHours[c.key]) ?? 0;

  autoTable(doc, {
    startY: statTop + 66,
    margin: { left: margin, right: margin },
    head: [["Person", ...columns.map((c) => (c.kind === "day" ? `${c.title}\n${c.sub}` : `${c.title}`)), "Total (h)"]],
    body: people.map((p) => [p.name, ...columns.map((c) => formatHours(valueOf(p, c), true)), formatHours(p.totalHours)]),
    foot: [["Team total", ...columns.map((c) => formatHours(valueOf(t, c))), formatHours(t.totalHours)]],
    theme: "plain",
    styles: { font: "helvetica", fontSize: 9.5, textColor: INK, cellPadding: { top: 6, bottom: 6, left: 6, right: 6 }, lineColor: LINE },
    headStyles: { fillColor: HEAD_FILL, textColor: MUTED, fontStyle: "bold", fontSize: 8.5, valign: "middle" },
    footStyles: { fillColor: HEAD_FILL, textColor: INK, fontStyle: "bold" },
    bodyStyles: { lineWidth: { bottom: 0.5 } },
    columnStyles: Object.fromEntries(
      [...columns.map((_, i) => [i + 1, { halign: "right" as const }]), [columns.length + 1, { halign: "right" as const, fontStyle: "bold" as const }]],
    ),
    didParseCell: (data) => {
      if (data.section !== "body" && data.column.index > 0) data.cell.styles.halign = "right";
    },
  });

  let y = (doc as unknown as { lastAutoTable: { finalY: number } }).lastAutoTable.finalY + 16;

  // Reconciliation note
  const ex = report.excluded;
  const notes: string[] = [];
  if (ex.nonWorkdayEntries > 0) notes.push(`${formatHours(ex.nonWorkdayHours)} h logged on weekends (${ex.nonWorkdayEntries} entries)`);
  if (ex.mergeRequestEntries > 0) notes.push(`${formatHours(ex.mergeRequestHours)} h logged on merge requests (${ex.mergeRequestEntries} entries)`);
  if (notes.length) {
    doc.setFontSize(8.5);
    doc.setTextColor(...MUTED);
    doc.text(`Not included: ${notes.join("; ")}.`, margin, y);
    y += 14;
  }

  // Optional per-person issue breakdown
  if (options.includeIssues) {
    const rows = people
      .filter((p) => p.issues.length > 0)
      .flatMap((p) => [
        [{ content: `${p.name}  ·  ${formatHours(p.totalHours)} h`, colSpan: 3, styles: { fontStyle: "bold" as const, fillColor: HEAD_FILL } }],
        ...p.issues.map((i) => [i.projectPath, `#${i.iid}  ${i.title}`, formatHours(i.hours)]),
      ]);

    if (rows.length) {
      doc.addPage();
      doc.setFont("helvetica", "bold");
      doc.setFontSize(13);
      doc.setTextColor(...INK);
      doc.text("Issue breakdown", margin, 56);
      autoTable(doc, {
        startY: 70,
        margin: { left: margin, right: margin },
        head: [["Project", "Issue", "Hours"]],
        body: rows,
        theme: "plain",
        styles: { font: "helvetica", fontSize: 9, textColor: INK, cellPadding: 5, lineColor: LINE, overflow: "linebreak" },
        headStyles: { textColor: MUTED, fontStyle: "bold", fontSize: 8.5 },
        bodyStyles: { lineWidth: { bottom: 0.5 } },
        columnStyles: { 0: { cellWidth: 130, textColor: MUTED }, 2: { halign: "right", cellWidth: 55 } },
      });
    }
  }

  // Footer on every page
  const pages = doc.getNumberOfPages();
  const generated = new Date(report.generatedAt).toLocaleString("en-PH", { timeZone: report.timeZone, dateStyle: "medium", timeStyle: "short" });
  for (let i = 1; i <= pages; i++) {
    doc.setPage(i);
    doc.setFont("helvetica", "normal");
    doc.setFontSize(8);
    doc.setTextColor(...MUTED);
    const h = doc.internal.pageSize.getHeight();
    doc.text(`Generated by gitlab-tempo from GitLab on ${generated}`, margin, h - 24);
    doc.text(`Page ${i} of ${pages}`, pageWidth - margin, h - 24, { align: "right" });
  }

  const slug = report.group.fullPath.replace(/[^\w-]+/g, "-");
  doc.save(`gitlab-tempo-${slug}-${report.from}_${report.to}.pdf`);
}
