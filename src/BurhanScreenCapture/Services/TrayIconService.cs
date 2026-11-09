using System.Windows;
using WinForms = System.Windows.Forms;

namespace BurhanScreenCapture.Services;

public sealed class TrayIconService : IDisposable
{
    private readonly WinForms.NotifyIcon _icon;
    private Action? _balloonClick;
    private readonly WinForms.ToolStripMenuItem _recordItem;

    public TrayIconService()
    {
        var stream = Application.GetResourceStream(new Uri("pack://application:,,,/BurhanScreenCapture;component/Assets/app.ico"))?.Stream;
        _icon = new WinForms.NotifyIcon
        {
            Icon = stream is null ? System.Drawing.SystemIcons.Application : new System.Drawing.Icon(stream),
            Text = "Burhan Screen Capture Pro",
            Visible = true
        };
        var menu = new WinForms.ContextMenuStrip();
        menu.Items.Add("Capture Area", null, (_, _) => Run(() => Workflows.CaptureRegion()));
        menu.Items.Add("Capture Full Screen", null, (_, _) => Run(() => Workflows.CaptureFullScreen(null)));
        menu.Items.Add("Capture Window", null, (_, _) => Run(() => Workflows.CaptureActiveWindow(false)));
        menu.Items.Add(new WinForms.ToolStripSeparator());
        _recordItem = new WinForms.ToolStripMenuItem("Start Recording", null, (_, _) => Run(() => RecorderController.ToggleQuick()));
        menu.Items.Add(_recordItem);
        menu.Items.Add("Image Editor", null, (_, _) => Run(() => Workflows.OpenEditorEmpty()));
        menu.Items.Add("Quick Toolbar", null, (_, _) => Run(Views.FloatingToolbar.ShowToolbar));
        menu.Items.Add(new WinForms.ToolStripSeparator());
        menu.Items.Add("Open Burhan Screen Capture", null, (_, _) => Run(App.ShowMainWindow));
        menu.Items.Add("Exit", null, (_, _) => Run(App.ExitApplication));
        menu.Opening += (_, _) => _recordItem.Text = RecorderController.IsBusy ? "Stop Recording" : "Start Recording";
        _icon.ContextMenuStrip = menu;
        _icon.MouseClick += (_, e) => { if (e.Button == WinForms.MouseButtons.Left) Run(App.ShowMainWindow); };
        _icon.BalloonTipClicked += (_, _) => { var a = _balloonClick; _balloonClick = null; if (a != null) Run(a); };
    }

    private static void Run(Action a) => Application.Current.Dispatcher.BeginInvoke(a);

    public void Notify(string title, string text, Action? onClick = null, bool error = false)
    {
        if (!error && !SettingsService.Current.ShowNotifications) return;
        _balloonClick = onClick;
        _icon.ShowBalloonTip(4000, title, text, error ? WinForms.ToolTipIcon.Warning : WinForms.ToolTipIcon.Info);
    }

    public void SetRecording(bool recording) =>
        _icon.Text = recording ? "Burhan Screen Capture Pro — Recording" : "Burhan Screen Capture Pro";

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Dispose();
    }
}
