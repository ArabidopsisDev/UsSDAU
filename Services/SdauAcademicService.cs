using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using UsSDAU.Models;

namespace UsSDAU.Services;

public sealed record ScoreBatchQueryResult(
    Dictionary<string, ScoreResult> Results,
    IReadOnlyList<string> EmptyTerms,
    IReadOnlyList<string> FailedTerms);

public sealed partial class SdauAcademicService
{
    private const string BaseUrl = "https://jw.sdau.edu.cn";
    private const string PublicMirror = "https://gitee.com/sleexy/onlinedata/raw/master";

    static SdauAcademicService() => Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

    public async Task<List<Course>> QueryCoursesAsync(string account, string password, string term, CancellationToken ct = default)
    {
        using var session = await LoginAsync(account, password, ct);
        var selectedPath = "/xkgl/loadXsxkjgList?lx=xkrz&type=list&pageNum=1&pageSize=200&xnxqid=" + Uri.EscapeDataString(term);
        var selected = ParseCourses(await session.RequestAsync(selectedPath, HttpMethod.Get, ct: ct));
        List<PersonalMeeting> personal;
        try
        {
            personal = ParsePersonalTimetable(await session.RequestAsync(
                "/xskb/xskb_list.do?viweType=0&xnxq01id=" + Uri.EscapeDataString(term), HttpMethod.Get, ct: ct));
        }
        catch { personal = []; }

        return selected.Select(course =>
        {
            var related = personal.Where(x =>
                (!string.IsNullOrWhiteSpace(course.CourseCode) && x.Code == course.CourseCode) || x.Name == course.Name).ToList();
            var exact = related.Where(x => x.Day == course.Day && x.StartSlot == course.StartSlot && x.SlotCount == course.SlotCount).ToList();
            var sameTime = related.Where(x => x.StartSlot == course.StartSlot && x.SlotCount == course.SlotCount).ToList();
            var weeks = NormalizeWeeks(string.Join(',', (exact.Count > 0 ? exact : sameTime.Count > 0 ? sameTime : related).Select(x => x.Weeks)));
            var withWeeks = string.IsNullOrWhiteSpace(weeks) ? course : course with { Weeks = weeks };
            return NormalizeCourseBlock(withWeeks);
        }).Distinct().ToList();
    }

    public async Task<StudentProfile> QueryStudentProfileAsync(string account, string password, CancellationToken ct = default)
    {
        using var session = await LoginAsync(account, password, ct);
        foreach (var path in new[] { "/framework/xsMainV_new.htmlx?t1=1", "/framework/xsMainV.jsp" })
        {
            var html = await session.RequestAsync(path, HttpMethod.Get, ct: ct);
            var title = Clean(Regex.Match(html, "<[^>]*class\\s*=\\s*[\\\"'][^\\\"']*\\binfoContentTitle\\b[^\\\"']*[\\\"'][^>]*>([\\s\\S]*?)</[^>]+>", RegexOptions.IgnoreCase).Groups[1].Value);
            var match = Regex.Match(title, "(.+?)-([0-9]{6,})$");
            if (match.Success) return new StudentProfile(match.Groups[1].Value.Trim(), match.Groups[2].Value.Trim());
        }
        throw new InvalidOperationException("未能从教务系统解析个人信息。");
    }

    public async Task<ScoreResult> QueryScoresAsync(string account, string password, string term, CancellationToken ct = default)
    {
        using var session = await LoginAsync(account, password, ct);
        const string formPath = "/kscj/cjcx_frm";
        await session.RequestAsync(formPath, HttpMethod.Get, ct: ct);
        var path = BuildScorePath(term);
        var body = await session.RequestAsync(path, HttpMethod.Get, BaseUrl + formPath, ct: ct);
        var records = ParseScoreRecords(body, term);
        return ScorePolicy.Recalculate(term, records);
    }

    public async Task<ScoreBatchQueryResult> QueryScoresAsync(
        string account,
        string password,
        IReadOnlyList<string> terms,
        IProgress<(int Current, int Total, string Term)>? progress = null,
        CancellationToken ct = default)
    {
        var results = new Dictionary<string, ScoreResult>();
        var emptyTerms = new List<string>();
        var failedTerms = new List<string>();
        Exception? lastError = null;
        for (var index = 0; index < terms.Count; index++)
        {
            var term = terms[index];
            progress?.Report((index + 1, terms.Count, term));
            try
            {
                // The teaching system can invalidate or throttle a session after several
                // consecutive score queries. Use a fresh authenticated session per term
                // so older semesters are not silently lost from the cumulative transcript.
                var result = await QueryScoresAsync(account, password, term, ct);
                if (result.Records.Count > 0) results[term] = result;
                else emptyTerms.Add(term);
            }
            catch (Exception error) when (!ct.IsCancellationRequested)
            {
                lastError = error;
                failedTerms.Add(term);
            }
            if (index + 1 < terms.Count) await Task.Delay(180, ct);
        }
        if (results.Count == 0 && lastError is not null) throw new InvalidOperationException("所有学期成绩查询均失败。", lastError);
        return new ScoreBatchQueryResult(results, emptyTerms, failedTerms);
    }

    public async Task<ScoreDetail> QueryScoreDetailAsync(string account, string password, ScoreRecord score, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(score.StudentIdRaw) || string.IsNullOrWhiteSpace(score.TeachingTaskId) || string.IsNullOrWhiteSpace(score.ScoreRecordId))
            throw new InvalidOperationException("该课程缺少成绩明细参数。");
        using var session = await LoginAsync(account, password, ct);
        var query = new Dictionary<string, string>
        {
            ["xs0101id"] = score.StudentIdRaw, ["jx0404id"] = score.TeachingTaskId,
            ["cj0708id"] = score.ScoreRecordId, ["zcj"] = score.Score
        };
        var path = "/kscj/pscj_list.do?" + string.Join('&', query.Select(x => $"{Uri.EscapeDataString(x.Key)}={Uri.EscapeDataString(x.Value)}"));
        var body = await session.RequestAsync(path, HttpMethod.Get, ct: ct);
        var array = Regex.Match(body.TrimStart('\uFEFF'), "(?:let\\s+)?arr\\s*=\\s*(\\[[\\s\\S]*?]);?", RegexOptions.IgnoreCase).Groups[1].Value;
        if (string.IsNullOrWhiteSpace(array)) throw new InvalidOperationException("成绩构成接口返回格式异常。");
        using var document = JsonDocument.Parse(array);
        var row = document.RootElement.EnumerateArray().FirstOrDefault();
        if (row.ValueKind != JsonValueKind.Object) throw new InvalidOperationException("暂无成绩构成数据。");
        return new ScoreDetail
        {
            UsualScore = Text(row, "cjxm3").Default("-"), UsualRatio = Text(row, "cjxm3bl").Default("-"),
            FinalScore = Text(row, "cjxm1").Default("-"), FinalRatio = Text(row, "cjxm1bl").Default("-"),
            TotalScore = Text(row, "zcj").Default(score.Score.Default("-"))
        };
    }

    public async Task<List<PublicCourse>> DownloadPublicScheduleAsync(string term, CancellationToken ct = default)
    {
        var parts = term.Split('-');
        if (parts.Length != 3 || !int.TryParse(parts[0], out var start) || !int.TryParse(parts[1], out var end) || parts[2] is not ("1" or "2"))
            throw new ArgumentException("学期格式不正确。");
        var file = $"sc{start % 100:00}-{end % 100:00}-{parts[2]}.json";
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("UsSDAU-Windows/1.0");
        await using var stream = await client.GetStreamAsync($"{PublicMirror}/{file}", ct);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
        var result = new List<PublicCourse>();
        WalkPublic(document.RootElement, result);
        return result.Distinct().ToList();
    }

    private static async Task<AcademicSession> LoginAsync(string account, string password, CancellationToken ct)
    {
        var session = new AcademicSession();
        try
        {
            var page = await session.RequestAsync("/", HttpMethod.Get, ct: ct);
            var seed = Capture(page, "var\\s+scode\\s*=\\s*['\"](.*?)['\"]");
            var sxh = Capture(page, "var\\s+sxh\\s*=\\s*['\"](.*?)['\"]");
            if (string.IsNullOrEmpty(seed) || string.IsNullOrEmpty(sxh)) throw new InvalidOperationException("无法提取登录动态参数。");
            var form = new Dictionary<string, string>
            {
                ["loginMethod"] = "LoginToXk", ["userlanguage"] = "0", ["userAccount"] = account,
                ["userPassword"] = "", ["encoded"] = BuildCredential(account, password, seed, sxh)
            };
            var response = await session.RequestAsync("/xk/LoginToXk", HttpMethod.Post, BaseUrl + "/", form, ct);
            var message = Clean(Capture(response, "id\\s*=\\s*['\"]showMsg['\"][^>]*>(.*?)</[^>]+>"));
            if (!string.IsNullOrWhiteSpace(message) && !message.Contains("请先登录系统")) throw new InvalidOperationException(message);
            if (IsLoginPage(response)) throw new InvalidOperationException("学号或密码错误，或教务系统未建立登录会话。");
            return session;
        }
        catch { session.Dispose(); throw; }
    }

    private static List<Course> ParseCourses(string body)
    {
        using var document = JsonDocument.Parse(body);
        if (!document.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array)
            throw new InvalidOperationException("教务系统课程数据格式变化。");
        var result = new List<Course>();
        foreach (var item in data.EnumerateArray())
        {
            var name = Clean(Text(item, "kc_mc"));
            if (string.IsNullOrWhiteSpace(name)) continue;
            var meetings = ParseMeetings(Text(item, "sksj"));
            var rooms = SplitLines(Text(item, "skdd"));
            var index = 0;
            foreach (var meeting in meetings)
            {
                result.Add(new Course
                {
                    Day = meeting.Day, StartSlot = meeting.StartSlot, SlotCount = meeting.SlotCount,
                    Name = name, Teacher = Clean(Text(item, "xm")), CourseCode = Clean(Text(item, "kch")),
                    Room = rooms.ElementAtOrDefault(index++) ?? rooms.FirstOrDefault() ?? "", Weeks = meeting.Weeks
                });
            }
        }
        return result.Distinct().ToList();
    }

    private static List<PersonalMeeting> ParsePersonalTimetable(string html)
    {
        var result = new List<PersonalMeeting>();
        var rows = Regex.Matches(html, "<tr[^>]*>(.*?)</tr>", RegexOptions.IgnoreCase | RegexOptions.Singleline);
        foreach (Match row in rows)
        {
            var cells = Regex.Matches(row.Groups[1].Value, "<td[^>]*>(.*?)</td>", RegexOptions.IgnoreCase | RegexOptions.Singleline).Cast<Match>().Select(x => x.Groups[1].Value).ToList();
            for (var day = 1; day < Math.Min(cells.Count, 8); day++)
            {
                var clean = Clean(cells[day]);
                var pattern = "([^;]+?)\\s+老师\\s*:\\s*[^;]*;\\s*时间\\s*:\\s*(.*?)\\s*\\[(\\d+)(?:-(\\d+))?节\\]\\s*;\\s*地点\\s*:\\s*([^;]+?)\\s*;\\s*课程编号\\s*:\\s*([A-Za-z0-9]+)";
                foreach (Match match in Regex.Matches(clean, pattern, RegexOptions.IgnoreCase | RegexOptions.Singleline))
                {
                    var start = int.Parse(match.Groups[3].Value) - 1;
                    var end = match.Groups[4].Success ? int.Parse(match.Groups[4].Value) - 1 : start;
                    if (start is < 0 or > 9 || end < start || end > 9) continue;
                    result.Add(new PersonalMeeting(Clean(match.Groups[1].Value), match.Groups[6].Value.Trim(), day - 1, start, end - start + 1, NormalizeWeeks(match.Groups[2].Value)));
                }
            }
        }
        return result.Distinct().ToList();
    }

    private static List<ScoreRecord> ParseScoreRecords(string body, string term)
    {
        using var document = JsonDocument.Parse(body.TrimStart('\uFEFF'));
        var rows = FindRows(document.RootElement);
        var result = new List<ScoreRecord>();
        foreach (var row in rows)
        {
            var code = Text(row, "kch", "courseCode", "kcdm");
            var name = Text(row, "kc_mc", "kcmc", "courseName");
            if (string.IsNullOrWhiteSpace(code) && string.IsNullOrWhiteSpace(name)) continue;
            result.Add(new ScoreRecord
            {
                CourseCode = code, CourseName = name, Credit = Text(row, "xf", "credit"),
                Score = Text(row, "zcj", "zcjstr", "score").Default("-"),
                StudentIdRaw = Text(row, "xs0101id"), TeachingTaskId = Text(row, "jx0404id"), ScoreRecordId = Text(row, "cj0708id")
            });
        }
        return result.DistinctBy(x => x.ScoreRecordId.Default($"{term}|{x.CourseCode}|{x.CourseName}")).ToList();
    }

    private static IEnumerable<JsonElement> FindRows(JsonElement root)
    {
        if (root.ValueKind == JsonValueKind.Array) return root.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.Object).ToList();
        foreach (var key in new[] { "rows", "data", "list", "result" })
            if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.Array)
                return value.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.Object).ToList();
        return [];
    }

    private static string BuildScorePath(string term)
    {
        var data = new Dictionary<string, string> { ["pageNum"] = "1", ["pageSize"] = "200", ["kksj"] = term, ["kcxz"] = "", ["kcsx"] = "", ["kcmc"] = "", ["xsfs"] = "all", ["sfxsbcxq"] = "1" };
        return "/kscj/cjcx_list?" + string.Join('&', data.Select(x => $"{Uri.EscapeDataString(x.Key)}={Uri.EscapeDataString(x.Value)}"));
    }

    private static void WalkPublic(JsonElement value, List<PublicCourse> result, int depth = 0)
    {
        if (depth > 5) return;
        if (value.ValueKind == JsonValueKind.Array)
        {
            foreach (var child in value.EnumerateArray()) WalkPublic(child, result, depth + 1);
            return;
        }
        if (value.ValueKind != JsonValueKind.Object) return;
        result.AddRange(ParsePublicRow(value));
        foreach (var property in value.EnumerateObject())
            if (property.Value.ValueKind is JsonValueKind.Array or JsonValueKind.Object) WalkPublic(property.Value, result, depth + 1);
    }

    private static IEnumerable<PublicCourse> ParsePublicRow(JsonElement row)
    {
        var name = Clean(Text(row, "kcmc", "kc_mc", "courseName", "course", "name"));
        if (string.IsNullOrWhiteSpace(name)) return [];
        var meetings = ParsePublicMeetings(row);
        return meetings.Select(m =>
        {
            var block = NormalizeBlock(m.StartSlot, m.SlotCount);
            return new PublicCourse
            {
                College = Clean(Text(row, "dwmc", "college", "yxmc", "skyx")), Grade = Clean(Text(row, "ksnd", "grade", "nj")).TrimEnd('级'),
                Major = Clean(Text(row, "zymc", "major", "zy")), ClassName = Clean(Text(row, "bj", "skbj", "className", "bjmc")),
                Day = m.Day, StartSlot = block.Start, SlotCount = block.Count, Weeks = m.Weeks, Name = name,
                Room = CleanLocation(Text(row, "skdd", "jxdd", "room", "location", "jsmc")),
                Teacher = Regex.Replace(Clean(Text(row, "xm", "jsxm", "teacher", "js")), "\\s*\\[[^]]*\\]", ""),
                CourseCode = Clean(Text(row, "kch", "courseCode", "kcdm"))
            };
        });
    }

    private static List<Meeting> ParsePublicMeetings(JsonElement row)
    {
        if (int.TryParse(Text(row, "day"), out var day) && int.TryParse(Text(row, "startSlot"), out var start) && int.TryParse(Text(row, "slotCount"), out var count) && day is >= 0 and <= 6 && start is >= 0 and <= 9 && count > 0)
            return [new Meeting(day, start, count, NormalizeWeeks(Text(row, "weeks", "week", "weekRange")))];
        var parsed = ParseMeetings(Text(row, "sksj", "courseTime", "time", "schedule"));
        if (parsed.Count > 0) return parsed;
        var dayText = Text(row, "zzdweek", "skxq", "xqj", "xq", "weekday", "weekDay");
        var dayMap = new Dictionary<string, int>
        {
            ["一"] = 0, ["二"] = 1, ["三"] = 2, ["四"] = 3, ["五"] = 4, ["六"] = 5, ["日"] = 6, ["天"] = 6
        };
        var resolvedDay = dayMap.FirstOrDefault(x => dayText.Contains(x.Key)).Value;
        var hasDay = dayMap.Keys.Any(dayText.Contains);
        if (!hasDay)
        {
            var dayNumber = Regex.Match(dayText, "[1-7]");
            if (!dayNumber.Success) return [];
            resolvedDay = int.Parse(dayNumber.Value) - 1;
        }
        var sections = Regex.Matches(Text(row, "jc", "jcs", "section", "sections", "skjc"), "\\d+")
            .Cast<Match>().Select(x => int.Parse(x.Value)).ToList();
        if (sections.Count == 0) return [];
        var sectionStart = sections.First() - 1;
        var sectionEnd = sections.Last() - 1;
        if (sectionStart is < 0 or > 9 || sectionEnd < sectionStart || sectionEnd > 9) return [];
        return [new Meeting(resolvedDay, sectionStart, sectionEnd - sectionStart + 1,
            NormalizeWeeks(Text(row, "kkzc", "zc", "zcmc", "weeks", "week", "weekRange")))];
    }

    private static List<Meeting> ParseMeetings(string raw)
    {
        var result = new List<Meeting>();
        var days = new Dictionary<char, int> { ['一'] = 0, ['二'] = 1, ['三'] = 2, ['四'] = 3, ['五'] = 4, ['六'] = 5, ['日'] = 6 };
        foreach (var line in SplitLines(raw))
        {
            var normalized = line.Replace("星期", "周").Replace("第", "");
            var match = Regex.Match(normalized, "周([一二三四五六日])\\s*([0-9][0-9\\s,，、-]*)\\s*节");
            if (!match.Success) continue;
            var numbers = Regex.Matches(match.Groups[2].Value, "\\d+").Cast<Match>().Select(x => int.Parse(x.Value)).ToList();
            if (numbers.Count == 1 && match.Groups[2].Value.Trim().Length >= 4 && match.Groups[2].Value.Trim().Length % 2 == 0)
                numbers = match.Groups[2].Value.Trim().Chunk(2).Select(x => int.Parse(new string(x))).ToList();
            if (numbers.Count == 0) continue;
            var start = numbers.First() - 1;
            var end = numbers.Last() - 1;
            if (start is < 0 or > 9 || end < start || end > 9) continue;
            result.Add(new Meeting(days[match.Groups[1].Value[0]], start, end - start + 1, NormalizeWeeks(normalized)));
        }
        return result.Distinct().ToList();
    }

    private static string NormalizeWeeks(string raw)
    {
        var normalized = raw.Replace("星期", "周").Replace("第", "").Replace("—", "-").Replace("至", "-");
        var weekday = Regex.Match(normalized, "周[一二三四五六日]");
        var source = weekday.Success ? normalized[..weekday.Index] : normalized;
        var matches = Regex.Matches(source.Any(char.IsDigit) ? source : normalized, "(\\d+)\\s*(?:-\\s*(\\d+))?");
        return string.Join(',', matches.Cast<Match>().Select(x => x.Groups[2].Success ? $"{x.Groups[1].Value}-{x.Groups[2].Value}" : x.Groups[1].Value).Distinct());
    }

    private static string BuildCredential(string account, string password, string seed, string sxh)
    {
        static string B64(string value) => Convert.ToBase64String(Encoding.UTF8.GetBytes(value));
        var code = $"{B64(account)}%%%{B64(password)}%%%{B64(" ")}";
        var remaining = seed;
        var output = new StringBuilder();
        for (var i = 0; i < code.Length; i++)
        {
            if (i >= 55) { output.Append(code.AsSpan(i)); break; }
            var take = i < sxh.Length && char.IsDigit(sxh[i]) ? sxh[i] - '0' : 0;
            var actual = Math.Min(take, remaining.Length);
            output.Append(code[i]); output.Append(remaining.AsSpan(0, actual)); remaining = remaining[actual..];
        }
        return output.ToString();
    }

    private static Course NormalizeCourseBlock(Course course)
    {
        var block = NormalizeBlock(course.StartSlot, course.SlotCount);
        return course with { StartSlot = block.Start, SlotCount = block.Count };
    }

    private static (int Start, int Count) NormalizeBlock(int startSlot, int slotCount)
    {
        var start = Math.Clamp(startSlot / 2 * 2, 0, 8);
        var count = Math.Max(2, (Math.Max(1, slotCount) + 1) / 2 * 2);
        return (start, Math.Min(count, 10 - start));
    }

    private static bool IsLoginPage(string body) => body.Contains("请先登录系统") || body.Contains("欢迎登录教务系统") || body.Contains("LoginToXk") && body.Contains("userAccount");
    private static string Capture(string value, string pattern) => Regex.Match(value, pattern, RegexOptions.IgnoreCase | RegexOptions.Singleline).Groups[1].Value.Trim();
    private static string Clean(string value) => Regex.Replace(WebUtility.HtmlDecode(Regex.Replace(value, "<[^>]+>", " ")), "\\s+", " ").Trim();
    private static string CleanLocation(string value) => Clean(Regex.Replace(value, "(?i)<br\\s*/?>", " / "));
    private static List<string> SplitLines(string value) => Regex.Replace(value, "(?i)<br\\s*/?>", "\n").Split('\n').Select(Clean).Where(x => x.Length > 0).ToList();
    private static string Text(JsonElement row, params string[] keys)
    {
        foreach (var key in keys)
            if (row.ValueKind == JsonValueKind.Object && row.TryGetProperty(key, out var value) && value.ValueKind is not (JsonValueKind.Null or JsonValueKind.Undefined))
                return value.ToString().Trim();
        return "";
    }

    private sealed record Meeting(int Day, int StartSlot, int SlotCount, string Weeks);
    private sealed record PersonalMeeting(string Name, string Code, int Day, int StartSlot, int SlotCount, string Weeks);

    private sealed class AcademicSession : IDisposable
    {
        private readonly HttpClient _client;
        public AcademicSession()
        {
            var handler = new HttpClientHandler { CookieContainer = new CookieContainer(), AutomaticDecompression = DecompressionMethods.None };
            _client = new HttpClient(handler) { BaseAddress = new Uri(BaseUrl), Timeout = TimeSpan.FromSeconds(30) };
            _client.DefaultRequestHeaders.UserAgent.ParseAdd("UsSDAU-Windows/1.0");
            _client.DefaultRequestHeaders.Accept.ParseAdd("text/html");
            _client.DefaultRequestHeaders.Accept.ParseAdd("application/json");
            _client.DefaultRequestHeaders.CacheControl = new CacheControlHeaderValue { NoCache = true };
            _client.DefaultRequestHeaders.TryAddWithoutValidation("Accept-Encoding", "identity");
        }

        public async Task<string> RequestAsync(string path, HttpMethod method, string? referer = null, Dictionary<string, string>? form = null, CancellationToken ct = default)
        {
            Exception? last = null;
            for (var attempt = 0; attempt < 3; attempt++)
            {
                try
                {
                    using var request = new HttpRequestMessage(method, path);
                    if (!string.IsNullOrWhiteSpace(referer)) request.Headers.Referrer = new Uri(referer);
                    if (form is not null) request.Content = new FormUrlEncodedContent(form);
                    using var response = await _client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
                    var bytes = await response.Content.ReadAsByteArrayAsync(ct);
                    var charset = response.Content.Headers.ContentType?.CharSet?.Trim('"');
                    var encoding = !string.IsNullOrWhiteSpace(charset) ? Encoding.GetEncoding(charset) : Encoding.UTF8;
                    var body = encoding.GetString(bytes);
                    if (!response.IsSuccessStatusCode) throw new HttpRequestException($"教务系统返回 HTTP {(int)response.StatusCode}");
                    return body;
                }
                catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
                {
                    last = ex;
                    if (attempt < 2) await Task.Delay(250 * (attempt + 1), ct);
                }
            }
            throw new InvalidOperationException("教务系统请求失败。", last);
        }

        public void Dispose() => _client.Dispose();
    }
}

internal static class StringFallbackExtensions
{
    public static string Default(this string value, string fallback) => string.IsNullOrWhiteSpace(value) ? fallback : value;
}
