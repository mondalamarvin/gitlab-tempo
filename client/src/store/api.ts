import {
  createApi,
  fetchBaseQuery,
  type BaseQueryFn,
  type FetchArgs,
  type FetchBaseQueryError,
} from "@reduxjs/toolkit/query/react";
import type { AppConfig, GitLabUser, ReportQuery, Session, WeeklyReport } from "@/lib/types";
import type { RootState } from "./store";

/** Cache key for requests that depend on the selected instance and token. */
type Connection = { instanceId: string; token: string };

/** Title of the server's 401 when the sign-in session is missing or has ended (GitLabSignIn.SignInRequiredTitle). */
const SIGN_IN_REQUIRED = "Sign in required";

const rawBaseQuery = fetchBaseQuery({
  baseUrl: "/api",
  prepareHeaders: (headers, { getState }) => {
    const { instanceId, connections } = (getState() as RootState).settings;
    if (instanceId) headers.set("X-GitLab-Instance", instanceId);
    const token = connections[instanceId]?.token;
    if (token) headers.set("X-GitLab-Token", token);
    return headers;
  },
});

/** When the sign-in session ends mid-use, reload the session so the app shows the sign-in screen. */
const baseQuery: BaseQueryFn<string | FetchArgs, unknown, FetchBaseQueryError> = async (args, api, extra) => {
  const result = await rawBaseQuery(args, api, extra);
  const data = result.error?.data as { title?: string } | undefined;
  if (result.error?.status === 401 && data?.title === SIGN_IN_REQUIRED) api.dispatch(tempoApi.util.invalidateTags(["Session"]));
  return result;
};

export const tempoApi = createApi({
  reducerPath: "tempoApi",
  baseQuery,
  tagTypes: ["Session"],
  keepUnusedDataFor: 300,
  endpoints: (build) => ({
    getSession: build.query<Session, void>({ query: () => "/session", providesTags: ["Session"] }),
    getConfig: build.query<AppConfig, void>({ query: () => "/config" }),
    // `instanceId` and `token` are part of the args so switching either refetches instead of reusing the cache.
    getMe: build.query<GitLabUser, Connection>({ query: () => "/me" }),
    getReport: build.query<WeeklyReport, ReportQuery & Connection>({
      query: ({ group, from, to }) => ({ url: "/report", params: { group, from, to, includeMembers: true } }),
    }),
  }),
});

export const { useGetSessionQuery, useGetConfigQuery, useGetMeQuery, useGetReportQuery, useLazyGetMeQuery } = tempoApi;

/** Ends the GitLab sign-in session and reloads, which shows the sign-in screen. */
export async function signOutOfApp() {
  await fetch("/auth/logout", { method: "POST" }).catch(() => undefined);
  window.location.assign("/");
}

/** Pulls the ProblemDetails "detail" message out of an RTK Query error. */
export function errorMessage(error: unknown): string {
  if (!error) return "";
  const e = error as Partial<FetchBaseQueryError> & { message?: string; data?: unknown };
  if (e.status !== undefined) {
    const data = e.data as { detail?: string; title?: string } | undefined;
    if (data?.detail) return data.detail;
    if (data?.title) return data.title;
    if (e.status === "FETCH_ERROR") return "Cannot reach the gitlab-tempo API. Is the server running?";
    return `Request failed (${String(e.status)}).`;
  }
  return e.message ?? "Something went wrong.";
}
