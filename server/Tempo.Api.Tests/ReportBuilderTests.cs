using System.Text.Json;
using Tempo.Api;
using Tempo.Api.GitLab;
using Tempo.Api.Reports;

namespace Tempo.Api.Tests;

public class ReportBuilderTests
{
    private static readonly TimeZoneInfo Manila = new TempoOptions().ResolveTimeZone();
    private static readonly IReadOnlyList<DayOfWeek> WorkDays = new TempoOptions().ResolvedWorkDays;
    private static readonly GroupInfo Group = new("acme", "Acme", null);
    private static readonly GitLabUser Ana = new("ana", "Ana Cruz", null);
    private static readonly GitLabUser Ben = new("ben", "Ben Santos", null);

    // Week of Mon 2026-11-16 .. Sun 2026-11-22
    private static readonly DateOnly Monday = new(2026, 11, 16);

    private static Timelog Log(GitLabUser user, string utc, double hours, string iid = "1", bool mr = false) =>
        new(DateTimeOffset.Parse(utc), (long)(hours * 3600), user,
            mr ? null : new IssueRef("acme/web", iid, $"Issue {iid}", $"https://gitlab.com/acme/web/-/issues/{iid}"),
            mr, "acme/web", null);

    private static WeeklyReport Build(IEnumerable<Timelog> logs, IEnumerable<GitLabUser>? members = null, DateOnly? to = null) =>
        ReportBuilder.Build(Group, Monday, to ?? Monday, logs, members, Manila, WorkDays, DateTimeOffset.UnixEpoch);

    [Fact]
    public void WeekStart_snaps_to_monday()
    {
        Assert.Equal(Monday, ReportBuilder.WeekStart(new DateOnly(2026, 11, 22))); // Sunday
        Assert.Equal(Monday, ReportBuilder.WeekStart(Monday));
        Assert.Equal(Monday, ReportBuilder.WeekStart(new DateOnly(2026, 11, 20))); // Friday
    }

    [Fact]
    public void Days_are_monday_to_friday()
    {
        var report = Build([]);
        Assert.Equal(5, report.Days.Count);
        Assert.Equal(Monday, report.Days[0]);
        Assert.Equal(new DateOnly(2026, 11, 20), report.Days[^1]);
        Assert.Equal("Nov 16 – 20", Assert.Single(report.Weeks).Label);
    }

    [Fact]
    public void Entries_are_bucketed_by_manila_date_not_utc()
    {
        // 2026-11-16 17:00 UTC is Tue 2026-11-17 01:00 in Manila.
        var report = Build([Log(Ana, "2026-11-16T17:00:00Z", 2)]);
        var ana = Assert.Single(report.People);
        Assert.Equal(0, ana.DayHours["2026-11-16"]);
        Assert.Equal(2, ana.DayHours["2026-11-17"]);
    }

    [Fact]
    public void Monday_morning_manila_logged_as_sunday_utc_counts_for_monday()
    {
        // Sun 2026-11-15 20:00 UTC = Mon 2026-11-16 04:00 Manila.
        var report = Build([Log(Ana, "2026-11-15T20:00:00Z", 1.5)]);
        Assert.Equal(1.5, report.People[0].DayHours["2026-11-16"]);
        Assert.Equal(1.5, report.Team.TotalHours);
    }

    [Fact]
    public void Weekend_time_is_excluded_but_reported()
    {
        var report = Build([
            Log(Ana, "2026-11-17T02:00:00Z", 3),
            Log(Ana, "2026-11-21T02:00:00Z", 4), // Saturday
        ]);
        Assert.Equal(3, report.Team.TotalHours);
        Assert.Equal(4, report.Excluded.NonWorkdayHours);
        Assert.Equal(1, report.Excluded.NonWorkdayEntries);
    }

    [Fact]
    public void Merge_request_time_is_excluded_but_reported()
    {
        var report = Build([
            Log(Ana, "2026-11-17T02:00:00Z", 3),
            Log(Ana, "2026-11-17T03:00:00Z", 1, mr: true),
        ]);
        Assert.Equal(3, report.Team.TotalHours);
        Assert.Equal(1, report.Excluded.MergeRequestHours);
    }

    [Fact]
    public void Entries_outside_the_period_are_ignored()
    {
        var report = Build([
            Log(Ana, "2026-11-13T02:00:00Z", 8), // previous Friday
            Log(Ana, "2026-11-23T02:00:00Z", 8), // next Monday
        ]);
        Assert.Equal(0, report.Team.TotalHours);
        Assert.Equal(0, report.Excluded.NonWorkdayHours);
    }

    [Fact]
    public void Negative_entries_reduce_totals()
    {
        var report = Build([
            Log(Ana, "2026-11-17T02:00:00Z", 3),
            Log(Ana, "2026-11-17T04:00:00Z", -1),
        ]);
        Assert.Equal(2, report.People[0].TotalHours);
        var issue = Assert.Single(report.People[0].Issues);
        Assert.Equal(2, issue.Hours);
        Assert.Equal(2, issue.Entries);
    }

    [Fact]
    public void Team_totals_and_average_cover_people_with_time()
    {
        var report = Build(
            [
                Log(Ana, "2026-11-16T01:00:00Z", 6, "1"),
                Log(Ana, "2026-11-17T01:00:00Z", 2, "2"),
                Log(Ben, "2026-11-16T01:00:00Z", 4, "1"),
            ],
            members: [Ana, Ben, new GitLabUser("cy", "Cy Reyes", null)]);

        Assert.Equal(12, report.Team.TotalHours);
        Assert.Equal(2, report.Team.PeopleWithTime);
        Assert.Equal(3, report.Team.PeopleListed);
        Assert.Equal(6, report.Team.AverageHoursPerPerson);
        Assert.Equal(2, report.Team.IssueCount);
        Assert.Equal(10, report.Team.DayHours["2026-11-16"]);
        Assert.Equal(["ana", "ben", "cy"], report.People.Select(p => p.Username));
        Assert.Equal(0, report.People[2].TotalHours);
        Assert.True(report.MembersIncluded);
    }

    [Fact]
    public void Multi_week_report_splits_hours_per_week()
    {
        var report = Build(
            [
                Log(Ana, "2026-11-17T01:00:00Z", 5),
                Log(Ana, "2026-11-24T01:00:00Z", 7),
            ],
            to: new DateOnly(2026, 11, 27));

        Assert.Equal(2, report.Weeks.Count);
        Assert.Equal(10, report.Days.Count);
        Assert.Equal(5, report.People[0].WeekHours["2026-11-16"]);
        Assert.Equal(7, report.People[0].WeekHours["2026-11-23"]);
        Assert.Equal(12, report.Team.TotalHours);
        Assert.Equal(new DateOnly(2026, 11, 27), report.To);
    }

    [Fact]
    public void Query_window_pads_the_manila_week()
    {
        var (start, end) = ReportBuilder.QueryWindow(Monday, Monday, Manila);
        // Mon 00:00 Manila = Sun 16:00 UTC, minus one day of padding.
        Assert.Equal(DateTimeOffset.Parse("2026-11-14T16:00:00Z"), start);
        Assert.Equal(DateTimeOffset.Parse("2026-11-23T16:00:00Z"), end);
    }

    [Fact]
    public void Parses_a_graphql_timelog_node()
    {
        var json = """
            {
              "spentAt": "2026-11-17T00:00:00Z",
              "timeSpent": 5400,
              "summary": "Fixed login",
              "user": { "username": "ana", "name": "Ana Cruz", "avatarUrl": "/uploads/a.png" },
              "issue": { "iid": "42", "title": "Login bug", "webUrl": "https://gitlab.com/acme/web/-/issues/42" },
              "mergeRequest": null,
              "project": { "fullPath": "acme/web" }
            }
            """;
        var log = GitLabClient.ReadTimelog(JsonDocument.Parse(json).RootElement);
        Assert.NotNull(log);
        Assert.Equal(5400, log.Seconds);
        Assert.Equal("42", log.Issue!.Iid);
        Assert.Equal("acme/web", log.Issue.ProjectPath);
        Assert.False(log.IsMergeRequest);
    }
}
