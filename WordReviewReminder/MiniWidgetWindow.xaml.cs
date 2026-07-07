using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Windows.Graphics;
using WinRT.Interop;

namespace WordReviewReminder;

public sealed partial class MiniWidgetWindow : Window
{
    private readonly DispatcherTimer _timer = new();

    public MiniWidgetWindow()
    {
        InitializeComponent();
        ExtendsContentIntoTitleBar = true;
        AppWindow.SetIcon("Assets/AppIcon.ico");
        ConfigureWindow();
        Render();

        _timer.Interval = TimeSpan.FromSeconds(10);
        _timer.Tick += (_, _) => Render();
        _timer.Start();
    }

    private void ConfigureWindow()
    {
        const int width = 380;
        const int height = 116;

        AppWindow.Resize(new SizeInt32(width, height));
        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.IsResizable = false;
            presenter.IsMaximizable = false;
            presenter.IsMinimizable = false;
            presenter.IsAlwaysOnTop = true;
            presenter.SetBorderAndTitleBar(false, false);
        }

        var hWnd = WindowNative.GetWindowHandle(this);
        var windowId = Win32Interop.GetWindowIdFromWindow(hWnd);
        var displayArea = DisplayArea.GetFromWindowId(windowId, DisplayAreaFallback.Primary);
        AppWindow.Move(new PointInt32(
            displayArea.WorkArea.X + displayArea.WorkArea.Width - width - 24,
            displayArea.WorkArea.Y + displayArea.WorkArea.Height - height - 24));
    }

    private void Render()
    {
        StatusText.Text = App.Data.IsPaused(DateTimeOffset.Now)
            ? $"Paused until {App.Data.PausedUntil?.ToLocalTime():HH:mm}"
            : $"Next in {App.Data.Settings.ReminderIntervalMinutes} min";

        MetaText.Text = $"{App.Data.ReviewStreakDays}d streak - {App.Data.DueNowCount:N0} due";
    }

    private void ReviewButton_Click(object sender, RoutedEventArgs e)
    {
        App.MainWindow?.Activate();
    }

    private void PauseButton_Click(object sender, RoutedEventArgs e)
    {
        App.Data.PauseFor(TimeSpan.FromMinutes(30));
        Render();
    }
}
