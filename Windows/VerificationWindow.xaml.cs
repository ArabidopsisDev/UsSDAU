using System.Windows;
using System.Windows.Media;
using UsSDAU.Services;

namespace UsSDAU.Windows;

public partial class VerificationWindow : Window
{
    private readonly TranscriptVerificationService _verification;
    public VerificationWindow(TranscriptVerificationService verification)
    {
        InitializeComponent();
        _verification = verification;
    }

    private void Paste_Click(object sender, RoutedEventArgs e)
    {
        if (Clipboard.ContainsText()) PayloadBox.Text = Clipboard.GetText().Trim();
    }

    private void Verify_Click(object sender, RoutedEventArgs e)
    {
        var result = _verification.Verify(PayloadBox.Text.Trim());
        ResultCard.Visibility = Visibility.Visible;
        ResultTitle.Text = result.IsValid ? "签名有效 · 数据未被修改" : "校验失败";
        ResultTitle.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(result.IsValid ? "#147D55" : "#C43D3D"));
        ResultText.Text = result.IsValid
            ? $"{result.Message}\n学号：{result.StudentNo}\n范围：{result.Scope}\n课程数：{result.CourseCount}\n签发时间：{result.IssuedAt.LocalDateTime:yyyy-MM-dd HH:mm}\n本机签名公钥指纹：{result.Fingerprint}\n\n该结果证明二维码载荷自生成后未被篡改，不代表山东农业大学官方认证。"
            : result.Message;
    }
}
