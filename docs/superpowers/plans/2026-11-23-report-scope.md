# Report Scope Setting Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Let operators choose via config/env whether signed-in people see only their own time or the whole group's report.

**Architecture:** A `ReportScope` enum on `AuthOptions`, bound from `Tempo:Auth:ReportScope`. `OwnTimeOnlyUsernameAsync` in `Program.cs` returns null (whole group) before the role check when the scope is `WholeGroup`. Invalid values fail at startup in `AddGitLabSignIn`, which already binds options eagerly.

**Tech Stack:** ASP.NET Core minimal API (.NET), xUnit + WebApplicationFactory.

**Spec:** `docs/superpowers/specs/2026-11-23-report-scope-design.md`

## Global Constraints

- Key: `Tempo:Auth:ReportScope`; env `Tempo__Auth__ReportScope`.
- Values: `OwnTime` (default, today's behavior), `WholeGroup`.
- No client changes: `ownTimeOnly` is already false when the whole group is returned, so the notice disappears on its own.

## Review Focus

- Value given in another case (`wholegroup`) → accepted (config binder parses enums case-insensitively); pinned by test using lowercase.
- Numeric value (`Tempo__Auth__ReportScope=7`) → binder accepts any integer for an enum; must fail at startup. Pinned by test.
- Unknown word (`Everyone`) → fails at startup with a message naming the key. Pinned by test.
- `WholeGroup` with a non-member / Guest → still sees the whole group (sign-in gate already decides who gets in). Covered by the Guest case.
- Sign-in off → setting has no effect; `UsesSignIn` short-circuit already runs first. No new test.

---

### Task 1: ReportScope setting

**Files:**
- Modify: `server/Tempo.Api/TempoOptions.cs` (add enum + `AuthOptions.ReportScope`)
- Modify: `server/Tempo.Api/Auth/GitLabSignIn.cs:26-28` (startup validation)
- Modify: `server/Tempo.Api/Program.cs` (`OwnTimeOnlyUsernameAsync`)
- Modify: `README.md` (Auth key table + "Who sees what")
- Test: `server/Tempo.Api.Tests/SignInFlowTests.cs`

**Interfaces:**
- Produces: `public enum ReportScope { OwnTime, WholeGroup }` in namespace `Tempo.Api`; `AuthOptions.ReportScope { get; set; } = ReportScope.OwnTime`.

- [ ] **Step 1: Write failing tests** in `SignInFlowTests.cs`. Give `Create` an optional `Action<IWebHostBuilder>? configure = null` applied after the default settings.

```csharp
[Theory]
[InlineData(10)] [InlineData(30)]
public async Task With_whole_group_scope_everyone_sees_the_whole_group(int accessLevel)
{
    var (app, gitLab) = Create(configure: b => b.UseSetting("Tempo:Auth:ReportScope", "wholegroup"));
    // sign in at accessLevel, fetch report
    Assert.False(report.GetProperty("ownTimeOnly").GetBoolean());
    Assert.Equal(["alice", "bob", "carol"], Usernames(report));
    Assert.Equal(5, report.GetProperty("team").GetProperty("totalHours").GetDouble());
}

[Theory]
[InlineData("Everyone")] [InlineData("7")]
public void An_invalid_report_scope_stops_the_app_at_startup(string value)
{
    var (app, _) = Create(configure: b => b.UseSetting("Tempo:Auth:ReportScope", value));
    using var _ = app;
    var ex = Assert.ThrowsAny<Exception>(() => app.CreateClient());
    Assert.Contains("Tempo:Auth:ReportScope", ex.ToString());
}
```

- [ ] **Step 2: Run** `cd server && dotnet test --filter "FullyQualifiedName~SignInFlowTests"` — Expected: new tests FAIL.

- [ ] **Step 3: Implement**
  - Add enum and property (XML doc comments like neighbors).
  - In `AddGitLabSignIn`, after binding: if `!Enum.IsDefined(auth.ReportScope)` throw `InvalidOperationException("Tempo:Auth:ReportScope must be OwnTime or WholeGroup (got '<raw>').")`. Wrap the `Get<TempoOptions>()` call so a binder `InvalidOperationException` is rethrown with the same message (inner exception kept).
  - In `OwnTimeOnlyUsernameAsync`, after the `UsesSignIn` check: `if (options.Auth.ReportScope == ReportScope.WholeGroup) return null;`. Update the comment above the function.

- [ ] **Step 4: Run** `cd server && dotnet test` — Expected: all PASS (existing OwnTime tests unchanged).

- [ ] **Step 5: README** — add row `| Tempo:Auth:ReportScope | OwnTime | OwnTime: only Maintainers/Owners/admins see everyone's time. WholeGroup: everyone who signs in sees the whole group |`; under "Who sees what", one sentence noting the table applies with `OwnTime`, and `Tempo__Auth__ReportScope=WholeGroup` lets everyone see everyone.

- [ ] **Step 6: Commit**

```bash
git add server README.md docs/superpowers
git commit -m "feat: Add Tempo:Auth:ReportScope to let everyone see the whole group"
```
