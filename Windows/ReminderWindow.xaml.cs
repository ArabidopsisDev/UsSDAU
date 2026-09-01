using System.Windows;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using UsSDAU.Models;

namespace UsSDAU.Windows;

public partial class ReminderWindow : Window
{
    public ReminderWindow(CourseOccurrence occurrence)
    {
        InitializeComponent();
        CourseText.Text = occurrence.Course.Name;
        MetaText.Text = $"{occurrence.Start:HH:mm}–{occurrence.End:HH:mm}  ·  {occurrence.Course.Room}";
        Loaded += (_, _) =>
        {
            var area = SystemParameters.WorkArea;
            Left = area.Right - Width - 24;
            Top = area.Bottom - Height - 24;
            BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(220)));
            var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(8) };
            timer.Tick += (_, _) => { timer.Stop(); Close(); };
            timer.Start();
        };
    }
}
