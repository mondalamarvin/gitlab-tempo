import { useState } from "react";
import { ExternalLink, Loader2, KeyRound } from "lucide-react";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Alert } from "@/components/ui/misc";
import { useAppDispatch, useAppSelector } from "@/store/store";
import { connected, type CurrentConnection } from "@/store/settingsSlice";

/**
 * Token + group form for one GitLab instance, used on first run and in the Settings dialog.
 * Render it with `key={connection.instance.id}` so switching instances resets the fields.
 */
export function ConnectForm({ connection, onDone }: { connection: CurrentConnection; onDone?: () => void }) {
  const dispatch = useAppDispatch();
  const rememberToken = useAppSelector((s) => s.settings.rememberToken);
  const { instance } = connection;

  const [token, setToken] = useState(connection.token);
  const [groupPath, setGroupPath] = useState(connection.groupPath);
  const [remember, setRemember] = useState(rememberToken);
  const [error, setError] = useState("");
  const [checking, setChecking] = useState(false);

  const gitLabUrl = instance.gitLabUrl;
  // On the sign-in instance the server uses the signed-in GitLab session; there is no token to enter.
  const askForToken = !instance.usesSignIn;
  const tokenOptional = instance.serverTokenConfigured;

  async function submit(e: React.FormEvent) {
    e.preventDefault();
    setError("");
    const group = groupPath.trim().replace(/^\/+|\/+$/g, "");
    const pat = askForToken ? token.trim() : "";
    if (!group) return setError("Enter the group path, e.g. my-company.");
    if (askForToken && !pat && !tokenOptional) return setError("Paste your Personal Access Token.");

    setChecking(true);
    try {
      const headers: Record<string, string> = { "X-GitLab-Instance": instance.id };
      if (pat) headers["X-GitLab-Token"] = pat;
      const res = await fetch("/api/me", { headers });
      if (!res.ok) {
        const body = await res.json().catch(() => ({}));
        throw new Error(body.detail ?? `GitLab check failed (${res.status}).`);
      }
      dispatch(connected({ instanceId: instance.id, token: pat, groupPath: group, rememberToken: remember }));
      onDone?.();
    } catch (err) {
      setError(err instanceof Error ? err.message : "Could not verify the token.");
    } finally {
      setChecking(false);
    }
  }

  return (
    <form onSubmit={submit} className="grid gap-5">
      {askForToken && (
        <div className="grid gap-2">
          <Label htmlFor="token">GitLab Personal Access Token {tokenOptional && <span className="text-muted-foreground font-normal">(optional)</span>}</Label>
          <Input
            id="token"
            type="password"
            autoComplete="off"
            placeholder="glpat-…"
            value={token}
            onChange={(e) => setToken(e.target.value)}
          />
          <p className="text-muted-foreground text-xs leading-relaxed">
            Create one with the <code className="bg-muted rounded px-1">read_api</code> scope.{" "}
            <a
              className="text-foreground inline-flex items-center gap-1 underline underline-offset-2"
              href={`${gitLabUrl}/-/user_settings/personal_access_tokens?name=gitlab-tempo&scopes=read_api`}
              target="_blank"
              rel="noreferrer"
            >
              Create token <ExternalLink className="size-3" />
            </a>
          </p>
        </div>
      )}

      <div className="grid gap-2">
        <Label htmlFor="group">Group path</Label>
        <Input id="group" placeholder="my-company" value={groupPath} onChange={(e) => setGroupPath(e.target.value)} />
        <p className="text-muted-foreground text-xs">The part after {gitLabUrl.replace(/^https?:\/\//, "")}/ — all projects in the group are included.</p>
      </div>

      {askForToken && (
        <label className="flex items-center gap-2 text-sm">
          <input type="checkbox" className="accent-foreground size-4" checked={remember} onChange={(e) => setRemember(e.target.checked)} />
          Remember token on this device
        </label>
      )}

      {error && <Alert variant="destructive">{error}</Alert>}

      <Button type="submit" disabled={checking}>
        {checking ? <Loader2 className="animate-spin" /> : <KeyRound />}
        {checking ? "Checking with GitLab…" : askForToken ? "Connect" : "Continue"}
      </Button>
    </form>
  );
}
