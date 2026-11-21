import { useState } from "react";
import { AlertCircle, LogIn } from "lucide-react";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { Alert } from "@/components/ui/misc";
import { ThemeToggle } from "@/components/ThemeToggle";

/** The server puts sign-in failures (e.g. "not allowed") in ?signin_error=… on the way back from GitLab. */
function takeSignInError(): string {
  const params = new URLSearchParams(window.location.search);
  const error = params.get("signin_error") ?? "";
  if (error) {
    params.delete("signin_error");
    const query = params.toString();
    window.history.replaceState(null, "", window.location.pathname + (query ? `?${query}` : "") + window.location.hash);
  }
  return error;
}

export function SignInScreen({ instanceName, logo }: { instanceName: string; logo: React.ReactNode }) {
  const [error] = useState(takeSignInError);
  const returnUrl = window.location.pathname + window.location.search;

  return (
    <main className="relative grid min-h-svh place-items-center p-4">
      <ThemeToggle className="absolute top-3 right-3" />
      <Card className="w-full max-w-md">
        <CardHeader>
          {logo}
          <CardTitle className="pt-4 text-xl">Sign in</CardTitle>
          <CardDescription>Use your {instanceName} account. Only people who have been given access can open the reports.</CardDescription>
        </CardHeader>
        <CardContent className="grid gap-5">
          {error && (
            <Alert variant="destructive">
              <AlertCircle />
              <span>{error}</span>
            </Alert>
          )}
          <Button asChild size="lg">
            <a href={`/auth/login?returnUrl=${encodeURIComponent(returnUrl)}`}>
              <LogIn /> Sign in with GitLab
            </a>
          </Button>
        </CardContent>
      </Card>
    </main>
  );
}
