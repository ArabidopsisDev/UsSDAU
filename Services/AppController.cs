using UsSDAU.Models;

namespace UsSDAU.Services;

public sealed class AppController(SettingsService settingsService, CacheService cache, SdauAcademicService academic)
{
    private readonly SemaphoreSlim _networkGate = new(1, 1);
    public AcademicSettings Settings { get; private set; } = settingsService.Load();
    public IReadOnlyList<Course> Courses { get; private set; } = [];
    public IReadOnlyList<PublicCourse> PublicCourses { get; private set; } = [];
    public ScoreResult? Scores { get; private set; }
    public IReadOnlyDictionary<string, ScoreResult> ScoreHistory { get; private set; } = new Dictionary<string, ScoreResult>();
    public StudentProfile? Profile { get; private set; }
    public DateTime DisplayDate { get; private set; } = DateTime.Today;
    public string Status { get; private set; } = "本地缓存";
    public bool HasAccount => !string.IsNullOrWhiteSpace(Settings.Account) && !string.IsNullOrWhiteSpace(settingsService.ReadPassword(Settings.Account));
    public event EventHandler? DataChanged;

    public async Task LoadCachedDataAsync()
    {
        if (string.IsNullOrWhiteSpace(Settings.Account)) return;
        Courses = await cache.LoadCoursesAsync(Settings.Account, Settings.Term);
        Scores = await cache.LoadScoresAsync(Settings.Account, Settings.ScoreTerm);
        ScoreHistory = await cache.LoadScoresAsync(Settings.Account, TermPolicy.ScoreTermOptions(Settings.Account));
        PublicCourses = await cache.LoadPublicCoursesAsync(Settings.Term);
        Status = Courses.Count == 0 ? "尚未同步课表" : $"已加载 {Courses.Count} 条课程安排";
        DataChanged?.Invoke(this, EventArgs.Empty);
    }

    public async Task ConfigureAsync(AcademicSettings settings, string password, bool validate)
    {
        var scoreTerms = TermPolicy.ScoreTermOptions(settings.Account);
        settings = settings with
        {
            ScoreTerm = scoreTerms.Contains(settings.ScoreTerm) ? settings.ScoreTerm : scoreTerms.First()
        };
        if (validate)
        {
            await _networkGate.WaitAsync();
            try
            {
                var result = await academic.QueryCoursesAsync(settings.Account, password, settings.Term);
                settingsService.Save(settings, password);
                Settings = settings;
                Courses = result;
                await cache.SaveCoursesAsync(settings.Account, settings.Term, result);
                Profile = await TryLoadProfileAsync(password);
            }
            finally { _networkGate.Release(); }
        }
        else
        {
            settingsService.Save(settings, password);
            Settings = settings;
        }
        Status = $"{Settings.Account} · {Settings.Term}";
        DataChanged?.Invoke(this, EventArgs.Empty);
    }

    public async Task RefreshCoursesAsync()
    {
        EnsureCredentials(out var account, out var password);
        await _networkGate.WaitAsync();
        try
        {
            var result = await academic.QueryCoursesAsync(account, password, Settings.Term);
            Courses = result;
            await cache.SaveCoursesAsync(account, Settings.Term, result);
            Status = $"课表更新于 {DateTime.Now:HH:mm}";
        }
        finally { _networkGate.Release(); }
        DataChanged?.Invoke(this, EventArgs.Empty);
    }

    public async Task RefreshScoresAsync(string? term = null)
    {
        EnsureCredentials(out var account, out var password);
        var scoreTerm = string.IsNullOrWhiteSpace(term) ? Settings.ScoreTerm : term;
        await _networkGate.WaitAsync();
        try
        {
            Scores = await academic.QueryScoresAsync(account, password, scoreTerm);
            await cache.SaveScoresAsync(account, scoreTerm, Scores);
            var updated = ScoreHistory.ToDictionary(x => x.Key, x => x.Value);
            if (Scores.Records.Count > 0) updated[scoreTerm] = Scores;
            ScoreHistory = updated;
            Settings = Settings with { ScoreTerm = scoreTerm };
            settingsService.Save(Settings, "");
            Profile ??= await TryLoadProfileAsync(password);
            Status = $"成绩更新于 {DateTime.Now:HH:mm}";
        }
        finally { _networkGate.Release(); }
        DataChanged?.Invoke(this, EventArgs.Empty);
    }

    public async Task RefreshAllScoresAsync(IProgress<(int Current, int Total, string Term)>? progress = null)
    {
        EnsureCredentials(out var account, out var password);
        var terms = TermPolicy.ScoreTermOptions(account);
        await _networkGate.WaitAsync();
        try
        {
            var results = await academic.QueryScoresAsync(account, password, terms, progress);
            foreach (var item in results) await cache.SaveScoresAsync(account, item.Key, item.Value);
            ScoreHistory = results;
            Scores = results.GetValueOrDefault(Settings.ScoreTerm);
            Profile ??= await TryLoadProfileAsync(password);
            Status = $"已汇总 {results.Count} 个有成绩的学期";
        }
        finally { _networkGate.Release(); }
        DataChanged?.Invoke(this, EventArgs.Empty);
    }

    public async Task SelectScoreTermAsync(string term)
    {
        if (string.IsNullOrWhiteSpace(term) || term == Settings.ScoreTerm) return;
        Settings = Settings with { ScoreTerm = term };
        settingsService.Save(Settings, "");
        Scores = await cache.LoadScoresAsync(Settings.Account, term);
        var history = ScoreHistory.ToDictionary(x => x.Key, x => x.Value);
        if (Scores is not null && Scores.Records.Count > 0) history[term] = Scores;
        ScoreHistory = history;
        Status = Scores is null ? $"{term} 尚未查询成绩" : $"已加载 {term} 成绩缓存";
        DataChanged?.Invoke(this, EventArgs.Empty);
    }

    public void SetDisplayDate(DateTime date)
    {
        var normalized = date.Date;
        if (DisplayDate == normalized) return;
        DisplayDate = normalized;
        DataChanged?.Invoke(this, EventArgs.Empty);
    }

    public async Task<ScoreDetail> QueryScoreDetailAsync(ScoreRecord score)
    {
        EnsureCredentials(out var account, out var password);
        await _networkGate.WaitAsync();
        try { return await academic.QueryScoreDetailAsync(account, password, score); }
        finally { _networkGate.Release(); }
    }

    public async Task RefreshPublicScheduleAsync()
    {
        await _networkGate.WaitAsync();
        try
        {
            PublicCourses = await academic.DownloadPublicScheduleAsync(Settings.Term);
            await cache.SavePublicCoursesAsync(Settings.Term, PublicCourses);
            Status = $"全校课表更新于 {DateTime.Now:HH:mm}";
        }
        finally { _networkGate.Release(); }
        DataChanged?.Invoke(this, EventArgs.Empty);
    }

    public IEnumerable<CourseOccurrence> GetOccurrences(DateTime date)
    {
        var week = TermPolicy.WeekForDate(Settings.TermStartDate, date);
        var day = SchedulePolicy.DayIndex(date);
        return Courses.Where(x => x.Day == day && SchedulePolicy.IsVisibleInWeek(x.Weeks, week))
            .Select(x =>
            {
                var (start, end) = SchedulePolicy.Resolve(date, x.StartSlot, x.SlotCount);
                return new CourseOccurrence(x, start, end, week);
            }).OrderBy(x => x.Start);
    }

    private async Task<StudentProfile?> TryLoadProfileAsync(string password)
    {
        try { return await academic.QueryStudentProfileAsync(Settings.Account, password); }
        catch { return null; }
    }

    private void EnsureCredentials(out string account, out string password)
    {
        account = Settings.Account;
        password = settingsService.ReadPassword(account);
        if (string.IsNullOrWhiteSpace(account) || string.IsNullOrWhiteSpace(password))
            throw new InvalidOperationException("请先配置教务系统账号。");
    }
}
