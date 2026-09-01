using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using UsSDAU.Models;
using UsSDAU.Services;

namespace UsSDAU.Windows;

public partial class IslandWindow : Window
{
    private const double FullIslandWidth = 760;
    private const double CompactIslandWidth = 286;
    private readonly AppController _controller;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(15) };
    private readonly DispatcherTimer _collapseTimer = new() { Interval = TimeSpan.FromSeconds(8) };
    private string? _lastReminderKey;
    private string? _stateKey;
    private bool _collapsed;
    private bool _compactMode;
    private bool _closingForExit;

    public IslandWindow(AppController controller)
    {
        InitializeComponent();
        _controller = controller;
        SourceInitialized += (_, _) => MakeNonActivating();
        Loaded += (_, _) => { PositionAtTop(); ExpandAndScheduleCollapse(); };
        MouseEnter += (_, _) => ExpandAndScheduleCollapse();
        MouseLeave += (_, _) => ScheduleCollapse();
        _timer.Tick += (_, _) => RefreshNow();
        _collapseTimer.Tick += (_, _) => CollapseToEdge();
        _timer.Start();
    }

    public void RefreshNow()
    {
        if (!_controller.Settings.IslandEnabled)
        {
            Hide();
            return;
        }
        if (!IsVisible) Show();
        PositionAtTop();
        var now = DateTime.Now;
        var displayDate = _controller.DisplayDate.Date;
        var isToday = displayDate == DateTime.Today;
        var today = _controller.GetOccurrences(displayDate).ToList();
        var current = isToday ? today.FirstOrDefault(x => now >= x.Start && now <= x.End) : null;
        var next = isToday ? today.FirstOrDefault(x => x.Start > now) : today.FirstOrDefault();
        var imminent = isToday && next is not null && next.Start - now <= TimeSpan.FromMinutes(30);
        ApplyWidthMode(_controller.Settings.CompactIslandUntilImminent && !imminent);

        Shell.Background = imminent
            ? new LinearGradientBrush(Color.FromArgb(244, 16, 91, 214), Color.FromArgb(244, 22, 132, 255), 0)
            : new SolidColorBrush(Color.FromArgb(236, 23, 29, 43));

        if (current is not null)
        {
            StateText.Text = "正在上课";
            PrimaryText.Text = current.Course.Name;
            SecondaryText.Text = $"{current.Start:HH:mm}–{current.End:HH:mm}  {current.Course.Room}";
        }
        else if (imminent && next is not null)
        {
            var minutes = Math.Max(1, (int)Math.Ceiling((next.Start - now).TotalMinutes));
            StateText.Text = $"{minutes} 分钟后上课";
            PrimaryText.Text = next.Course.Name;
            SecondaryText.Text = $"{next.Start:HH:mm}–{next.End:HH:mm}  {next.Course.Room}";
            MaybeShowReminder(next);
        }
        else if (next is not null)
        {
            StateText.Text = isToday ? "下一节" : $"{displayDate:M月d日} · {displayDate:dddd}";
            PrimaryText.Text = next.Course.Name;
            SecondaryText.Text = $"{next.Start:HH:mm}–{next.End:HH:mm}  {next.Course.Room}";
        }
        else
        {
            StateText.Text = today.Count == 0 ? $"{displayDate:M月d日}没有课程" : "今日课程已结束";
            PrimaryText.Text = today.Count == 0 ? "留一点时间给自己" : "辛苦了";
            SecondaryText.Text = $"{displayDate:yyyy年M月d日 dddd}";
        }
        RenderDayStrip(today, current, next);
        var nextState = $"{displayDate:yyyyMMdd}|{current?.Course.CourseCode}|{next?.Course.CourseCode}|{imminent}";
        if (_stateKey != nextState)
        {
            _stateKey = nextState;
            ExpandAndScheduleCollapse();
        }
    }

    private void RenderDayStrip(IReadOnlyList<CourseOccurrence> courses, CourseOccurrence? current, CourseOccurrence? next)
    {
        DayStrip.Items.Clear();
        var visible = courses.Take(5).ToList();
        if (visible.Count == 0) return;
        var available = DayStrip.ActualWidth > 120 ? DayStrip.ActualWidth : 455;
        const double gap = 6;
        var cardWidth = Math.Max(76, (available - gap * (visible.Count - 1)) / visible.Count);
        for (var index = 0; index < visible.Count; index++)
        {
            var item = visible[index];
            var active = ReferenceEquals(item, current) || ReferenceEquals(item, next);
            var panel = new System.Windows.Controls.Border
            {
                Width = cardWidth, Height = 52, CornerRadius = new CornerRadius(15), Margin = new Thickness(0, 0, index == visible.Count - 1 ? 0 : gap, 0),
                Background = new SolidColorBrush(active ? Color.FromArgb(46, 255, 255, 255) : Color.FromArgb(22, 255, 255, 255)),
                Child = new System.Windows.Controls.StackPanel
                {
                    Margin = new Thickness(10, 7, 8, 4),
                    Children =
                    {
                        new System.Windows.Controls.TextBlock { Text = item.Start.ToString("HH:mm"), Foreground = new SolidColorBrush(Color.FromRgb(174, 207, 242)), FontSize = 10 },
                        new System.Windows.Controls.TextBlock { Text = item.Course.Name, Foreground = Brushes.White, FontSize = 12, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis }
                    }
                }
            };
            DayStrip.Items.Add(panel);
        }
    }

    private void MaybeShowReminder(CourseOccurrence next)
    {
        if (!_controller.Settings.ReminderEnabled || FullScreenDetector.IsForegroundFullScreen()) return;
        var key = $"{next.Start:yyyyMMddHHmm}|{next.Course.Name}";
        if (_lastReminderKey == key) return;
        _lastReminderKey = key;
        ExpandAndScheduleCollapse();
        new ReminderWindow(next).Show();
    }

    private void PositionAtTop()
    {
        var area = SystemParameters.WorkArea;
        Left = area.Left + (area.Width - Width) / 2;
        if (!IsLoaded) Top = area.Top + 8;
        else if (_collapsed) Top = area.Top - Height + 22;
        else if (!Top.Equals(area.Top + 8)) Top = area.Top + 8;
    }

    private void ExpandAndScheduleCollapse()
    {
        if (!IsLoaded) return;
        _collapsed = false;
        AnimateTop(SystemParameters.WorkArea.Top + 8);
        ScheduleCollapse();
    }

    private void ScheduleCollapse()
    {
        _collapseTimer.Stop();
        _collapseTimer.Start();
    }

    private void CollapseToEdge()
    {
        _collapseTimer.Stop();
        if (IsMouseOver)
        {
            ScheduleCollapse();
            return;
        }
        _collapsed = true;
        AnimateTop(SystemParameters.WorkArea.Top - Height + 22);
    }

    private void AnimateTop(double target)
    {
        BeginAnimation(TopProperty, null);
        var animation = new DoubleAnimation(Top, target, TimeSpan.FromMilliseconds(280))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
            FillBehavior = FillBehavior.Stop
        };
        animation.Completed += (_, _) => { BeginAnimation(TopProperty, null); Top = target; };
        BeginAnimation(TopProperty, animation);
    }

    private void ApplyWidthMode(bool compact)
    {
        if (_compactMode == compact && Math.Abs(Width - (compact ? CompactIslandWidth : FullIslandWidth)) < 1) return;
        _compactMode = compact;
        var targetWidth = compact ? CompactIslandWidth : FullIslandWidth;
        var targetLeft = SystemParameters.WorkArea.Left + (SystemParameters.WorkArea.Width - targetWidth) / 2;

        if (!compact)
        {
            ScheduleColumn.Width = new GridLength(1, GridUnitType.Star);
            Divider.Visibility = Visibility.Visible;
            DayStrip.Visibility = Visibility.Visible;
        }
        else
        {
            ScheduleColumn.Width = new GridLength(0);
            Divider.Visibility = Visibility.Collapsed;
            DayStrip.Visibility = Visibility.Collapsed;
        }

        BeginAnimation(WidthProperty, null);
        BeginAnimation(LeftProperty, null);
        var easing = new CubicEase { EasingMode = EasingMode.EaseOut };
        var widthAnimation = new DoubleAnimation(Width, targetWidth, TimeSpan.FromMilliseconds(300)) { EasingFunction = easing, FillBehavior = FillBehavior.Stop };
        var leftAnimation = new DoubleAnimation(Left, targetLeft, TimeSpan.FromMilliseconds(300)) { EasingFunction = easing, FillBehavior = FillBehavior.Stop };
        widthAnimation.Completed += (_, _) => { BeginAnimation(WidthProperty, null); Width = targetWidth; };
        leftAnimation.Completed += (_, _) => { BeginAnimation(LeftProperty, null); Left = targetLeft; };
        BeginAnimation(WidthProperty, widthAnimation);
        BeginAnimation(LeftProperty, leftAnimation);
    }

    private void MakeNonActivating()
    {
        var handle = new WindowInteropHelper(this).Handle;
        var style = GetWindowLongPtr(handle, -20).ToInt64();
        SetWindowLongPtr(handle, -20, new IntPtr(style | 0x00000080L | 0x08000000L));
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        if (!_closingForExit) { e.Cancel = true; Hide(); }
        base.OnClosing(e);
    }

    public void CloseForExit() { _closingForExit = true; _timer.Stop(); _collapseTimer.Stop(); Close(); }

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] private static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] private static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int index, IntPtr value);
}
