# gitlab-tempo

Weekly time reports from GitLab issue time tracking.

GitLab lets you log time on issues (`/spend 2h`), but it can't give management a weekly summary per person. gitlab-tempo reads the time logs for a whole group through the GitLab GraphQL API and shows:

- hours **per person, per day** for a single week, or **per person, per week** for a longer range
- **team totals** (total hours, people who logged time, average per person, issues worked on)
- a drill-down of each person's issues
- a **PDF export** for management, with an optional per-issue breakdown

## How the numbers are counted

| Rule | Behaviour |
|---|---|
| Date | Uses the time log's **spent at** date (the date in `/spend 2h 2026-11-17`), not the date it was entered |
| Time zone | Each entry is placed on its day in **Asia/Manila** time |
| Work days | **Monday to Friday**. Weekend entries are left out of the totals and listed in a note under the table |
| Source | **Issues only**. Merge request time is left out of the totals and also listed in the note |
| Corrections | Negative entries (`/spend -1h`) reduce the totals, the same way they do in GitLab |
| Scope | Every project in the group, including subgroups |

Public holidays are not excluded yet.

## Stack

- **client/**: React 19, TypeScript, Vite, Tailwind CSS v4, shadcn/ui, Redux Toolkit (RTK Query), jsPDF
- **server/**: ASP.NET Core (.NET 10) minimal API that calls GitLab GraphQL and builds the report, with xUnit tests

## Prerequisites

- .NET 10 SDK
- Node.js 20 or newer
- A GitLab **Personal Access Token** with the `read_api` scope. Create one at **GitLab → Preferences → Access tokens**. Each team member can use their own.

## Run it locally

```bash
cd server/Tempo.Api
dotnet run
```

This starts the API on http://localhost:5080 and also starts the web app (`npm run dev` in `client/`) on http://localhost:5173, which proxies `/api` to the API. The first run installs the client's npm packages if they're missing. Stopping `dotnet run` stops the web app too. If Vite is already running, it is reused.

Open http://localhost:5173 (or http://localhost:5080, which redirects there), paste your token, enter the group path (the part after `gitlab.com/`), and connect.

## Deploy for the team

`npm run build` writes the web app into `server/Tempo.Api/wwwroot`, so one .NET app serves both the UI and the API:

```bash
cd client && npm ci && npm run build
cd ../server/Tempo.Api && dotnet publish -c Release -o ../../publish
```

Host the `publish` folder on IIS, a Windows service, Docker, or any server that runs .NET 10.

## Configuration

Set these in `server/Tempo.Api/appsettings.json`, or with environment variables such as `Tempo__DefaultGroupPath=my-company`.

| Key | Default | Purpose |
|---|---|---|
| `Tempo:GitLabUrl` | `https://gitlab.com` | Change it for a self-managed GitLab |
| `Tempo:DefaultGroupPath` | (empty) | Fills in the group path on the connect screen |
| `Tempo:ServerToken` | (empty) | Optional shared token used when a user doesn't enter one. Keep it in user secrets or an environment variable, never in git |
| `Tempo:TimeZone` | `Asia/Manila` | Decides which day an entry belongs to |
| `Tempo:WorkDays` | Mon–Fri | Example: `["Monday","Tuesday","Wednesday","Thursday","Friday","Saturday"]` |
| `Tempo:MaxWeeks` | `26` | Longest range a single report can cover |
| `Tempo:Instances` | (empty) | Several GitLab instances to switch between in the UI. When set, it replaces `GitLabUrl`, `DefaultGroupPath` and `ServerToken` |

### Several GitLab instances

To switch between, say, gitlab.com and a self-managed GitLab without restarting, list them under `Tempo:Instances`. The first one is the default, and a picker appears in the app header. Each instance keeps its own token and group in the browser. With [sign-in](#sign-in-with-gitlab-limit-who-can-use-the-app) on, only the instance people sign in with is offered, so there is no picker.

```json
"Tempo": {
  "Instances": [
    { "Id": "gitlab-com", "Name": "GitLab.com", "Url": "https://gitlab.com", "DefaultGroupPath": "my-company" },
    { "Id": "local", "Name": "Local GitLab", "Url": "http://localhost:8929" }
  ]
}
```

Each entry can also have a `ServerToken`. Keep tokens in user secrets, for example `dotnet user-secrets set "Tempo:Instances:1:ServerToken" "glpat-..."`. The browser only sends an instance `Id`, so the server never contacts a GitLab URL that isn't in this list.

## Sign in with GitLab (limit who can use the app)

Without this, anyone who can reach the app can use it, and if an instance has a `ServerToken`, they can read reports without any credentials. Turn on sign-in to allow only the people you choose.

**1. Register an OAuth application in GitLab.** Use User settings → Applications, your group → Settings → Applications, or Admin Area → Applications on self-managed. Register it on the same GitLab that `Tempo:Auth:InstanceId` points to:

| Field | Value |
|---|---|
| Redirect URI | `http://localhost:5173/signin-gitlab` for local development, plus `https://<your-host>/signin-gitlab` for production (one per line) |
| Confidential | Yes |
| Scopes | `read_api` |

**2. Give the app its ID and secret.** Never put the secret in `appsettings.json`.

```bash
cd server/Tempo.Api
dotnet user-secrets set "Tempo:Auth:ClientId" "<Application ID>"
dotnet user-secrets set "Tempo:Auth:ClientSecret" "<Secret>"
```

In production, use environment variables: `Tempo__Auth__ClientId`, `Tempo__Auth__ClientSecret`.

**3. Choose who gets in** (in `appsettings.json` or environment variables):

```json
"Tempo": {
  "Auth": {
    "AllowedGroups": ["my-company/managers"],
    "AllowedUsers": ["jdoe", "boss@my-company.com"]
  }
}
```

| Key | Default | Purpose |
|---|---|---|
| `Tempo:Auth:ClientId` / `ClientSecret` | (empty) | The GitLab OAuth application. Sign-in is on when `ClientId` is set |
| `Tempo:Auth:InstanceId` | first instance | Which GitLab instance people sign in with |
| `Tempo:Auth:AllowedGroups` | (empty) | Members of any of these groups get in (direct or inherited members) |
| `Tempo:Auth:AllowedUsers` | (empty) | Usernames or emails that get in as well |
| `Tempo:Auth:SessionHours` | `8` | Sign-in lasts this long without activity |

If both lists are empty, **anyone with an account on that GitLab** gets in. That's fine for a private self-managed GitLab, but not for gitlab.com.

How it works:

- The app works only with the GitLab people sign in with. Other entries in `Tempo:Instances` are hidden and refused by the API, so there is no instance picker. To use a different GitLab, point `Tempo:Auth:InstanceId` at it and register the OAuth application there.
- Reports use the person's own GitLab access, so nobody pastes a token and `ServerToken` is not used.
- The session is an encrypted, HTTP-only cookie. GitLab access tokens expire every 2 hours and are renewed automatically. Access is checked again at each renewal, so removing someone from the group or list locks them out within about 2 hours.
- In production, serve the app over **HTTPS**. Behind a reverse proxy that terminates TLS (nginx, Docker, a load balancer), set `ASPNETCORE_FORWARDEDHEADERS_ENABLED=true` so the redirect URI uses `https`.
- The cookie is encrypted with ASP.NET Core Data Protection keys. If the host doesn't keep them between restarts (e.g. some Docker setups), people have to sign in again after each restart.

## Security

- The token is sent from the browser to the gitlab-tempo API in the `X-GitLab-Token` header. The API forwards it to GitLab and doesn't store or log it.
- "Remember token on this device" keeps the token in `localStorage`. Turn it off to keep it only for the browser session.
- Each user sees only the time logs their own token can access in GitLab.

## API

| Endpoint | Description |
|---|---|
| `GET /api/session` | Whether sign-in is on and who is signed in. Always public |
| `GET /api/config` | Server settings the UI needs |
| `GET /api/me` | Checks the token and returns the GitLab user |
| `GET /api/report?group=acme&from=2026-11-16&to=2026-11-20` | The report. `from` and `to` are snapped to whole weeks starting Monday |
| `GET /auth/login`, `POST /auth/logout` | Sign in with GitLab / sign out (only when sign-in is on) |

With sign-in on, every `/api` endpoint except `/api/session` returns 401 without a session.

## Tests

```bash
cd server && dotnet test
```

## Roadmap ideas

- Philippine public holidays calendar
- Scheduled weekly email of the PDF to management
- Excel/CSV export
- Filters by project, label or milestone

## License

MIT
