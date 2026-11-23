# Report scope setting — design

**Size:** small change. Approved 2026-11-23.

## Intent
Operators want to choose, through config / environment variables, whether signed-in people see only
their own time or the whole group's report.

## Goals / non-goals
- Goal: one server-side setting that switches between "own time only" (today's behavior) and "whole group".
- Non-goal: per-group settings, configurable role thresholds, an in-app (browser) toggle.
- Anti-goal: anything a user could flip from the browser to bypass the restriction.

## Approaches
1. **Single option under `Tempo:Auth` (picked).** Simple, server-side, matches existing config style.
2. Per-group map or minimum-role threshold — more flexible, but nobody asked for it.
3. In-app Settings toggle — rejected: settings live in the browser, so anyone could bypass it.

## Design
- `Tempo:Auth:ReportScope` (env `Tempo__Auth__ReportScope`), enum `ReportScope`:
  - `OwnTime` (default): Maintainers/Owners/admins see everyone; others see only their own time.
  - `WholeGroup`: everyone who signs in sees the whole group.
- With `WholeGroup`, `OwnTimeOnlyUsernameAsync` returns null before the GitLab role check.
- Only relevant with sign-in on; without sign-in reports already show the whole group.
- Unknown value fails at startup with a clear error.
- Tests cover both modes; README documents the variable.
