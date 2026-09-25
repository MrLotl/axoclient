using System.Windows;
using Forms = System.Windows.Forms;

namespace AxoClient.Platform;

public sealed class TrayIcon : IDisposable
{
    private readonly Forms.NotifyIcon _icon;

    public event Action? ShowRequested;
    public event Action? ExitRequested;

    public TrayIcon()
    {
        _icon = new Forms.NotifyIcon { Text = AppInfo.Name, Visible = false };
        try
        {
            var stream = Application.GetResourceStream(new Uri("pack://application:,,,/Assets/axoclient.ico"))?.Stream;
            if (stream != null)
                using (stream)
                    _icon.Icon = new System.Drawing.Icon(stream, Forms.SystemInformation.SmallIconSize);
        }
        catch (Exception ex)
        {
            ErrorReport.Log("Symbol für den Infobereich laden", ex);
        }
        _icon.Icon ??= System.Drawing.SystemIcons.Application;

        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("AxoClient öffnen", null, (_, _) => ShowRequested?.Invoke());
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("Launcher beenden", null, (_, _) => ExitRequested?.Invoke());
        _icon.ContextMenuStrip = menu;
        _icon.MouseClick += (_, e) =>
        {
            if (e.Button == Forms.MouseButtons.Left)
                ShowRequested?.Invoke();
        };
    }

    public bool Visible
    {
        get => _icon.Visible;
        set => _icon.Visible = value;
    }

    public void ShowHint(string text) => _icon.ShowBalloonTip(3000, AppInfo.Name, text, Forms.ToolTipIcon.None);

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.ContextMenuStrip?.Dispose();
        _icon.Dispose();
    }
}
