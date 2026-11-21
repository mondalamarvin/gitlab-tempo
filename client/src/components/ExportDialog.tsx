import { useState } from "react";
import { FileDown } from "lucide-react";
import { Button } from "@/components/ui/button";
import { Dialog, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle, DialogTrigger } from "@/components/ui/dialog";
import { formatRange } from "@/lib/dates";
import type { WeeklyReport } from "@/lib/types";

export function ExportDialog({ report, showEmpty }: { report: WeeklyReport; showEmpty: boolean }) {
  const [open, setOpen] = useState(false);
  const [includeIssues, setIncludeIssues] = useState(false);
  const [includeEmpty, setIncludeEmpty] = useState(showEmpty);

  return (
    <Dialog open={open} onOpenChange={setOpen}>
      <DialogTrigger asChild>
        <Button>
          <FileDown /> Export PDF
        </Button>
      </DialogTrigger>
      <DialogContent>
        <DialogHeader>
          <DialogTitle>Export PDF</DialogTitle>
          <DialogDescription>
            {report.group.name} · {formatRange(report.from, report.to)}
          </DialogDescription>
        </DialogHeader>

        <div className="grid gap-3 text-sm">
          <label className="flex items-start gap-3">
            <input type="checkbox" className="accent-foreground mt-0.5 size-4" checked={includeIssues} onChange={(e) => setIncludeIssues(e.target.checked)} />
            <span>
              <span className="font-medium">Include issue breakdown</span>
              <span className="text-muted-foreground block">Adds a page listing each person's issues and hours.</span>
            </span>
          </label>
          <label className="flex items-start gap-3">
            <input type="checkbox" className="accent-foreground mt-0.5 size-4" checked={includeEmpty} onChange={(e) => setIncludeEmpty(e.target.checked)} />
            <span>
              <span className="font-medium">Include members with no time</span>
              <span className="text-muted-foreground block">Shows everyone in the group, even with 0 hours.</span>
            </span>
          </label>
        </div>

        <DialogFooter>
          <Button variant="outline" onClick={() => setOpen(false)}>
            Cancel
          </Button>
          <Button
            onClick={async () => {
              // jsPDF is loaded only when someone exports, keeping the first page load small.
              const { exportReportPdf } = await import("@/lib/pdf");
              exportReportPdf(report, { includeIssues, includeEmptyMembers: includeEmpty });
              setOpen(false);
            }}
          >
            <FileDown /> Download
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
}
