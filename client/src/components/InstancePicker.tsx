import { Server } from "lucide-react";
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select";
import { cn } from "@/lib/utils";
import { useGetConfigQuery } from "@/store/api";
import { currentConnection, instanceChanged } from "@/store/settingsSlice";
import { useAppDispatch, useAppSelector } from "@/store/store";

/** Switches between the GitLab instances the server is configured for. Hidden when there is only one. */
export function InstancePicker({ className }: { className?: string }) {
  const dispatch = useAppDispatch();
  const settings = useAppSelector((s) => s.settings);
  const { data: config } = useGetConfigQuery();
  if (!config || config.instances.length < 2) return null;

  const { instance } = currentConnection(settings, config);
  return (
    <Select value={instance.id} onValueChange={(id) => dispatch(instanceChanged(id))}>
      <SelectTrigger aria-label="GitLab instance" className={cn(className)}>
        <span className="flex min-w-0 items-center gap-2">
          <Server className="text-muted-foreground size-4" />
          <SelectValue />
        </span>
      </SelectTrigger>
      <SelectContent>
        {config.instances.map((i) => (
          <SelectItem key={i.id} value={i.id} description={i.gitLabUrl.replace(/^https?:\/\//, "")}>
            {i.name}
          </SelectItem>
        ))}
      </SelectContent>
    </Select>
  );
}
