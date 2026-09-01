using System.Windows;
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
        UsualRatioText.Text = $"占比 {detail.UsualRatio}%";
        FinalText.Text = detail.FinalScore;
        FinalRatioText.Text = $"占比 {detail.FinalRatio}%";
        TotalText.Text = detail.TotalScore;
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
