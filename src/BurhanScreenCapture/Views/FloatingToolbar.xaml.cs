using System.Windows;
using System.Windows.Input;
using BurhanScreenCapture.Services;

namespace BurhanScreenCapture.Views;

/// <summary>Compact always-on-top toolbar for capturing without opening the dashboard.</summary>
public partial class FloatingToolbar : Window
{
    private static FloatingToolbar? _instance;

    public static void ShowToolbar()
    {
        if (_instance is null)
        {
            _instance = new FloatingToolbar();
            _instance.Closed += (_, _) => _instance = null;
        }
        _instance.Show();
        _instance.Activate();
    }

    public static void HideToolbar() => _instance?.Close();

    private FloatingToolbar()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            var wa = SystemParameters.WorkArea;
            Left = wa.Left + (wa.Width - ActualWidth) / 2;
            Top = wa.Top + 12;
        };
        RecorderController.StateChanged += UpdateRec;
        Closed += (_, _) => RecorderController.StateChanged -= UpdateRec;
    }

    private void UpdateRec() => Dispatcher.BeginInvoke(() => RecGlyph.Text = RecorderController.IsBusy ? "■" : "●");

    private void OnDrag(object sender, MouseButtonEventArgs e) { try { DragMove(); } catch { } }
    private void OnArea(object sender, RoutedEventArgs e) => Workflows.CaptureRegion();
    private void OnFull(object sender, RoutedEventArgs e) => Workflows.CaptureFullScreen(null);
    private void OnWindow(object sender, RoutedEventArgs e) => Workflows.CaptureActiveWindow(true);
    private void OnDelay(object sender, RoutedEventArgs e) => Workflows.CaptureDelayed(CaptureMode.Region, 5);
    private void OnRecord(object sender, RoutedEventArgs e) => RecorderController.ToggleQuick();
    private void OnEditor(object sender, RoutedEventArgs e) => Workflows.OpenEditorEmpty();
    private void OnHome(object sender, RoutedEventArgs e) => App.ShowMainWindow();
    private void OnClose(object sender, RoutedEventArgs e) => Close();
}
