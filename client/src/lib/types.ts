// Mirrors the JSON returned by Tempo.Api (see server/Tempo.Api/Reports/ReportModels.cs).

export type HoursMap = Record<string, number>;

export interface GitLabInstance {
  id: string;
  name: string;
  gitLabUrl: string;
  defaultGroupPath: string | null;
  serverTokenConfigured: boolean;
  /** Reports use the signed-in person's GitLab session, so no token is needed. */
  usesSignIn: boolean;
}

export interface Session {
  /** False when the server has no GitLab OAuth app configured: the app is open to anyone. */
  authEnabled: boolean;
  signInInstance: { id: string; name: string } | null;
  user: { username: string; name: string; avatarUrl: string | null } | null;
}

export interface AppConfig {
  /** GitLab instances the server can read from; the first is the default. */
  instances: GitLabInstance[];
  timeZone: string;
  workDays: string[];
  maxWeeks: number;
}

export interface GitLabUser {
  username: string;
  name: string;
  avatarUrl: string | null;
}

export interface WeekInfo {
  key: string;
  start: string;
  end: string;
  label: string;
}

export interface IssueHours {
  projectPath: string;
  iid: string;
  title: string;
  webUrl: string | null;
  hours: number;
  entries: number;
}

export interface PersonReport {
  username: string;
  name: string;
  avatarUrl: string | null;
  totalHours: number;
  weekHours: HoursMap;
  dayHours: HoursMap;
  issues: IssueHours[];
}

export interface TeamSummary {
  totalHours: number;
  weekHours: HoursMap;
  dayHours: HoursMap;
  peopleWithTime: number;
  peopleListed: number;
  issueCount: number;
  averageHoursPerPerson: number;
}

export interface WeeklyReport {
  group: { fullPath: string; name: string; webUrl: string | null };
  from: string;
  to: string;
  timeZone: string;
  weeks: WeekInfo[];
  days: string[];
  people: PersonReport[];
  team: TeamSummary;
  excluded: {
    nonWorkdayHours: number;
    nonWorkdayEntries: number;
    mergeRequestHours: number;
    mergeRequestEntries: number;
  };
  membersIncluded: boolean;
  generatedAt: string;
}

export interface ReportQuery {
  group: string;
  from: string;
  to: string;
}
