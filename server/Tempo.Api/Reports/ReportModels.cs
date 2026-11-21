using Tempo.Api.GitLab;

namespace Tempo.Api.Reports;

/// <summary>
/// Hours per person, per week (and per day) for one group.
/// Dictionary keys are ISO dates (yyyy-MM-dd): week keys are the week's Monday.
/// </summary>
public sealed record WeeklyReport(
    GroupInfo Group,
    DateOnly From,
    DateOnly To,
    string TimeZone,
    IReadOnlyList<WeekInfo> Weeks,
    IReadOnlyList<DateOnly> Days,
    IReadOnlyList<PersonReport> People,
    TeamSummary Team,
    ExcludedSummary Excluded,
    bool MembersIncluded,
    DateTimeOffset GeneratedAt);

public sealed record WeekInfo(string Key, DateOnly Start, DateOnly End, string Label);

public sealed record PersonReport(
    string Username,
    string Name,
    string? AvatarUrl,
    double TotalHours,
    IReadOnlyDictionary<string, double> WeekHours,
    IReadOnlyDictionary<string, double> DayHours,
    IReadOnlyList<IssueHours> Issues);

public sealed record IssueHours(
    string ProjectPath,
    string Iid,
    string Title,
    string? WebUrl,
    double Hours,
    int Entries);

public sealed record TeamSummary(
    double TotalHours,
    IReadOnlyDictionary<string, double> WeekHours,
    IReadOnlyDictionary<string, double> DayHours,
    int PeopleWithTime,
    int PeopleListed,
    int IssueCount,
    double AverageHoursPerPerson);

/// <summary>Time that was found in the period but left out of the report, so totals can be reconciled.</summary>
public sealed record ExcludedSummary(
    double NonWorkdayHours,
    int NonWorkdayEntries,
    double MergeRequestHours,
    int MergeRequestEntries);
