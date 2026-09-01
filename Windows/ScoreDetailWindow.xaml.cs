using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using UsSDAU.Models;

namespace UsSDAU.Windows;

public partial class ScoreDetailWindow : Window
{
    public ScoreDetailWindow(ScoreRecord score, ScoreDetail detail)
    {
        InitializeComponent();
        CourseText.Text = score.CourseName;
        MetaText.Text = $"{score.CourseCode}  ·  {score.Credit} 学分  ·  GPA {score.Gpa}";
        UsualText.Text = detail.UsualScore;
        FinalText.Text = detail.FinalScore;
        TotalText.Text = detail.TotalScore;

        var usualScore = Numeric(detail.UsualScore);
        var finalScore = Numeric(detail.FinalScore);
        var usualRatio = Numeric(detail.UsualRatio);
        var finalRatio = Numeric(detail.FinalRatio);
        UsualProgress.Value = usualScore;
        FinalProgress.Value = finalScore;
        UsualRatioText.Text = $"权重 {detail.UsualRatio}%";
        FinalRatioText.Text = $"权重 {detail.FinalRatio}%";
        UsualContributionText.Text = $"计入总成绩 {Contribution(usualScore, usualRatio):0.##} 分";
        FinalContributionText.Text = $"计入总成绩 {Contribution(finalScore, finalRatio):0.##} 分";
        FormulaText.Text = $"{detail.UsualScore} × {detail.UsualRatio}%  +  {detail.FinalScore} × {detail.FinalRatio}%  =  {detail.TotalScore}";

        if (Numeric(detail.TotalScore) < 60)
        {
            TotalText.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#C43D3D"));
        }
    }

    private static double Numeric(string value) => double.TryParse(value.Trim().TrimEnd('%'), NumberStyles.Any, CultureInfo.InvariantCulture, out var number) ? number : 0;
    private static double Contribution(double score, double ratio) => score * ratio / 100d;
    private void Close_Click(object sender, RoutedEventArgs e) => Close();
    private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) { if (e.ButtonState == MouseButtonState.Pressed) DragMove(); }
}
