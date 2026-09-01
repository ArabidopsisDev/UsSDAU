using System.Globalization;
using System.Text.RegularExpressions;

namespace UsSDAU.Models;

public sealed record AcademicSettings
{
    public string Account { get; init; } = "";
    public string Term { get; init; } = TermPolicy.InferCurrentTerm();
    public string ScoreTerm { get; init; } = TermPolicy.InferCurrentTerm();
    public DateTime TermStartDate { get; init; } = TermPolicy.InferTermStart(TermPolicy.InferCurrentTerm());
    public bool StartWithWindows { get; init; }
    public bool IslandEnabled { get; init; } = true;
    public bool ReminderEnabled { get; init; } = true;
}

public record Course
{
    public int Day { get; init; }
    public int StartSlot { get; init; }
    public int SlotCount { get; init; }
    public string Name { get; init; } = "";
    public string Room { get; init; } = "";
    public string Teacher { get; init; } = "";
    public string Weeks { get; init; } = "";
    public string CourseCode { get; init; } = "";
}

public sealed record PublicCourse : Course
{
    public string College { get; init; } = "";
    public string Grade { get; init; } = "";
    public string Major { get; init; } = "";
    public string ClassName { get; init; } = "";
}

public sealed record ScoreRecord
{
    public string CourseCode { get; init; } = "";
    public string CourseName { get; init; } = "";
    public string Credit { get; init; } = "";
    public string Score { get; init; } = "-";
    public string Gpa { get; init; } = "-";
    public string StudentIdRaw { get; init; } = "";
    public string TeachingTaskId { get; init; } = "";
    public string ScoreRecordId { get; init; } = "";
    public double? NumericScore => ScorePolicy.ToNumeric(Score);
}

public sealed record ScoreResult
{
    public string Term { get; init; } = "";
    public List<ScoreRecord> Records { get; init; } = [];
    public string AverageScore { get; init; } = "-";
    public string AverageCreditGpa { get; init; } = "-";
    public string TotalCredits { get; init; } = "-";
}

public sealed record ScoreDetail
{
    public string UsualScore { get; init; } = "-";
    public string UsualRatio { get; init; } = "-";
    public string FinalScore { get; init; } = "-";
    public string FinalRatio { get; init; } = "-";
    public string TotalScore { get; init; } = "-";
}

public sealed record StudentProfile(string Name, string StudentId);

public sealed record CourseOccurrence(Course Course, DateTime Start, DateTime End, int Week);

public static class ScorePolicy
{
    public static double? ToNumeric(string value) => value.Trim().Replace(" ", "") switch
    {
        "优秀" => 95,
        "良好" => 85,
        "中等" => 75,
        "及格" => 65,
        "不及格" or "不合格" => 0,
        var raw when double.TryParse(raw, NumberStyles.Any, CultureInfo.InvariantCulture, out var score) => score,
        _ => null
    };

    public static double? ToGradePoint(string value)
    {
        var score = ToNumeric(value);
        return score is null ? null : score < 60 ? 0 : score / 10 - 5;
    }

    public static ScoreResult Recalculate(string term, IEnumerable<ScoreRecord> selected, IEnumerable<ScoreRecord>? all = null)
    {
        var records = selected.Select(x => x with
        {
            Gpa = ToGradePoint(x.Score)?.ToString("0.0", CultureInfo.InvariantCulture) ?? "-"
        }).ToList();
        var allRecords = (all ?? records).ToList();
        var numeric = allRecords.Select(x => ToNumeric(x.Score)).Where(x => x.HasValue).Select(x => x!.Value).ToList();
        var weighted = allRecords.Select(x =>
        {
            var okCredit = double.TryParse(x.Credit, NumberStyles.Any, CultureInfo.InvariantCulture, out var credit);
            return (okCredit ? credit : 0, Gpa: ToGradePoint(x.Score));
        }).Where(x => x.Item1 > 0 && x.Gpa.HasValue).ToList();
        var credits = records.Sum(x => double.TryParse(x.Credit, NumberStyles.Any, CultureInfo.InvariantCulture, out var c) ? c : 0);
        return new ScoreResult
        {
            Term = term,
            Records = records,
            AverageScore = numeric.Count == 0 ? "-" : numeric.Average().ToString("0.00", CultureInfo.InvariantCulture),
            AverageCreditGpa = weighted.Count == 0 ? "-" : (weighted.Sum(x => x.Item1 * x.Gpa!.Value) / weighted.Sum(x => x.Item1)).ToString("0.00", CultureInfo.InvariantCulture),
            TotalCredits = credits.ToString("0.#", CultureInfo.InvariantCulture)
        };
    }
}

public static class TermPolicy
{
    public static string InferCurrentTerm(DateTime? value = null)
    {
        var today = (value ?? DateTime.Today).Date;
        if (today.Month > 7 || (today.Month == 7 && today.Day >= 20)) return $"{today.Year}-{today.Year + 1}-1";
        if (today.Month > 2 || (today.Month == 2 && today.Day >= 16)) return $"{today.Year - 1}-{today.Year}-2";
        return $"{today.Year - 1}-{today.Year}-1";
    }

    public static DateTime InferTermStart(string term)
    {
        if (term == "2026-2027-1") return new DateTime(2026, 9, 7);
        var parts = term.Split('-');
        var year = int.TryParse(parts.ElementAtOrDefault(0), out var y) ? y : DateTime.Today.Year;
        var date = parts.ElementAtOrDefault(2) == "2" ? new DateTime(year + 1, 2, 1) : new DateTime(year, 9, 1);
        while (date.DayOfWeek != DayOfWeek.Monday) date = date.AddDays(1);
        return date;
    }

    public static IReadOnlyList<string> ScoreTermOptions(string account, DateTime? value = null)
    {
        var current = InferCurrentTerm(value);
        var currentStart = int.Parse(current.Split('-')[0]);
        var enrollment = int.TryParse(account[..Math.Min(4, account.Length)], out var year) && year is >= 2000 and <= 2100
            ? Math.Min(year, currentStart)
            : Math.Max(2020, currentStart - 3);
        var result = new List<string>();
        var term = $"{enrollment}-{enrollment + 1}-1";
        while (TermOrder(term) <= TermOrder(current))
        {
            result.Add(term);
            term = NextTerm(term);
        }
        return result.AsEnumerable().Reverse().ToList();
    }

    public static string NextTerm(string term)
    {
        var parts = term.Split('-');
        if (parts.Length != 3 || !int.TryParse(parts[0], out var start)) return term;
        return parts[2] == "1" ? $"{start}-{start + 1}-2" : $"{start + 1}-{start + 2}-1";
    }

    private static int TermOrder(string term)
    {
        var parts = term.Split('-');
        return parts.Length == 3 && int.TryParse(parts[0], out var start) && int.TryParse(parts[2], out var number)
            ? start * 2 + number - 1 : int.MinValue;
    }

    public static int WeekForDate(DateTime termStart, DateTime date)
    {
        var days = (date.Date - termStart.Date).Days;
        return days < 0 ? 0 : days / 7 + 1;
    }
}

public static class SchedulePolicy
{
    private static readonly TimeOnly[] WinterStarts =
    [new(8,0), new(8,55), new(10,0), new(10,55), new(14,0), new(14,55), new(16,0), new(16,55), new(19,0), new(19,55)];
    private static readonly TimeOnly[] WinterEnds =
    [new(8,45), new(9,40), new(10,45), new(11,40), new(14,45), new(15,40), new(16,45), new(17,40), new(19,45), new(20,40)];
    private static readonly TimeOnly[] SummerStarts =
    [new(8,0), new(8,55), new(10,0), new(10,55), new(14,30), new(15,25), new(16,30), new(17,25), new(19,30), new(20,25)];
    private static readonly TimeOnly[] SummerEnds =
    [new(8,45), new(9,40), new(10,45), new(11,40), new(15,15), new(16,10), new(17,15), new(18,10), new(20,15), new(21,10)];

    public static bool IsSummer(DateTime date) => date.Month is >= 5 and <= 7 || date.Month == 8 && date.Day == 1;

    public static (DateTime Start, DateTime End) Resolve(DateTime date, int startSlot, int slotCount)
    {
        var starts = IsSummer(date) ? SummerStarts : WinterStarts;
        var ends = IsSummer(date) ? SummerEnds : WinterEnds;
        var first = Math.Clamp(startSlot, 0, 9);
        var last = Math.Clamp(first + Math.Max(1, slotCount) - 1, first, 9);
        return (date.Date + starts[first].ToTimeSpan(), date.Date + ends[last].ToTimeSpan());
    }

    public static string BigSectionLabel(int startSlot) => Math.Clamp(startSlot / 2, 0, 4) switch
    {
        0 => "早一", 1 => "早二", 2 => "午一", 3 => "午二", _ => "晚间"
    };

    public static string TimeLabel(DateTime date, int startSlot, int slotCount)
    {
        var (start, end) = Resolve(date, startSlot, slotCount);
        return $"{start:HH:mm}–{end:HH:mm}";
    }

    public static bool IsVisibleInWeek(string weeks, int week)
    {
        if (week <= 0) return false;
        var normalized = weeks.Replace("周", "").Replace("—", "-").Replace("至", "-");
        var matches = Regex.Matches(normalized, @"(\d+)(?:\s*-\s*(\d+))?");
        if (matches.Count == 0) return true;
        return matches.Cast<Match>().Any(m =>
        {
            var start = int.Parse(m.Groups[1].Value);
            var end = m.Groups[2].Success ? int.Parse(m.Groups[2].Value) : start;
            return week >= start && week <= end;
        });
    }

    public static int DayIndex(DateTime date) => ((int)date.DayOfWeek + 6) % 7;
}
