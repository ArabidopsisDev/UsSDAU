using System.Windows;
using UsSDAU.Services;
using UsSDAU.Windows;

namespace UsSDAU;

public partial class App : System.Windows.Application
{
    private TrayService? _tray;
    private MainWindow? _mainWindow;
    private IslandWindow? _islandWindow;
    private AppController? _controller;
    private Mutex? _singleInstance;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        ShutdownMode = ShutdownMode.OnExplicitShutdown;
        _singleInstance = new Mutex(true, "Local\\UsSDAU.SingleInstance", out var firstInstance);
        if (!firstInstance)
        {
            Shutdown();
            return;
        }

        _controller = new AppController(new SettingsService(), new CacheService(), new SdauAcademicService());
        _mainWindow = new MainWindow(_controller);
        _islandWindow = new IslandWindow(_controller);
        _tray = new TrayService(ShowMainWindow, SyncFromTrayAsync, ShowSettings, ExitApplication);

        _controller.DataChanged += (_, _) => _islandWindow.RefreshNow();
        _islandWindow.Show();

        if (!_controller.HasAccount)
        {
            var configured = await ShowSettingsAsync(firstRun: true);
            if (!configured)
                _tray.ShowInfo("UsSDAU 正在托盘运行", "尚未配置教务账号，可从托盘菜单进入设置。");
        }
        else
        {
            await _controller.LoadCachedDataAsync();
            _islandWindow.RefreshNow();
            _tray.ShowInfo("UsSDAU 已启动", "课表灵动岛已在桌面顶部运行。");
        }
        if (e.Args.Any(x => x.Equals("--open", StringComparison.OrdinalIgnoreCase))) ShowMainWindow();
    }

    private void ShowMainWindow()
    {
        if (_mainWindow is null) return;
        _mainWindow.RefreshAllViews();
        _mainWindow.Show();
        _mainWindow.WindowState = WindowState.Normal;
        _mainWindow.Activate();
    }

    private async Task SyncFromTrayAsync()
    {
        if (_controller is null) return;
        try
        {
            await _controller.RefreshCoursesAsync();
            _tray?.ShowInfo("课表已更新", "已从教务系统获取最新个人课表。");
        }
        catch (Exception ex)
        {
            _tray?.ShowInfo("课表更新失败", ex.Message);
        }
    }

    private void ShowSettings() => _ = ShowSettingsAsync(firstRun: false);

    private async Task<bool> ShowSettingsAsync(bool firstRun)
    {
        if (_controller is null) return false;
        var dialog = new SettingsWindow(_controller, firstRun);
        var result = dialog.ShowDialog() == true;
        if (result)
        {
            await _controller.LoadCachedDataAsync();
            _mainWindow?.RefreshAllViews();
            _islandWindow?.RefreshNow();
        }
        return result;
    }

    private void ExitApplication()
    {
        _tray?.Dispose();
        _islandWindow?.CloseForExit();
        _mainWindow?.CloseForExit();
        _singleInstance?.ReleaseMutex();
        _singleInstance?.Dispose();
        Shutdown();
    }
}
