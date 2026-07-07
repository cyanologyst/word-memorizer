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
        const int width = 430;
        const int height = 150;

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
        var word = App.Data.PickNextWord(DateTimeOffset.Now);
        CurrentWordText.Text = word?.Term ?? "No word due";
        StatusText.Text = App.Data.IsPaused(DateTimeOffset.Now)
            ? $"Paused until {App.Data.PausedUntil?.ToLocalTime():HH:mm}"
            : $"Next in {App.Data.Settings.ReminderIntervalMinutes} min";

        DueText.Text = $"{App.Data.DueNowCount:N0} due";
        DailyProgressBar.Value = App.Data.DailyGoalProgress;
        MetaText.Text = $"{App.Data.ReviewStreakDays}d streak - {App.Data.DueNowCount:N0} due";
    }

    private void ReviewButton_Click(object sender, RoutedEventArgs e)
    {
        App.MainWindow?.Activate();
    }

    private void Pause15Button_Click(object sender, RoutedEventArgs e)
    {
        PauseFor(TimeSpan.FromMinutes(15));
    }

    private void Pause30Button_Click(object sender, RoutedEventArgs e)
    {
        PauseFor(TimeSpan.FromMinutes(30));
    }

    private void Pause60Button_Click(object sender, RoutedEventArgs e)
    {
        PauseFor(TimeSpan.FromHours(1));
    }

    private void PauseTomorrowButton_Click(object sender, RoutedEventArgs e)
    {
        var tomorrowMorning = new DateTimeOffset(DateTimeOffset.Now.Date.AddDays(1).AddHours(8));
        PauseFor(tomorrowMorning - DateTimeOffset.Now);
    }

    private void PauseFor(TimeSpan duration)
    {
        App.Data.PauseFor(duration);
        Render();
    }
}
