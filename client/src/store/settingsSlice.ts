import { createSlice, type PayloadAction } from "@reduxjs/toolkit";
import { mondayOf, todayIn } from "@/lib/dates";
import type { AppConfig, GitLabInstance } from "@/lib/types";

const STORAGE_KEY = "gitlab-tempo.settings";
const SESSION_TOKENS_KEY = `${STORAGE_KEY}.tokens`;
const LEGACY_KEYS = ["tempo.settings", "tempo.settings.token"];

/** What the user entered for one GitLab instance. Each instance keeps its own token and group. */
export interface Connection {
  token: string;
  groupPath: string;
  /** Set by "Sign out" so the server token (if any) is not used until the user connects again. */
  signedOut: boolean;
}

export interface SettingsState {
  /** Id of the selected GitLab instance ("" until the config has loaded). */
  instanceId: string;
  connections: Record<string, Connection>;
  rememberToken: boolean;
  /** Monday of the first week in the report. */
  from: string;
  /** Number of weeks in the report. */
  weeks: number;
  showEmptyMembers: boolean;
}

interface Persisted {
  instanceId?: string;
  connections?: Record<string, Partial<Connection>>;
  rememberToken?: boolean;
  showEmptyMembers?: boolean;
}

function load(): Persisted {
  try {
    // Settings saved before the rename (single instance) are dropped: the user reconnects once.
    for (const key of LEGACY_KEYS) {
      localStorage.removeItem(key);
      sessionStorage.removeItem(key);
    }
    const saved = JSON.parse(localStorage.getItem(STORAGE_KEY) ?? "{}") as Persisted;
    const sessionTokens = JSON.parse(sessionStorage.getItem(SESSION_TOKENS_KEY) ?? "{}") as Record<string, string>;
    const connections: Record<string, Partial<Connection>> = { ...saved.connections };
    for (const [id, token] of Object.entries(sessionTokens)) connections[id] = { ...connections[id], token };
    return { ...saved, connections };
  } catch {
    return {};
  }
}

export function persist(state: SettingsState) {
  try {
    const connections: Record<string, Partial<Connection>> = {};
    const sessionTokens: Record<string, string> = {};
    for (const [id, c] of Object.entries(state.connections)) {
      connections[id] = { groupPath: c.groupPath, signedOut: c.signedOut, token: state.rememberToken ? c.token : undefined };
      if (!state.rememberToken && c.token) sessionTokens[id] = c.token;
    }
    const data: Persisted = {
      instanceId: state.instanceId,
      connections,
      rememberToken: state.rememberToken,
      showEmptyMembers: state.showEmptyMembers,
    };
    localStorage.setItem(STORAGE_KEY, JSON.stringify(data));
    if (state.rememberToken) sessionStorage.removeItem(SESSION_TOKENS_KEY);
    else sessionStorage.setItem(SESSION_TOKENS_KEY, JSON.stringify(sessionTokens));
  } catch {
    /* storage unavailable: settings live in memory for this tab */
  }
}

const saved = load();

const initialState: SettingsState = {
  instanceId: saved.instanceId ?? "",
  connections: Object.fromEntries(
    Object.entries(saved.connections ?? {}).map(([id, c]) => [
      id,
      { token: c.token ?? "", groupPath: c.groupPath ?? "", signedOut: c.signedOut ?? false },
    ]),
  ),
  rememberToken: saved.rememberToken ?? true,
  from: mondayOf(todayIn()),
  weeks: 1,
  showEmptyMembers: saved.showEmptyMembers ?? true,
};

const settingsSlice = createSlice({
  name: "settings",
  initialState,
  reducers: {
    instanceChanged(state, action: PayloadAction<string>) {
      state.instanceId = action.payload;
    },
    connected(state, action: PayloadAction<{ instanceId: string; token: string; groupPath: string; rememberToken: boolean }>) {
      const { instanceId, token, groupPath, rememberToken } = action.payload;
      state.instanceId = instanceId;
      state.connections[instanceId] = { token, groupPath, signedOut: false };
      state.rememberToken = rememberToken;
    },
    signedOut(state) {
      const current = state.connections[state.instanceId];
      state.connections[state.instanceId] = { token: "", groupPath: current?.groupPath ?? "", signedOut: true };
    },
    periodChanged(state, action: PayloadAction<{ from: string; weeks: number }>) {
      state.from = mondayOf(action.payload.from);
      state.weeks = Math.max(1, action.payload.weeks);
    },
    showEmptyMembersToggled(state) {
      state.showEmptyMembers = !state.showEmptyMembers;
    },
  },
});

export interface CurrentConnection {
  instance: GitLabInstance;
  token: string;
  groupPath: string;
  /** True when there is enough to load a report: a group, plus a token, the server's token or a sign-in session. */
  ready: boolean;
}

/** The selected instance and what the user entered for it, with server defaults filled in. */
export function currentConnection(settings: SettingsState, config: AppConfig): CurrentConnection {
  const instance = config.instances.find((i) => i.id === settings.instanceId) ?? config.instances[0];
  const saved = settings.connections[instance.id];
  const token = saved?.token ?? "";
  const groupPath = saved?.groupPath || instance.defaultGroupPath || "";
  // On the sign-in instance the server uses the signed-in session, so only the group is needed.
  const ready = instance.usesSignIn
    ? !!groupPath
    : !saved?.signedOut && !!groupPath && (!!token || instance.serverTokenConfigured);
  return { instance, token, groupPath, ready };
}

export const { instanceChanged, connected, signedOut, periodChanged, showEmptyMembersToggled } = settingsSlice.actions;
export default settingsSlice.reducer;
