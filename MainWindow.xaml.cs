using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using UsSDAU.Models;
using UsSDAU.Services;
using UsSDAU.Windows;
using Brush = System.Windows.Media.Brush;
using Button = System.Windows.Controls.Button;
using Color = System.Windows.Media.Color;
using ComboBox = System.Windows.Controls.ComboBox;
using Rectangle = System.Windows.Shapes.Rectangle;

namespace UsSDAU;

public partial class MainWindow : Window
{
    private readonly AppController _controller;
    private readonly TranscriptVerificationService _verification = new();
    private readonly TranscriptPdfService _pdf;
    private string _page = "day";
    private DateTime _day = DateTime.Today;
    private DateTime _weekAnchor = DateTime.Today;
    private bool _closingForExit;
    private bool _updatingPublicFilters;
    private int _scoreViewMode;
    private bool _scoreAllTerms;
    private ComboBox? _collegeBox, _gradeBox, _majorBox, _classBox;

    public MainWindow(AppController controller)
    {
        InitializeComponent();
        _controller = controller;
        _pdf = new TranscriptPdfService(_verification);
        _controller.DataChanged += (_, _) => Dispatcher.Invoke(RefreshAllViews);
        Loaded += (_, _) => { SetActiveNav("day"); RefreshAllViews(); };
    }

    public void RefreshAllViews()
    {
        AccountText.Text = string.IsNullOrWhiteSpace(_controller.Settings.Account) ? "未配置账号" : _controller.Settings.Account;
        TermText.Text = _controller.Settings.Term;
        StatusText.Text = _controller.Status;
        RenderCurrentPage();
    }

    private void Nav_Click(object sender, RoutedEventArgs e)
    {
        _page = (sender as Button)?.Tag?.ToString() ?? "day";
        SetActiveNav(_page);
        RenderCurrentPage();
    }

    private void RenderCurrentPage()
    {
        PageHost.Children.Clear();
        switch (_page)
        {
            case "week": PageTitle.Text = "周课表"; PageHost.Children.Add(BuildWeekPage()); break;
            case "public": PageTitle.Text = "全校课表"; PageHost.Children.Add(BuildPublicPage()); break;
            case "scores": PageTitle.Text = "成绩中心"; PageHost.Children.Add(BuildScoresPage()); break;
            default: PageTitle.Text = "今日课表"; PageHost.Children.Add(BuildDayPage()); break;
        }
    }

    private UIElement BuildDayPage()
    {
        var root = new DockPanel();
        var toolbar = DateToolbar(_day, delta => ChangeDay(_day.AddDays(delta)), () => ChangeDay(DateTime.Today));
        DockPanel.SetDock(toolbar, Dock.Top); root.Children.Add(toolbar);
        var stack = new StackPanel { Margin = new Thickness(0, 18, 0, 0) };
        var courses = _controller.GetOccurrences(_day).ToList();
        if (courses.Count == 0) stack.Children.Add(EmptyState("今天没有课程", "可以安心安排自己的时间。"));
        foreach (var item in courses)
        {
            var card = new Border { CornerRadius = new CornerRadius(18), Background = Brush("#F5F8FD"), Margin = new Thickness(0, 0, 0, 12), Padding = new Thickness(18) };
            var grid = new Grid(); grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(108) }); grid.ColumnDefinitions.Add(new ColumnDefinition()); grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var time = new StackPanel();
            time.Children.Add(new TextBlock { Text = item.Start.ToString("HH:mm"), FontSize = 20, FontWeight = FontWeights.Bold, Foreground = Brush("#1677FF") });
            time.Children.Add(new TextBlock { Text = item.End.ToString("HH:mm"), Foreground = Brush("#8492A9"), FontSize = 12 });
            grid.Children.Add(time);
            var info = new StackPanel { Margin = new Thickness(8, 0, 0, 0) };
            info.Children.Add(new TextBlock { Text = item.Course.Name, FontSize = 18, FontWeight = FontWeights.SemiBold });
            info.Children.Add(new TextBlock { Text = $"{item.Course.Teacher}  ·  {item.Course.Room}", Foreground = Brush("#6F7E95"), Margin = new Thickness(0, 5, 0, 0) });
            Grid.SetColumn(info, 1); grid.Children.Add(info);
            var badge = Badge(SchedulePolicy.BigSectionLabel(item.Course.StartSlot), "#E8F2FF", "#1677FF"); Grid.SetColumn(badge, 2); grid.Children.Add(badge);
            card.Child = grid; stack.Children.Add(card);
        }
        root.Children.Add(new ScrollViewer { Content = stack, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        return root;
    }

    private UIElement BuildWeekPage()
    {
        var monday = _weekAnchor.Date.AddDays(-SchedulePolicy.DayIndex(_weekAnchor));
        var root = new DockPanel();
        var toolbar = DateToolbar(monday, delta => { _weekAnchor = _weekAnchor.AddDays(delta * 7); RenderCurrentPage(); }, () => { _weekAnchor = DateTime.Today; RenderCurrentPage(); }, $"{monday:M月d日} – {monday.AddDays(6):M月d日}");
        DockPanel.SetDock(toolbar, Dock.Top); root.Children.Add(toolbar);
        root.Children.Add(BuildScheduleGrid(_controller.Courses, monday));
        return root;
    }

    private UIElement BuildScheduleGrid(IEnumerable<Course> source, DateTime monday)
    {
        var grid = new Grid { Margin = new Thickness(0, 18, 0, 0), Background = Brush("#F7F9FC") };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(72) });
        for (var day = 0; day < 7; day++) grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(46) });
        for (var row = 0; row < 5; row++) grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(92) });
        grid.Children.Add(Cell("时段", true));
        for (var day = 0; day < 7; day++)
        {
            var date = monday.AddDays(day); var cell = Cell($"{ChineseDay(day)}\n{date:M.d}", true); Grid.SetColumn(cell, day + 1); grid.Children.Add(cell);
        }
        for (var section = 0; section < 5; section++)
        {
            var date = monday;
            var label = Cell($"{SchedulePolicy.BigSectionLabel(section * 2)}\n{SchedulePolicy.TimeLabel(date, section * 2, 2)}", false); Grid.SetRow(label, section + 1); grid.Children.Add(label);
            for (var day = 0; day < 7; day++)
            {
                var target = monday.AddDays(day); var week = TermPolicy.WeekForDate(_controller.Settings.TermStartDate, target);
                var course = source.FirstOrDefault(x => x.Day == day && x.StartSlot / 2 == section && SchedulePolicy.IsVisibleInWeek(x.Weeks, week));
                var cell = course is null ? Cell("", false) : CourseCell(course);
                Grid.SetColumn(cell, day + 1); Grid.SetRow(cell, section + 1); grid.Children.Add(cell);
            }
        }
        return new ScrollViewer { Content = grid, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto };
    }

    private UIElement BuildPublicPage()
    {
        var root = new DockPanel();
        var toolbar = new StackPanel { Orientation = Orientation.Horizontal };
        var update = new Button { Content = _controller.PublicCourses.Count == 0 ? "下载全校课表" : "更新全校课表", Style = FindResource("PrimaryButton") as Style, Margin = new Thickness(0, 0, 14, 0) };
        update.Click += async (_, _) => await RunBusyAsync(update, "正在下载…", _controller.RefreshPublicScheduleAsync);
        toolbar.Children.Add(update);
        _collegeBox = FilterBox("学院"); _gradeBox = FilterBox("年级"); _majorBox = FilterBox("专业"); _classBox = FilterBox("班级");
        foreach (var box in new[] { _collegeBox, _gradeBox, _majorBox, _classBox }) toolbar.Children.Add(box);
        _collegeBox.SelectionChanged += (_, _) => { if (_updatingPublicFilters) return; PopulatePublicFilters(1); RenderPublicGrid(root); };
        _gradeBox.SelectionChanged += (_, _) => { if (_updatingPublicFilters) return; PopulatePublicFilters(2); RenderPublicGrid(root); };
        _majorBox.SelectionChanged += (_, _) => { if (_updatingPublicFilters) return; PopulatePublicFilters(3); RenderPublicGrid(root); };
        _classBox.SelectionChanged += (_, _) => { if (!_updatingPublicFilters) RenderPublicGrid(root); };
        DockPanel.SetDock(toolbar, Dock.Top); root.Children.Add(toolbar);
        PopulatePublicFilters(0); RenderPublicGrid(root);
        return root;
    }

    private void RenderPublicGrid(DockPanel root)
    {
        while (root.Children.Count > 1) root.Children.RemoveAt(1);
        if (_controller.PublicCourses.Count == 0) { root.Children.Add(EmptyState("尚未下载全校课表", "点击上方按钮下载当前学期数据。")); return; }
        var selected = _controller.PublicCourses.Where(x => Match(x.College, _collegeBox) && Match(x.Grade, _gradeBox) && Match(x.Major, _majorBox) && Match(x.ClassName, _classBox)).Cast<Course>().ToList();
        var monday = DateTime.Today.AddDays(-SchedulePolicy.DayIndex(DateTime.Today));
        root.Children.Add(BuildScheduleGrid(selected, monday));
    }

    private void PopulatePublicFilters(int from)
    {
        if (_controller.PublicCourses.Count == 0) return;
        _updatingPublicFilters = true;
        try
        {
            if (from <= 0) Fill(_collegeBox!, _controller.PublicCourses.Select(x => x.College));
            var college = _collegeBox?.SelectedItem?.ToString();
            if (from <= 1) Fill(_gradeBox!, _controller.PublicCourses.Where(x => college is null || x.College == college).Select(x => x.Grade));
            var grade = _gradeBox?.SelectedItem?.ToString();
            if (from <= 2) Fill(_majorBox!, _controller.PublicCourses.Where(x => (college is null || x.College == college) && (grade is null || x.Grade == grade)).Select(x => x.Major));
            var major = _majorBox?.SelectedItem?.ToString();
            if (from <= 3) Fill(_classBox!, _controller.PublicCourses.Where(x => (college is null || x.College == college) && (grade is null || x.Grade == grade) && (major is null || x.Major == major)).Select(x => x.ClassName));
        }
        finally { _updatingPublicFilters = false; }
    }

    private UIElement BuildScoresPage()
    {
        var root = new DockPanel();
        var toolbar = new StackPanel { Orientation = Orientation.Horizontal };
        var termBox = new ComboBox
        {
            Width = 168, Height = 40, Margin = new Thickness(0, 0, 10, 0),
            ItemsSource = TermPolicy.ScoreTermOptions(_controller.Settings.Account),
            SelectedItem = _controller.Settings.ScoreTerm,
            ToolTip = "成绩查询学期（与课表学期独立）"
        };
        var scope = new ComboBox { Width = 132, Height = 40, Margin = new Thickness(0, 0, 10, 0), ItemsSource = new[] { "当前学期", "全部学期" }, SelectedIndex = _scoreAllTerms ? 1 : 0 };
        var mode = new ComboBox { Width = 150, Height = 40, Margin = new Thickness(0, 0, 12, 0), ItemsSource = new[] { "现代卡片", "正式成绩单", "红笔批注" }, SelectedIndex = _scoreViewMode };
        var update = new Button { Content = "查询最新成绩", Style = FindResource("PrimaryButton") as Style };
        var export = new Button { Content = "导出 PDF", Style = FindResource("GhostButton") as Style, Margin = new Thickness(10, 0, 0, 0) };
        var verify = new Button { Content = "离线验真", Style = FindResource("GhostButton") as Style, Margin = new Thickness(10, 0, 0, 0) };
        toolbar.Children.Add(scope); toolbar.Children.Add(termBox); toolbar.Children.Add(mode); toolbar.Children.Add(update); toolbar.Children.Add(export); toolbar.Children.Add(verify);
        var controls = new StackPanel();
        controls.Children.Add(toolbar);
        controls.Children.Add(new TextBlock { Text = "成绩查询学期独立于个人课表学期，切换后优先读取对应学期缓存。", Foreground = Brush("#7B899E"), FontSize = 10.5, Margin = new Thickness(2, 8, 0, 0) });
        DockPanel.SetDock(controls, Dock.Top); root.Children.Add(controls);
        var host = new Grid { Margin = new Thickness(0, 18, 0, 0) }; root.Children.Add(host);
        void render() { host.Children.Clear(); host.Children.Add(RenderScores(mode.SelectedIndex)); }
        termBox.SelectionChanged += async (_, _) =>
        {
            if (termBox.SelectedItem is not string term) return;
            await _controller.SelectScoreTermAsync(term);
            render();
        };
        scope.SelectionChanged += (_, _) =>
        {
            _scoreAllTerms = scope.SelectedIndex == 1;
            termBox.IsEnabled = !_scoreAllTerms;
            update.Content = _scoreAllTerms ? "遍历并汇总全部学期" : "查询最新成绩";
            render();
        };
        mode.SelectionChanged += (_, _) => { _scoreViewMode = mode.SelectedIndex; render(); };
        update.Click += async (_, _) =>
        {
            if (_scoreAllTerms)
            {
                var progress = new Progress<(int Current, int Total, string Term)>(x => update.Content = $"正在读取 {x.Current}/{x.Total}");
                await RunBusyAsync(update, "正在准备…", () => _controller.RefreshAllScoresAsync(progress));
            }
            else
            {
                var term = termBox.SelectedItem?.ToString() ?? _controller.Settings.ScoreTerm;
                await RunBusyAsync(update, "正在查询…", () => _controller.RefreshScoresAsync(term));
            }
            render();
        };
        export.Click += async (_, _) => await ExportTranscriptAsync();
        verify.Click += (_, _) => new VerificationWindow(_verification) { Owner = this }.ShowDialog();
        termBox.IsEnabled = !_scoreAllTerms;
        update.Content = _scoreAllTerms ? "遍历并汇总全部学期" : "查询最新成绩";
        render(); return root;
    }

    private UIElement RenderScores(int mode)
    {
        if (_scoreAllTerms)
        {
            var history = _controller.ScoreHistory.Values.OrderBy(x => x.Term).ToList();
            if (history.Count == 0) return EmptyState("尚未汇总全部学期", "点击“遍历并汇总全部学期”，将依次读取每个学期的成绩。");
            if (mode == 1) return TranscriptView(history, totalTranscript: true);
            var records = history.SelectMany(x => x.Records).ToList();
            var aggregate = ScorePolicy.Recalculate("全部学期", records, records);
            return mode == 2 ? RedPenView(aggregate) : ScoreCards(aggregate);
        }
        var result = _controller.Scores;
        if (result is null || result.Records.Count == 0)
            return EmptyState($"{_controller.Settings.ScoreTerm} 暂无成绩", "可切换其他成绩学期，或点击“查询最新成绩”从教务系统读取。");
        return mode switch { 1 => TranscriptView([result]), 2 => RedPenView(result), _ => ScoreCards(result) };
    }

    private UIElement ScoreCards(ScoreResult result)
    {
        var page = new StackPanel();
        var summary = new WrapPanel { Margin = new Thickness(0, 0, 0, 18) };
        summary.Children.Add(MetricCard("平均分", result.AverageScore, "综合表现"));
        summary.Children.Add(MetricCard("平均绩点", result.AverageCreditGpa, "按学分加权"));
        summary.Children.Add(MetricCard(result.Term == "全部学期" ? "累计总学分" : "本学期学分", result.TotalCredits, $"{result.Records.Count} 门课程"));
        page.Children.Add(summary);
        var list = new StackPanel();
        var listHeader = new Grid { Height = 38, Background = Brush("#F3F6FA") };
        listHeader.ColumnDefinitions.Add(new ColumnDefinition());
        listHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(86) });
        listHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(86) });
        listHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(96) });
        listHeader.Children.Add(new TextBlock { Text = "课程", Foreground = Brush("#6F7E94"), FontSize = 11, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(16, 0, 0, 0) });
        foreach (var item in new[] { ("学分", 1), ("绩点", 2), ("成绩", 3) }) { var text = new TextBlock { Text = item.Item1, Foreground = Brush("#6F7E94"), FontSize = 11, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center }; Grid.SetColumn(text, item.Item2); listHeader.Children.Add(text); }
        list.Children.Add(listHeader);
        foreach (var score in result.Records)
        {
            var row = new Grid { Height = 66, Background = Brushes.White, Cursor = Cursors.Hand };
            row.ColumnDefinitions.Add(new ColumnDefinition()); row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(86) }); row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(86) }); row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(96) });
            var info = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(16, 0, 8, 0) };
            info.Children.Add(new TextBlock { Text = score.CourseName, FontWeight = FontWeights.SemiBold, FontSize = 13.5, TextTrimming = TextTrimming.CharacterEllipsis });
            info.Children.Add(new TextBlock { Text = score.CourseCode, Foreground = Brush("#8794A8"), FontFamily = new FontFamily("Times New Roman"), FontSize = 10, Margin = new Thickness(0, 3, 0, 0) });
            row.Children.Add(info);
            var credit = new TextBlock { Text = score.Credit, FontFamily = new FontFamily("Times New Roman"), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center }; Grid.SetColumn(credit, 1); row.Children.Add(credit);
            var gpa = new TextBlock { Text = score.Gpa, FontFamily = new FontFamily("Times New Roman"), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center }; Grid.SetColumn(gpa, 2); row.Children.Add(gpa);
            var grade = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            grade.Children.Add(new TextBlock { Text = score.Score, FontFamily = new FontFamily("Times New Roman"), FontSize = 17, FontWeight = FontWeights.Bold, Foreground = ScoreBrush(score), VerticalAlignment = VerticalAlignment.Center });
            grade.Children.Add(new Border { Background = Brush("#EEF3F8"), CornerRadius = new CornerRadius(9), Padding = new Thickness(7, 3, 7, 3), Margin = new Thickness(7, 0, 0, 0), Child = new TextBlock { Text = GradeLabel(score), FontFamily = new FontFamily("Times New Roman"), FontSize = 9, Foreground = Brush("#52647D") } });
            Grid.SetColumn(grade, 3); row.Children.Add(grade);
            row.MouseLeftButtonUp += async (_, _) => await ShowScoreDetailAsync(score);
            list.Children.Add(row);
            list.Children.Add(new Border { Height = 1, Background = Brush("#E8ECF2"), Margin = new Thickness(16, 0, 16, 0) });
        }
        page.Children.Add(new Border { CornerRadius = new CornerRadius(16), BorderBrush = Brush("#E1E7F0"), BorderThickness = new Thickness(1), Background = Brushes.White, Child = list });
        return new ScrollViewer { Content = page, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    }

    private UIElement TranscriptView(IReadOnlyList<ScoreResult> results, bool totalTranscript = false)
    {
        var allTerms = totalTranscript || results.Count > 1;
        var allRecords = results.SelectMany(x => x.Records).ToList();
        var result = allTerms ? ScorePolicy.Recalculate("全部学期", allRecords, allRecords) : results[0];
        var documentLayer = new Grid { Background = Brushes.White };
        var paper = new StackPanel { Margin = new Thickness(46, 40, 46, 42) };
        var letterhead = new Grid();
        letterhead.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(74) });
        letterhead.ColumnDefinitions.Add(new ColumnDefinition());
        letterhead.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(74) });
        letterhead.Children.Add(LogoImage(62));
        var titles = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
        titles.Children.Add(new TextBlock { Text = allTerms ? "山东农业大学学生总成绩单" : "山东农业大学学生成绩单", FontFamily = new FontFamily("SimHei"), FontSize = 25, FontWeight = FontWeights.Bold, HorizontalAlignment = HorizontalAlignment.Center });
        titles.Children.Add(new TextBlock { Text = "SHANDONG AGRICULTURAL UNIVERSITY · ACADEMIC TRANSCRIPT", FontFamily = new FontFamily("Times New Roman"), FontSize = 10.5, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 5, 0, 0) });
        Grid.SetColumn(titles, 1); letterhead.Children.Add(titles);
        paper.Children.Add(letterhead);
        paper.Children.Add(new Border { Height = 3, Background = Brush("#15233A"), Margin = new Thickness(0, 19, 0, 2) });
        paper.Children.Add(new Border { Height = 1, Background = Brush("#15233A"), Margin = new Thickness(0, 0, 0, 18) });

        var info = new Grid { Margin = new Thickness(2, 0, 2, 16) };
        for (var i = 0; i < 4; i++) info.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(i % 2 == 0 ? 92 : 1, i % 2 == 0 ? GridUnitType.Pixel : GridUnitType.Star) });
        info.RowDefinitions.Add(new RowDefinition { Height = new GridLength(28) }); info.RowDefinitions.Add(new RowDefinition { Height = new GridLength(28) });
        AddInfo(info, 0, 0, "姓名 / NAME", _controller.Profile?.Name ?? "—");
        AddInfo(info, 0, 2, "学号 / ID", _controller.Settings.Account);
        AddInfo(info, 1, 0, "学期 / TERM", allTerms ? $"全部学期（{results.Count}）" : result.Term);
        AddInfo(info, 1, 2, "制表日期 / DATE", DateTime.Today.ToString("yyyy-MM-dd"));
        paper.Children.Add(info);

        var table = new Grid { HorizontalAlignment = HorizontalAlignment.Stretch };
        table.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.2, GridUnitType.Star) });
        table.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2.8, GridUnitType.Star) });
        table.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(.75, GridUnitType.Star) });
        table.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(.75, GridUnitType.Star) });
        table.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(.75, GridUnitType.Star) });
        table.RowDefinitions.Add(new RowDefinition { Height = new GridLength(48) });
        var headers = new[] { ("课程代码", "COURSE CODE"), ("课程名称", "COURSE TITLE"), ("学分", "CREDIT"), ("成绩", "GRADE"), ("绩点", "GPA") };
        for (var column = 0; column < headers.Length; column++) AddTranscriptCell(table, 0, column, TranscriptHeader(headers[column].Item1, headers[column].Item2), true);
        var rowIndex = 1;
        foreach (var termResult in results.OrderBy(x => x.Term))
        {
            if (allTerms)
            {
                table.RowDefinitions.Add(new RowDefinition { Height = new GridLength(29) });
                AddTranscriptTermRow(table, rowIndex, termResult.Term);
                rowIndex++;
            }
            foreach (var score in termResult.Records)
            {
                table.RowDefinitions.Add(new RowDefinition { Height = new GridLength(39) });
                AddTranscriptCell(table, rowIndex, 0, TranscriptText(score.CourseCode, "Times New Roman", TextAlignment.Center));
                AddTranscriptCell(table, rowIndex, 1, TranscriptText(score.CourseName, "SimSun, Times New Roman", TextAlignment.Left));
                AddTranscriptCell(table, rowIndex, 2, TranscriptText(score.Credit, "Times New Roman", TextAlignment.Center));
                var scoreCell = AddTranscriptCell(table, rowIndex, 3, TranscriptText(score.Score, "Times New Roman", TextAlignment.Center, FontWeights.Bold));
                scoreCell.Cursor = Cursors.Hand;
                scoreCell.ToolTip = "点击查看成绩构成";
                scoreCell.MouseLeftButtonUp += async (_, _) => await ShowScoreDetailAsync(score);
                AddTranscriptCell(table, rowIndex, 4, TranscriptText(score.Gpa, "Times New Roman", TextAlignment.Center));
                rowIndex++;
            }
        }
        paper.Children.Add(table);

        var summary = new Grid { Margin = new Thickness(0, 16, 0, 0), Background = Brush("#F3F5F7") };
        for (var i = 0; i < 4; i++) summary.ColumnDefinitions.Add(new ColumnDefinition());
        AddSummaryMetric(summary, 0, "学期数", results.Count.ToString());
        AddSummaryMetric(summary, 1, "课程数", allRecords.Count.ToString());
        AddSummaryMetric(summary, 2, "总学分", result.TotalCredits);
        AddSummaryMetric(summary, 3, allTerms ? "累计绩点" : "平均绩点", result.AverageCreditGpa);
        paper.Children.Add(new Border { BorderBrush = Brush("#CBD1D9"), BorderThickness = new Thickness(1), Child = summary });
        var watermark = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Height = 82, Opacity = .065, IsHitTestVisible = false, Margin = new Thickness(0, 8, 0, 0) };
        watermark.Children.Add(LogoImage(58));
        watermark.Children.Add(new TextBlock { Text = "UsSDAU", FontFamily = new FontFamily("Times New Roman"), FontSize = 34, FontWeight = FontWeights.Bold, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0, 0, 0) });
        paper.Children.Add(watermark);
        paper.Children.Add(new Border { Height = 1, Background = Brush("#B9C0CA"), Margin = new Thickness(0, 22, 0, 13) });
        paper.Children.Add(new TextBlock { Text = "本成绩单由辅助工具生成，不具有山东农业大学官方的效力。", FontFamily = new FontFamily("SimSun"), FontSize = 9.5, Foreground = Brush("#5F6875"), TextWrapping = TextWrapping.Wrap });
        paper.Children.Add(new TextBlock { Text = "第 1 页 / 共 1 页", FontFamily = new FontFamily("Times New Roman"), FontSize = 9, Foreground = Brush("#7C8490"), HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 6, 0, 0) });
        documentLayer.Children.Add(paper);
        var sheet = new Border { Width = 880, MinHeight = 1000, Background = Brushes.White, BorderBrush = Brush("#D9DEE7"), BorderThickness = new Thickness(1), Child = documentLayer, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(18), Padding = new Thickness(0) };
        sheet.Effect = new System.Windows.Media.Effects.DropShadowEffect { Color = Color.FromRgb(43, 57, 77), BlurRadius = 28, ShadowDepth = 8, Opacity = .13 };
        return new ScrollViewer { Content = sheet, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, Background = Brush("#EEF1F5") };
    }

    private UIElement RedPenView(ScoreResult result)
    {
        var panel = new StackPanel { Background = Brush("#FFFDF8"), Margin = new Thickness(56, 35, 38, 40) };
        var header = new Grid(); header.ColumnDefinitions.Add(new ColumnDefinition()); header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var heading = new StackPanel();
        heading.Children.Add(new TextBlock { Text = "SEMESTER REVIEW", FontFamily = new FontFamily("Times New Roman"), FontSize = 10, Foreground = Brush("#8A796A") });
        heading.Children.Add(new TextBlock { Text = result.Term == "全部学期" ? "全部学期学习记录" : $"{result.Term} 学习反馈", FontFamily = new FontFamily("SimHei"), FontSize = 24, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 4, 0, 0) });
        header.Children.Add(heading);
        var note = new TextBlock { Text = $"平均分 {result.AverageScore}   GPA {result.AverageCreditGpa}", FontFamily = new FontFamily("Segoe Print, Microsoft YaHei UI"), FontSize = 14, Foreground = Brush("#B84040"), VerticalAlignment = VerticalAlignment.Bottom, RenderTransform = new RotateTransform(-2) }; Grid.SetColumn(note, 1); header.Children.Add(note);
        panel.Children.Add(header);
        panel.Children.Add(new Border { Height = 1, Background = Brush("#D9D0C4"), Margin = new Thickness(0, 17, 0, 8) });
        foreach (var score in result.Records)
        {
            var row = new Grid { Height = 58 };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(88) });
            row.ColumnDefinitions.Add(new ColumnDefinition());
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(70) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(82) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(100) });
            row.Children.Add(new TextBlock { Text = score.CourseCode, FontFamily = new FontFamily("Times New Roman"), Foreground = Brush("#7A7168"), VerticalAlignment = VerticalAlignment.Center, FontSize = 11 });
            var course = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            course.Children.Add(new TextBlock { Text = score.CourseName, FontFamily = new FontFamily("SimSun, Times New Roman"), FontSize = 14 });
            course.Children.Add(new TextBlock { Text = $"{score.Credit} 学分", Foreground = Brush("#93887D"), FontSize = 10, Margin = new Thickness(0, 3, 0, 0) });
            Grid.SetColumn(course, 1); row.Children.Add(course);
            var gpa = new TextBlock { Text = $"GPA\n{score.Gpa}", FontFamily = new FontFamily("Times New Roman"), TextAlignment = TextAlignment.Center, Foreground = Brush("#6E665E"), VerticalAlignment = VerticalAlignment.Center, FontSize = 10 }; Grid.SetColumn(gpa, 2); row.Children.Add(gpa);
            var mark = new Border { BorderBrush = Brush("#C74747"), BorderThickness = new Thickness(1.7), CornerRadius = new CornerRadius(24), Width = 61, Height = 39, HorizontalAlignment = HorizontalAlignment.Center, RenderTransform = new RotateTransform(-3), Cursor = Cursors.Hand, Child = new TextBlock { Text = score.Score, FontFamily = new FontFamily("Segoe Print, SimHei"), Foreground = Brush("#C74747"), FontSize = 18, FontWeight = FontWeights.Bold, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center } };
            mark.MouseLeftButtonUp += async (_, _) => await ShowScoreDetailAsync(score); Grid.SetColumn(mark, 3); row.Children.Add(mark);
            var feedback = new TextBlock { Text = RedPenFeedback(score), FontFamily = new FontFamily("Segoe Print, KaiTi"), Foreground = Brush("#B84040"), FontSize = 12, VerticalAlignment = VerticalAlignment.Center, RenderTransform = new RotateTransform(-2) }; Grid.SetColumn(feedback, 4); row.Children.Add(feedback);
            panel.Children.Add(row);
            panel.Children.Add(new Rectangle { Height = 1, Fill = Brush("#DED6CB") });
        }
        panel.Children.Add(new TextBlock { Text = result.AverageScore == "-" ? "继续保持稳定的学习节奏。" : "总体表现已记录，点击圈注成绩可查看构成。", FontFamily = new FontFamily("Segoe Print, KaiTi"), Foreground = Brush("#B84040"), FontSize = 13, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 18, 4, 0), RenderTransform = new RotateTransform(-1) });
        var paper = new Border { Width = 900, Background = Brush("#FFFDF8"), BorderBrush = Brush("#E3DBD0"), BorderThickness = new Thickness(1), Child = panel, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(18) };
        paper.Effect = new System.Windows.Media.Effects.DropShadowEffect { Color = Color.FromRgb(56, 48, 40), BlurRadius = 22, ShadowDepth = 7, Opacity = .11 };
        return new ScrollViewer { Content = paper, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, Background = Brush("#F1EEE9") };
    }

    private async Task ShowScoreDetailAsync(ScoreRecord score)
    {
        try
        {
            var detail = await _controller.QueryScoreDetailAsync(score);
            new ScoreDetailWindow(score, detail) { Owner = this }.ShowDialog();
        }
        catch (Exception ex) { System.Windows.MessageBox.Show(this, ex.Message, "成绩详情", MessageBoxButton.OK, MessageBoxImage.Information); }
    }

    private async Task ExportTranscriptAsync()
    {
        var results = _scoreAllTerms
            ? _controller.ScoreHistory.Values.OrderBy(x => x.Term).ToList()
            : _controller.Scores is null ? [] : new List<ScoreResult> { _controller.Scores };
        if (results.Count == 0 || results.All(x => x.Records.Count == 0))
        {
            System.Windows.MessageBox.Show(this, "当前范围没有可导出的成绩。", "导出成绩单", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Filter = "PDF 文档 (*.pdf)|*.pdf",
            FileName = _scoreAllTerms ? "UsSDAU-全部学期总成绩单.pdf" : $"UsSDAU-{results[0].Term}-成绩单.pdf",
            AddExtension = true,
            DefaultExt = ".pdf"
        };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            var exported = await _pdf.ExportAsync(dialog.FileName, _controller.Settings, _controller.Profile, results, _scoreAllTerms);
            System.Windows.MessageBox.Show(this, $"成绩单已导出，共 {exported.PageCount} 页。\n二维码使用本机 ECDSA 签名，可离线检查导出后是否被篡改。", "导出完成", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception error)
        {
            System.Windows.MessageBox.Show(this, error.Message, "导出失败", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e)
    {
        if (System.Windows.MessageBox.Show(this, "是否从教务系统更新当前学期课表？", "更新课表", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        await RunBusyAsync(RefreshButton, "正在更新…", _controller.RefreshCoursesAsync);
    }

    private async Task RunBusyAsync(Button button, string busyText, Func<Task> action)
    {
        var original = button.Content; button.Content = busyText; button.IsEnabled = false;
        try { await action(); StatusText.Text = _controller.Status; RenderCurrentPage(); }
        catch (Exception ex) { System.Windows.MessageBox.Show(this, ex.Message, "UsSDAU", MessageBoxButton.OK, MessageBoxImage.Warning); }
        finally { button.Content = original; button.IsEnabled = true; }
    }

    private static DockPanel DateToolbar(DateTime date, Action<int> move, Action today, string? title = null)
    {
        var bar = new DockPanel();
        var text = new TextBlock { Text = title ?? $"{date:yyyy年M月d日} · {date:dddd}", FontSize = 17, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center };
        bar.Children.Add(text);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var previous = SmallButton("‹"); previous.Click += (_, _) => move(-1); var now = SmallButton("今天"); now.Click += (_, _) => today(); var next = SmallButton("›"); next.Click += (_, _) => move(1);
        buttons.Children.Add(previous); buttons.Children.Add(now); buttons.Children.Add(next); DockPanel.SetDock(buttons, Dock.Right); bar.Children.Add(buttons); return bar;
    }

    private static Button SmallButton(string text) => new() { Content = text, MinWidth = 42, Height = 34, Margin = new Thickness(6, 0, 0, 0), Background = Brush("#EEF2F7"), BorderThickness = new Thickness(0), Cursor = Cursors.Hand };
    private static Border Badge(string text, string bg, string fg) => new() { Background = Brush(bg), CornerRadius = new CornerRadius(12), Padding = new Thickness(11, 6, 11, 6), VerticalAlignment = VerticalAlignment.Center, Child = new TextBlock { Text = text, Foreground = Brush(fg), FontWeight = FontWeights.SemiBold } };
    private static Border Cell(string text, bool header) => new() { BorderBrush = Brush("#E5EAF2"), BorderThickness = new Thickness(0, 0, 1, 1), Padding = new Thickness(8), Background = header ? Brushes.White : Brushes.Transparent, Child = new TextBlock { Text = text, TextAlignment = TextAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Foreground = header ? Brush("#4F5F77") : Brush("#71809B"), FontSize = header ? 12 : 10 } };
    private static Border CourseCell(Course course) => new() { BorderBrush = Brush("#D9E7FB"), BorderThickness = new Thickness(0, 0, 1, 1), Margin = new Thickness(3), Padding = new Thickness(8), CornerRadius = new CornerRadius(12), Background = Brush("#EAF3FF"), Child = new TextBlock { Text = $"{course.Name}\n{course.Room}", TextAlignment = TextAlignment.Center, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap, Foreground = Brush("#1759A8"), FontWeight = FontWeights.SemiBold, FontSize = 11 } };
    private static Border EmptyState(string title, string subtitle) { var panel = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center }; panel.Children.Add(new TextBlock { Text = "○", FontSize = 48, Foreground = Brush("#A9B7CB"), HorizontalAlignment = HorizontalAlignment.Center }); panel.Children.Add(new TextBlock { Text = title, FontSize = 19, FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Center }); panel.Children.Add(new TextBlock { Text = subtitle, Foreground = Brush("#7C8BA2"), Margin = new Thickness(0, 6, 0, 0), HorizontalAlignment = HorizontalAlignment.Center }); return new Border { Child = panel }; }
    private static string ChineseDay(int day) => new[] { "周一", "周二", "周三", "周四", "周五", "周六", "周日" }[day];
    private static SolidColorBrush Brush(string hex) => new((Color)ColorConverter.ConvertFromString(hex));
    private static ComboBox FilterBox(string hint) => new() { Width = 130, Height = 38, Margin = new Thickness(0, 0, 9, 0), ToolTip = hint };
    private static void Fill(ComboBox box, IEnumerable<string> values) { var prior = box.SelectedItem?.ToString(); var data = values.Where(x => !string.IsNullOrWhiteSpace(x)).Distinct().Order().ToList(); box.ItemsSource = data; box.SelectedItem = data.Contains(prior ?? "") ? prior : data.FirstOrDefault(); }
    private static bool Match(string value, ComboBox? box) => box?.SelectedItem is null || value == box.SelectedItem.ToString();
    private static Brush ScoreBrush(ScoreRecord score) => score.NumericScore is < 60 ? Brush("#D84A4A") : score.NumericScore is >= 90 ? Brush("#16A273") : Brush("#1677FF");
    private static string RedPenFeedback(ScoreRecord score) => score.NumericScore switch
    {
        >= 90 => "Excellent",
        >= 80 => "Well done",
        >= 70 => "Good",
        >= 60 => "Passed",
        < 60 => "Review",
        _ => "Recorded"
    };
    private static string GradeLabel(ScoreRecord score) => score.NumericScore switch
    {
        >= 90 => "A",
        >= 80 => "B",
        >= 70 => "C",
        >= 60 => "D",
        < 60 => "F",
        _ => "—"
    };

    private static Border MetricCard(string label, string value, string hint)
    {
        var panel = new StackPanel();
        panel.Children.Add(new TextBlock { Text = label, Foreground = Brush("#71809A"), FontSize = 11 });
        panel.Children.Add(new TextBlock { Text = value, Foreground = Brush("#183A70"), FontSize = 25, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 4, 0, 0) });
        panel.Children.Add(new TextBlock { Text = hint, Foreground = Brush("#94A0B2"), FontSize = 10, Margin = new Thickness(0, 2, 0, 0) });
        return new Border { Width = 190, Height = 98, CornerRadius = new CornerRadius(17), Background = Brush("#F3F7FD"), BorderBrush = Brush("#E2E9F4"), BorderThickness = new Thickness(1), Padding = new Thickness(16, 13, 16, 10), Margin = new Thickness(0, 0, 12, 0), Child = panel };
    }

    private static Image LogoImage(double size) => new()
    {
        Source = new BitmapImage(new Uri("pack://application:,,,/Assets/UsSDAU-logo-square-v2.png")),
        Width = size, Height = size, Stretch = Stretch.Uniform
    };

    private static void AddInfo(Grid grid, int row, int column, string label, string value)
    {
        var labelText = new TextBlock { Text = label, FontFamily = new FontFamily("SimHei, Times New Roman"), FontSize = 10, FontWeight = FontWeights.Bold, VerticalAlignment = VerticalAlignment.Center };
        Grid.SetRow(labelText, row); Grid.SetColumn(labelText, column); grid.Children.Add(labelText);
        var valueText = new TextBlock { Text = value, FontFamily = new FontFamily("SimSun, Times New Roman"), FontSize = 11.5, VerticalAlignment = VerticalAlignment.Center };
        Grid.SetRow(valueText, row); Grid.SetColumn(valueText, column + 1); grid.Children.Add(valueText);
    }

    private static StackPanel TranscriptHeader(string chinese, string english)
    {
        var panel = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        panel.Children.Add(new TextBlock { Text = chinese, FontFamily = new FontFamily("SimHei"), FontSize = 10.5, FontWeight = FontWeights.Bold, HorizontalAlignment = HorizontalAlignment.Center });
        panel.Children.Add(new TextBlock { Text = english, FontFamily = new FontFamily("Times New Roman"), FontSize = 8.5, FontWeight = FontWeights.Bold, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 2, 0, 0) });
        return panel;
    }

    private static TextBlock TranscriptText(string text, string fontFamily, TextAlignment alignment, FontWeight? weight = null) => new()
    {
        Text = text,
        FontFamily = new FontFamily(fontFamily),
        FontSize = 10.5,
        FontWeight = weight ?? FontWeights.Normal,
        TextAlignment = alignment,
        VerticalAlignment = VerticalAlignment.Center,
        TextWrapping = TextWrapping.Wrap
    };

    private static Border AddTranscriptCell(Grid table, int row, int column, UIElement content, bool header = false)
    {
        var border = new Border
        {
            BorderBrush = Brush("#28313D"), BorderThickness = new Thickness(.65),
            Background = header ? Brush("#F1F2F4") : Brushes.Transparent,
            Padding = new Thickness(column == 1 ? 10 : 5, 3, column == 1 ? 10 : 5, 3), Child = content
        };
        Grid.SetRow(border, row); Grid.SetColumn(border, column); table.Children.Add(border);
        return border;
    }

    private static void AddTranscriptTermRow(Grid table, int row, string term)
    {
        var border = new Border
        {
            BorderBrush = Brush("#28313D"), BorderThickness = new Thickness(.65), Background = Brush("#E9EDF2"),
            Padding = new Thickness(10, 4, 10, 4),
            Child = new TextBlock { Text = term, FontFamily = new FontFamily("SimHei, Times New Roman"), FontWeight = FontWeights.Bold, FontSize = 10.5, VerticalAlignment = VerticalAlignment.Center }
        };
        Grid.SetRow(border, row); Grid.SetColumn(border, 0); Grid.SetColumnSpan(border, 5); table.Children.Add(border);
    }

    private static void AddSummaryMetric(Grid grid, int column, string label, string value)
    {
        var panel = new StackPanel { Margin = new Thickness(12, 10, 12, 10) };
        panel.Children.Add(new TextBlock { Text = label, FontFamily = new FontFamily("SimHei"), FontSize = 9.5, HorizontalAlignment = HorizontalAlignment.Center });
        panel.Children.Add(new TextBlock { Text = value, FontFamily = new FontFamily("Times New Roman"), FontSize = 15, FontWeight = FontWeights.Bold, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 3, 0, 0) });
        var border = new Border { BorderBrush = Brush("#CBD1D9"), BorderThickness = new Thickness(column == 0 ? 0 : 1, 0, 0, 0), Child = panel };
        Grid.SetColumn(border, column); grid.Children.Add(border);
    }

    private void ChangeDay(DateTime date)
    {
        _day = date.Date;
        _controller.SetDisplayDate(_day);
    }

    private void SetActiveNav(string page)
    {
        foreach (var button in new[] { DayNav, WeekNav, PublicNav, ScoresNav })
        {
            var selected = button.Tag?.ToString() == page;
            button.Background = selected ? Brush("#EAF2FF") : Brushes.Transparent;
            button.Foreground = selected ? Brush("#1D5FD1") : Brush("#52647D");
        }
    }

    private void Settings_Click(object sender, RoutedEventArgs e) => new SettingsWindow(_controller, false) { Owner = this }.ShowDialog();
    private void Hide_Click(object sender, RoutedEventArgs e) => Hide();
    protected override void OnClosing(System.ComponentModel.CancelEventArgs e) { if (!_closingForExit) { e.Cancel = true; Hide(); } base.OnClosing(e); }
    public void CloseForExit() { _closingForExit = true; Close(); }
}
