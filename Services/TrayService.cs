using System.Drawing;
using System.Windows;

namespace UsSDAU.Services;

public sealed class TrayService : IDisposable
{
    private readonly System.Windows.Forms.NotifyIcon _icon;
    private readonly Icon _appIcon;
    private readonly Stream _iconStream;

    public TrayService(Action show, Func<Task> sync, Action settings, Action exit)
    {
        var menu = new System.Windows.Forms.ContextMenuStrip();
        menu.Items.Add("打开 UsSDAU", null, (_, _) => show());
        menu.Items.Add("立即更新课表", null, async (_, _) => await sync());
        menu.Items.Add("账号与设置", null, (_, _) => settings());
        menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
        menu.Items.Add("退出", null, (_, _) => exit());
        _iconStream = Application.GetResourceStream(new Uri("pack://application:,,,/Assets/UsSDAU-tray-v2.ico"))?.Stream
            ?? throw new InvalidOperationException("无法加载 UsSDAU 品牌图标资源。");
        _appIcon = new Icon(_iconStream);
        _icon = new System.Windows.Forms.NotifyIcon
        {
            Text = "UsSDAU · 山农课表",
            Icon = _appIcon,
            Visible = true,
            ContextMenuStrip = menu
        };
        _icon.DoubleClick += (_, _) => show();
    }

    public void ShowInfo(string title, string message)
    {
        _icon.BalloonTipTitle = title;
        _icon.BalloonTipText = message;
        _icon.BalloonTipIcon = System.Windows.Forms.ToolTipIcon.Info;
        _icon.ShowBalloonTip(3200);
    }

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Dispose();
        _appIcon.Dispose();
        _iconStream.Dispose();
    }
}
