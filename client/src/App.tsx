import { useEffect, useState } from "react";
import { AlertCircle, Info, LogOut, RefreshCw, Settings } from "lucide-react";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { Dialog, DialogContent, DialogDescription, DialogHeader, DialogTitle } from "@/components/ui/dialog";
import { Alert, Skeleton } from "@/components/ui/misc";
import { ConnectForm } from "@/components/ConnectForm";
import { PeriodPicker } from "@/components/PeriodPicker";
import { SummaryCards } from "@/components/SummaryCards";
import { ReportTable } from "@/components/ReportTable";
import { ExportDialog } from "@/components/ExportDialog";
import { InstancePicker } from "@/components/InstancePicker";
import { ThemeToggle } from "@/components/ThemeToggle";
import { SignInScreen } from "@/components/SignInScreen";
import { addDays, formatHours } from "@/lib/dates";
import type { Session } from "@/lib/types";
import { errorMessage, signOutOfApp, useGetConfigQuery, useGetMeQuery, useGetReportQuery, useGetSessionQuery } from "@/store/api";
import { currentConnection, instanceChanged, showEmptyMembersToggled, signedOut, type CurrentConnection } from "@/store/settingsSlice";
import { useAppDispatch, useAppSelector } from "@/store/store";

function Logo() {
  return (
    <div className="flex items-center gap-2.5">
      <img src="/favicon.svg" alt="" className="size-7 rounded-lg dark:ring-1 dark:ring-white/20" />
      <span className="text-lg font-semibold tracking-tight">gitlab-tempo</span>
    </div>
  );
}

function ErrorScreen({ message }: { message: string }) {
  return (
    <main className="grid min-h-svh place-items-center p-4">
      <Alert variant="destructive" className="max-w-md">
        <AlertCircle />
        <span>{message}</span>
      </Alert>
    </main>
  );
}

export default function App() {
  const { data: session, error: sessionError, isLoading } = useGetSessionQuery();
  if (isLoading) return null;
  if (!session) return <ErrorScreen message={errorMessage(sessionError) || "Cannot reach the gitlab-tempo API."} />;
  if (session.authEnabled && !session.user) return <SignInScreen instanceName={session.signInInstance?.name ?? "GitLab"} logo={<Logo />} />;
  return <SignedInApp session={session} />;
}

function SignedInApp({ session }: { session: Session }) {
  const dispatch = useAppDispatch();
  const settings = useAppSelector((s) => s.settings);
  const { data: config, error: configError, isLoading } = useGetConfigQuery();

  // Select the default instance on first run, or when a saved one was removed from the server config.
  useEffect(() => {
    if (config?.instances.length && !config.instances.some((i) => i.id === settings.instanceId))
      dispatch(instanceChanged(config.instances[0].id));
  }, [config, settings.instanceId, dispatch]);

  if (isLoading) return null;
  if (!config?.instances.length)
    return <ErrorScreen message={configError ? errorMessage(configError) : "No GitLab instance is configured on the server."} />;

  const connection = currentConnection(settings, config);
  if (!connection.ready) {
    return (
      <main className="relative grid min-h-svh place-items-center p-4">
        <div className="absolute top-3 right-3 flex items-center gap-1">
          <ThemeToggle />
          {session.authEnabled && <SignOutButton session={session} />}
        </div>
        <Card className="w-full max-w-md">
          <CardHeader>
            <Logo />
            <CardTitle className="pt-4 text-xl">Connect to {connection.instance.name}</CardTitle>
            <CardDescription>
              {connection.instance.usesSignIn
                ? "gitlab-tempo reads the time logged on your group's issues and turns it into a weekly report. Choose the group to report on."
                : "gitlab-tempo reads the time logged on your group's issues and turns it into a weekly report. Your token stays in this browser and is only sent to GitLab."}
            </CardDescription>
          </CardHeader>
          <CardContent className="grid gap-5">
            <InstancePicker />
            <ConnectForm key={connection.instance.id} connection={connection} />
          </CardContent>
        </Card>
      </main>
    );
  }

  return <ReportPage connection={connection} session={session} />;
}

function SignOutButton({ session }: { session: Session }) {
  return (
    <Button variant="ghost" size="icon" aria-label="Sign out" title={`Sign out (${session.user?.username ?? ""})`} onClick={() => void signOutOfApp()}>
      <LogOut />
    </Button>
  );
}

function ReportPage({ connection, session }: { connection: CurrentConnection; session: Session }) {
  const dispatch = useAppDispatch();
  const { from, weeks, showEmptyMembers } = useAppSelector((s) => s.settings);
  const { instance, token, groupPath } = connection;
  const [settingsOpen, setSettingsOpen] = useState(false);
  const { data: me } = useGetMeQuery({ instanceId: instance.id, token });

  const to = addDays(from, weeks * 7 - 1);
  const { data: report, error, isFetching, isLoading, refetch } = useGetReportQuery({ instanceId: instance.id, token, group: groupPath, from, to });
  const unauthorized = !!error && "status" in error && error.status === 401;

  return (
    <div className="min-h-svh">
      <header className="bg-background/80 sticky top-0 z-10 border-b backdrop-blur">
        <div className="mx-auto flex h-14 max-w-6xl items-center gap-4 px-4">
          <Logo />
          <span className="text-muted-foreground hidden truncate text-sm sm:inline">/ {report?.group.name ?? groupPath}</span>
          <div className="ml-auto flex items-center gap-2">
            <InstancePicker className="w-40 sm:w-48" />
            {me && (
              <div className="hidden items-center gap-2 text-sm md:flex">
                {me.avatarUrl && <img src={me.avatarUrl} alt="" className="size-6 rounded-full" />}
                <span className="text-muted-foreground">{me.name}</span>
              </div>
            )}
            <ThemeToggle />
            <Button variant="ghost" size="icon" aria-label="Settings" onClick={() => setSettingsOpen(true)}>
              <Settings />
            </Button>
            {session.authEnabled && <SignOutButton session={session} />}
          </div>
        </div>
      </header>

      <main className="mx-auto grid max-w-6xl gap-6 px-4 py-6">
        <div className="flex flex-wrap items-end justify-between gap-4">
          <div>
            <h1 className="text-2xl font-semibold tracking-tight">Weekly time report</h1>
            <p className="text-muted-foreground text-sm">Hours logged on issues, Monday to Friday, Philippine time.</p>
          </div>
          <div className="flex items-center gap-2">
            <Button variant="outline" onClick={() => refetch()} disabled={isFetching}>
              <RefreshCw className={isFetching ? "animate-spin" : ""} /> Refresh
            </Button>
            {report && <ExportDialog report={report} showEmpty={showEmptyMembers} />}
          </div>
        </div>

        <PeriodPicker />

        {error && (
          <Alert variant="destructive">
            <AlertCircle />
            <div className="grid gap-2">
              <span>{errorMessage(error)}</span>
              {unauthorized && !instance.usesSignIn && (
                <Button size="sm" variant="outline" className="w-fit" onClick={() => setSettingsOpen(true)}>
                  Update token
                </Button>
              )}
            </div>
          </Alert>
        )}

        {isLoading && <LoadingState />}

        {report && !error && (
          <div className={isFetching ? "opacity-60 transition-opacity" : "transition-opacity"}>
            <div className="grid gap-6">
              <SummaryCards report={report} />

              <Card className="gap-0 py-0">
                <div className="flex flex-wrap items-center justify-between gap-2 border-b px-4 py-3">
                  <div className="text-sm font-medium">Hours per person</div>
                  {report.membersIncluded && (
                    <label className="text-muted-foreground flex items-center gap-2 text-sm">
                      <input
                        type="checkbox"
                        className="accent-foreground size-4"
                        checked={showEmptyMembers}
                        onChange={() => dispatch(showEmptyMembersToggled())}
                      />
                      Show members with no time
                    </label>
                  )}
                </div>
                <ReportTable report={report} showEmpty={showEmptyMembers} />
              </Card>

              <ExcludedNote report={report} />
            </div>
          </div>
        )}
      </main>

      <Dialog open={settingsOpen} onOpenChange={setSettingsOpen}>
        <DialogContent>
          <DialogHeader>
            <DialogTitle>Settings</DialogTitle>
            <DialogDescription>
              Change {instance.usesSignIn ? "" : "the token or "}the group the report is built from on {instance.name}.
            </DialogDescription>
          </DialogHeader>
          <ConnectForm key={instance.id} connection={connection} onDone={() => setSettingsOpen(false)} />
          {instance.usesSignIn ? (
            <Button variant="ghost" className="text-muted-foreground" onClick={() => void signOutOfApp()}>
              <LogOut /> Sign out{session.user ? ` (${session.user.username})` : ""}
            </Button>
          ) : (
            <Button
              variant="ghost"
              className="text-muted-foreground"
              onClick={() => {
                dispatch(signedOut());
                setSettingsOpen(false);
              }}
            >
              <LogOut /> Sign out of {instance.name} and forget the token
            </Button>
          )}
        </DialogContent>
      </Dialog>
    </div>
  );
}

function ExcludedNote({ report }: { report: import("@/lib/types").WeeklyReport }) {
  const ex = report.excluded;
  const parts: string[] = [];
  if (ex.nonWorkdayEntries) parts.push(`${formatHours(ex.nonWorkdayHours)} h logged on weekends`);
  if (ex.mergeRequestEntries) parts.push(`${formatHours(ex.mergeRequestHours)} h logged on merge requests`);
  if (!parts.length) return null;
  return (
    <p className="text-muted-foreground flex items-center gap-2 text-xs">
      <Info className="size-3.5" /> Not included in totals: {parts.join(" · ")}.
    </p>
  );
}

function LoadingState() {
  return (
    <div className="grid gap-6">
      <div className="grid grid-cols-2 gap-3 lg:grid-cols-4">
        {Array.from({ length: 4 }).map((_, i) => (
          <Skeleton key={i} className="h-24 rounded-xl" />
        ))}
      </div>
      <Skeleton className="h-80 rounded-xl" />
      <p className="text-muted-foreground text-center text-sm">Reading time logs from GitLab…</p>
    </div>
  );
}
