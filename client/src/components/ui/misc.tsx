import * as React from "react";
import { cn } from "@/lib/utils";

function Skeleton({ className, ...props }: React.ComponentProps<"div">) {
  return <div data-slot="skeleton" className={cn("bg-accent animate-pulse rounded-md", className)} {...props} />;
}

function Alert({
  className,
  variant = "default",
  ...props
}: React.ComponentProps<"div"> & { variant?: "default" | "destructive" }) {
  return (
    <div
      role="alert"
      data-slot="alert"
      className={cn(
        "relative w-full rounded-lg border px-4 py-3 text-sm flex gap-3 items-start [&>svg]:size-4 [&>svg]:mt-0.5 [&>svg]:shrink-0",
        variant === "destructive" ? "text-destructive bg-card border-destructive/30" : "bg-card text-card-foreground",
        className,
      )}
      {...props}
    />
  );
}

function Badge({ className, ...props }: React.ComponentProps<"span">) {
  return (
    <span
      data-slot="badge"
      className={cn(
        "inline-flex items-center rounded-md border px-2 py-0.5 text-xs font-medium whitespace-nowrap bg-secondary text-secondary-foreground",
        className,
      )}
      {...props}
    />
  );
}

export { Skeleton, Alert, Badge };
