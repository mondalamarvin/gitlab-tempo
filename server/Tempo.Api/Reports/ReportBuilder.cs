using System.Globalization;
using Tempo.Api.GitLab;

namespace Tempo.Api.Reports;

/// <summary>
/// Pure aggregation: raw GitLab timelogs in, weekly report out. No I/O, so it is easy to test.
/// </summary>
public static class ReportBuilder
{
    /// <summary>Monday of the week that contains <paramref name="date"/>.</summary>
    public static DateOnly WeekStart(DateOnly date) => date.AddDays(-(((int)date.DayOfWeek + 6) % 7));

    /// <summary>
    /// The UTC window to ask GitLab for. Padded by a day on each side so entries near midnight
    /// are never lost; the exact cut is made later in local time.
    /// </summary>
    public static (DateTimeOffset Start, DateTimeOffset End) QueryWindow(DateOnly from, DateOnly to, TimeZoneInfo tz)
    {
        var start = LocalMidnightUtc(WeekStart(from), tz).AddDays(-1);
        var end = LocalMidnightUtc(WeekStart(to).AddDays(7), tz).AddDays(1);
        return (start, end);
    }

    public static WeeklyReport Build(
        GroupInfo group,
        DateOnly from,
        DateOnly to,
        IEnumerable<Timelog> timelogs,
        IEnumerable<GitLabUser>? members,
        TimeZoneInfo tz,
        IReadOnlyList<DayOfWeek> workDays,
        DateTimeOffset generatedAt)
    {
        var firstMonday = WeekStart(from);
        var lastMonday = WeekStart(to);
        var periodEnd = lastMonday.AddDays(6); // Sunday
        var workDaySet = workDays.ToHashSet();

        var weeks = new List<WeekInfo>();
        var days = new List<DateOnly>();
        for (var monday = firstMonday; monday <= lastMonday; monday = monday.AddDays(7))
        {
            var weekDays = Enumerable.Range(0, 7).Select(monday.AddDays).Where(d => workDaySet.Contains(d.DayOfWeek)).ToList();
            days.AddRange(weekDays);
            var end = weekDays.Count > 0 ? weekDays[^1] : monday.AddDays(6);
            weeks.Add(new WeekInfo(Key(monday), weekDays.Count > 0 ? weekDays[0] : monday, end, WeekLabel(weekDays.Count > 0 ? weekDays[0] : monday, end)));
        }

        var people = new Dictionary<string, PersonAcc>(StringComparer.OrdinalIgnoreCase);
        var teamWeek = new Dictionary<string, long>();
        var teamDay = new Dictionary<string, long>();
        var issueKeys = new HashSet<string>();
        long nonWorkdaySeconds = 0, mrSeconds = 0;
        int nonWorkdayEntries = 0, mrEntries = 0;

        foreach (var log in timelogs)
        {
            var local = TimeZoneInfo.ConvertTime(log.SpentAt, tz);
            var date = DateOnly.FromDateTime(local.DateTime);
            if (date < firstMonday || date > periodEnd) continue; // outside the padded window

            if (log.Issue is null)
            {
                // Issues only: merge request (or other) time is reported as excluded.
                mrSeconds += log.Seconds;
                mrEntries++;
                continue;
            }

            if (!workDaySet.Contains(date.DayOfWeek))
            {
                nonWorkdaySeconds += log.Seconds;
                nonWorkdayEntries++;
                continue;
            }

            var weekKey = Key(WeekStart(date));
            var dayKey = Key(date);

            if (!people.TryGetValue(log.User.Username, out var person))
                people[log.User.Username] = person = new PersonAcc(log.User);

            person.Total += log.Seconds;
            Add(person.Week, weekKey, log.Seconds);
            Add(person.Day, dayKey, log.Seconds);

            var issueKey = $"{log.Issue.ProjectPath}#{log.Issue.Iid}";
            issueKeys.Add(issueKey);
            if (!person.Issues.TryGetValue(issueKey, out var issue))
                person.Issues[issueKey] = issue = new IssueAcc(log.Issue);
            issue.Seconds += log.Seconds;
            issue.Entries++;

            Add(teamWeek, weekKey, log.Seconds);
            Add(teamDay, dayKey, log.Seconds);
        }

        var membersIncluded = members is not null;
        if (members is not null)
            foreach (var m in members)
                people.TryAdd(m.Username, new PersonAcc(m));

        var report = people.Values
            .Select(p => new PersonReport(
                p.User.Username,
                p.User.Name,
                p.User.AvatarUrl,
                Hours(p.Total),
                weeks.ToDictionary(w => w.Key, w => Hours(p.Week.GetValueOrDefault(w.Key))),
                days.ToDictionary(Key, d => Hours(p.Day.GetValueOrDefault(Key(d)))),
                p.Issues.Values
                    .OrderByDescending(i => i.Seconds)
                    .Select(i => new IssueHours(i.Issue.ProjectPath, i.Issue.Iid, i.Issue.Title, i.Issue.WebUrl, Hours(i.Seconds), i.Entries))
                    .ToList()))
            .OrderByDescending(p => p.TotalHours)
            .ThenBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var teamTotal = people.Values.Sum(p => p.Total);
        var withTime = people.Values.Count(p => p.Total != 0);

        var team = new TeamSummary(
            Hours(teamTotal),
            weeks.ToDictionary(w => w.Key, w => Hours(teamWeek.GetValueOrDefault(w.Key))),
            days.ToDictionary(Key, d => Hours(teamDay.GetValueOrDefault(Key(d)))),
            withTime,
            report.Count,
            issueKeys.Count,
            withTime == 0 ? 0 : Math.Round(teamTotal / 3600.0 / withTime, 2));

        return new WeeklyReport(
            group,
            firstMonday,
            weeks.Count > 0 ? weeks[^1].End : periodEnd,
            tz.Id,
            weeks,
            days,
            report,
            team,
            new ExcludedSummary(Hours(nonWorkdaySeconds), nonWorkdayEntries, Hours(mrSeconds), mrEntries),
            membersIncluded,
            generatedAt);
    }

    private static DateTimeOffset LocalMidnightUtc(DateOnly date, TimeZoneInfo tz)
    {
        var local = date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);
        return new DateTimeOffset(local, tz.GetUtcOffset(local)).ToUniversalTime();
    }

    private static string Key(DateOnly d) => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static string WeekLabel(DateOnly start, DateOnly end)
    {
        var c = CultureInfo.InvariantCulture;
        if (start.Year != end.Year) return $"{start.ToString("MMM d, yyyy", c)} – {end.ToString("MMM d, yyyy", c)}";
        if (start.Month != end.Month) return $"{start.ToString("MMM d", c)} – {end.ToString("MMM d", c)}";
        return $"{start.ToString("MMM d", c)} – {end.Day}";
    }

    private static double Hours(long seconds) => Math.Round(seconds / 3600.0, 2);

    private static void Add(Dictionary<string, long> map, string key, long value) =>
        map[key] = map.GetValueOrDefault(key) + value;

    private sealed class PersonAcc(GitLabUser user)
    {
        public GitLabUser User { get; } = user;
        public long Total;
        public Dictionary<string, long> Week { get; } = [];
        public Dictionary<string, long> Day { get; } = [];
        public Dictionary<string, IssueAcc> Issues { get; } = [];
    }

    private sealed class IssueAcc(IssueRef issue)
    {
        public IssueRef Issue { get; } = issue;
        public long Seconds;
        public int Entries;
    }
}
