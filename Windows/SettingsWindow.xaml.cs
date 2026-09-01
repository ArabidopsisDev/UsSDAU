using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using UsSDAU.Models;
using UsSDAU.Services;

namespace UsSDAU.Windows;

public partial class SettingsWindow : Window
{
    private readonly AppController _controller;
    private readonly bool _firstRun;

    public SettingsWindow(AppController controller, bool firstRun)
    {
        InitializeComponent();
        _controller = controller;
        _firstRun = firstRun;
        TitleText.Text = firstRun ? "先配置教务系统账号" : "账号与运行设置";
        AccountBox.Text = controller.Settings.Account;
        PasswordBox.Password = "";
        TermBox.Text = controller.Settings.Term;
        StartDatePicker.SelectedDate = controller.Settings.TermStartDate;
        IslandCheck.IsChecked = controller.Settings.IslandEnabled;
        CompactIslandCheck.IsChecked = controller.Settings.CompactIslandUntilImminent;
        ReminderCheck.IsChecked = controller.Settings.ReminderEnabled;
        StartupCheck.IsChecked = controller.Settings.StartWithWindows;
    }

    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        var account = AccountBox.Text.Trim();
        var passwordWasEntered = !string.IsNullOrWhiteSpace(PasswordBox.Password);
        var password = PasswordBox.Password;
        var term = TermBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(account) || string.IsNullOrWhiteSpace(term))
        {
            StatusText.Text = "请填写学号和学期。";
            return;
        }
        if (string.IsNullOrWhiteSpace(password) && account == _controller.Settings.Account)
            password = new SettingsService().ReadPassword(account);
        if (string.IsNullOrWhiteSpace(password))
        {
            StatusText.Text = "请输入教务密码。";
            return;
        }

        SaveButton.IsEnabled = false;
        SaveButton.Content = "正在验证…";
        StatusText.Text = "正在登录教务系统并读取课表，请稍候。";
        try
        {
            var settings = new AcademicSettings
            {
                Account = account, Term = term,
                ScoreTerm = _controller.Settings.ScoreTerm,
                TermStartDate = StartDatePicker.SelectedDate?.Date ?? TermPolicy.InferTermStart(term),
                IslandEnabled = IslandCheck.IsChecked == true,
                CompactIslandUntilImminent = CompactIslandCheck.IsChecked == true,
                ReminderEnabled = ReminderCheck.IsChecked == true,
                StartWithWindows = StartupCheck.IsChecked == true
            };
            var needsValidation = _firstRun || passwordWasEntered ||
                account != _controller.Settings.Account || term != _controller.Settings.Term ||
                _controller.Courses.Count == 0;
            await _controller.ConfigureAsync(settings, password, validate: needsValidation);
            DialogResult = true;
            Close();
        }
        catch (Exception ex)
        {
            StatusText.Text = ex.Message;
        }
        finally
        {
            SaveButton.IsEnabled = true;
            SaveButton.Content = "验证并保存";
        }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed && e.OriginalSource is not Button) DragMove();
    }
}
