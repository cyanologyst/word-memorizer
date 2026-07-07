using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using WordReviewReminder.Core;
using WordReviewReminder.Services;

namespace WordReviewReminder;

public sealed class ReminderOverlayWindow : System.Windows.Window
{
    private const double CardWidth = 560;
    private const double CardHeight = 328;

    private readonly Func<ReviewAction, Task> _recordActionAsync;
    private readonly SpeechService _speech = new();
    private readonly WordEntry _word;
    private readonly DispatcherTimer _closeTimer = new();
    private readonly int _durationSeconds;
    private Border? _feedbackOverlay;
    private ScaleTransform? _timerScale;
    private bool _recorded;

    public ReminderOverlayWindow(WordEntry word, int durationSeconds, Func<ReviewAction, Task> recordActionAsync)
    {
        _word = word;
        _recordActionAsync = recordActionAsync;
        _durationSeconds = Math.Max(5, durationSeconds);

        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        SizeToContent = SizeToContent.Manual;
        Topmost = true;
        WindowStyle = WindowStyle.None;
        Width = CardWidth;
        Height = CardHeight;
        Content = BuildCard();

        Loaded += (_, _) =>
        {
            PositionWindow();
            StartTimerAnimation();
        };

        _closeTimer.Interval = TimeSpan.FromSeconds(_durationSeconds);
        _closeTimer.Tick += (_, _) =>
        {
            _closeTimer.Stop();
            Close();
        };
        _closeTimer.Start();
    }

    public new bool Activate()
    {
        if (!IsVisible)
        {
            Show();
        }

        return base.Activate();
    }

    private UIElement BuildCard()
    {
        var root = new Grid
        {
            Width = Width,
            Height = Height,
            Margin = new Thickness(0)
        };

        var card = new Border
        {
            Width = CardWidth,
            Height = CardHeight,
            Background = Brush("#2A2225"),
            BorderThickness = new Thickness(0),
            CornerRadius = new CornerRadius(20),
            SnapsToDevicePixels = true
        };
        root.Children.Add(card);

        var layout = new Grid();
        layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(5) });
        layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        card.Child = layout;

        layout.Children.Add(new Border
        {
            Background = Brush("#FF7A5F"),
            CornerRadius = new CornerRadius(20, 0, 0, 20)
        });

        var body = new Grid
        {
            Margin = new Thickness(24, 20, 20, 18)
        };
        body.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        body.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        body.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        body.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        Grid.SetColumn(body, 1);
        layout.Children.Add(body);

        body.Children.Add(BuildTopStrip());
        body.Children.Add(BuildHeader());
        body.Children.Add(BuildMeaningPanel());
        body.Children.Add(BuildActions());

        _feedbackOverlay = BuildFeedbackOverlay();
        root.Children.Add(_feedbackOverlay);

        return root;
    }

    private UIElement BuildTopStrip()
    {
        var grid = new Grid { Height = 16 };
        var track = new Border
        {
            Height = 3,
            VerticalAlignment = VerticalAlignment.Center,
            Background = Brush("#48393E"),
            CornerRadius = new CornerRadius(2)
        };
        var fill = new Border
        {
            Height = 3,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Center,
            Background = Brush("#FF7A5F"),
            CornerRadius = new CornerRadius(2),
            RenderTransformOrigin = new Point(0, 0.5)
        };
        _timerScale = new ScaleTransform(1, 1);
        fill.RenderTransform = _timerScale;
        grid.Children.Add(track);
        grid.Children.Add(fill);
        Grid.SetRow(grid, 0);
        return grid;
    }

    private void StartTimerAnimation()
    {
        if (_timerScale is null)
        {
            return;
        }

        var animation = new DoubleAnimation
        {
            From = 1,
            To = 0,
            Duration = TimeSpan.FromSeconds(_durationSeconds),
            FillBehavior = FillBehavior.HoldEnd
        };
        _timerScale.BeginAnimation(ScaleTransform.ScaleXProperty, animation);
    }

    private UIElement BuildHeader()
    {
        var grid = new Grid { Margin = new Thickness(0, 10, 0, 12) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetRow(grid, 1);

        var copy = new StackPanel { Orientation = Orientation.Vertical };
        var chips = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 12) };
        chips.Children.Add(BuildChip("TOEFL Review", "#5B3333"));
        chips.Children.Add(BuildChip(BuildSourceText(_word), "#3A3033", new Thickness(8, 0, 0, 0)));

        copy.Children.Add(chips);
        copy.Children.Add(new TextBlock
        {
            Text = _word.Term,
            Foreground = Brushes.White,
            FontFamily = new FontFamily("Segoe UI Variable Display, Segoe UI"),
            FontSize = 37,
            FontWeight = FontWeights.SemiBold,
            LineHeight = 42,
            TextTrimming = TextTrimming.CharacterEllipsis
        });
        copy.Children.Add(new TextBlock
        {
            Text = $"{_word.PartOfSpeech}  {_word.Pronunciation}".Trim(),
            Foreground = Brush("#D8CBCF"),
            FontSize = 13,
            Margin = new Thickness(0, 6, 0, 0),
            TextTrimming = TextTrimming.CharacterEllipsis
        });
        grid.Children.Add(copy);

        var listenButton = BuildIconButton("\uE767", "Listen");
        listenButton.MouseLeftButtonUp += async (_, _) => await _speech.SpeakAsync(_word.Term);
        Grid.SetColumn(listenButton, 1);
        grid.Children.Add(listenButton);

        return grid;
    }

    private UIElement BuildMeaningPanel()
    {
        var panel = new Border
        {
            Background = Brush("#34282C"),
            BorderBrush = Brush("#3E3035"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(14),
            Padding = new Thickness(16, 14, 16, 14),
            Margin = new Thickness(0, 0, 0, 16)
        };
        Grid.SetRow(panel, 2);

        panel.Child = new TextBlock
        {
            Text = _word.ShortMeaning ?? "",
            Foreground = Brushes.White,
            FontSize = 16,
            LineHeight = 24,
            TextWrapping = TextWrapping.Wrap,
            VerticalAlignment = VerticalAlignment.Top
        };

        return panel;
    }

    private UIElement BuildActions()
    {
        var grid = new Grid { Height = 46 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.18, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.18, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        Grid.SetRow(grid, 3);

        grid.Children.Add(BuildAction("Know", "\uE73E", "#FF7A5F", "#FF7A5F", ReviewAction.Known, 0, true));
        grid.Children.Add(BuildAction("Later", "\uE916", "#3A3033", "#4A3F43", ReviewAction.Later, 1));
        grid.Children.Add(BuildAction("Skip", "\uE711", "#3A3033", "#4A3F43", ReviewAction.Skipped, 2));
        grid.Children.Add(BuildDetailsAction());

        return grid;
    }

    private Border BuildAction(string label, string icon, string background, string stroke, ReviewAction action, int column, bool isPrimary = false)
    {
        var button = BuildActionShell(background, stroke, column);
        button.Child = BuildActionContent(label, icon, isPrimary);
        button.MouseLeftButtonUp += async (_, _) => await RecordAndCloseAsync(action);
        return button;
    }

    private Border BuildDetailsAction()
    {
        var button = BuildActionShell("#342C30", "#342C30", 3);
        button.Child = BuildActionContent("Details", "\uE946");
        button.MouseLeftButtonUp += (_, _) => App.MainWindow?.Activate();
        return button;
    }

    private Border BuildActionShell(string background, string stroke, int column)
    {
        var button = new Border
        {
            Height = 40,
            Margin = new Thickness(column == 0 ? 0 : 8, 0, 0, 0),
            Background = Brush(background),
            BorderBrush = Brush(stroke),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(7),
            Cursor = Cursors.Hand,
            SnapsToDevicePixels = true
        };
        Grid.SetColumn(button, column);
        return button;
    }

    private UIElement BuildActionContent(string label, string icon, bool isPrimary = false)
    {
        var foreground = isPrimary ? Brushes.White : Brush("#F4EEF0");
        var stack = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        stack.Children.Add(new TextBlock
        {
            Text = icon,
            FontFamily = new FontFamily("Segoe Fluent Icons"),
            FontSize = 14,
            Foreground = foreground,
            Margin = new Thickness(0, 0, 7, 0),
            VerticalAlignment = VerticalAlignment.Center
        });
        stack.Children.Add(new TextBlock
        {
            Text = label,
            Foreground = foreground,
            FontSize = 14,
            VerticalAlignment = VerticalAlignment.Center
        });
        return stack;
    }

    private Border BuildIconButton(string glyph, string accessibleName)
    {
        var button = new Border
        {
            Width = 50,
            Height = 50,
            Margin = new Thickness(16, 0, 0, 0),
            Background = Brush("#3A3033"),
            CornerRadius = new CornerRadius(10),
            Cursor = Cursors.Hand,
            ToolTip = accessibleName,
            SnapsToDevicePixels = true
        };

        button.Child = new TextBlock
        {
            Text = glyph,
            FontFamily = new FontFamily("Segoe Fluent Icons"),
            FontSize = 19,
            Foreground = Brushes.White,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };

        return button;
    }

    private Border BuildFeedbackOverlay()
    {
        var overlay = new Border
        {
            Width = CardWidth,
            Height = CardHeight,
            Background = Brush("#CC2A2225"),
            CornerRadius = new CornerRadius(20),
            Visibility = Visibility.Collapsed
        };

        var pill = new Border
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Padding = new Thickness(20, 12, 20, 12),
            Background = Brush("#263D30"),
            BorderBrush = Brush("#62D187"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(16)
        };
        pill.Child = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Children =
            {
                new TextBlock
                {
                    Text = "\uE73E",
                    FontFamily = new FontFamily("Segoe Fluent Icons"),
                    FontSize = 17,
                    Foreground = Brushes.White,
                    Margin = new Thickness(0, 0, 10, 0),
                    VerticalAlignment = VerticalAlignment.Center
                },
                new TextBlock
                {
                    Name = "FeedbackText",
                    Text = "Saved",
                    FontSize = 18,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = Brushes.White,
                    VerticalAlignment = VerticalAlignment.Center
                }
            }
        };
        overlay.Child = pill;

        return overlay;
    }

    private Border BuildChip(string text, string background, Thickness? margin = null)
    {
        return new Border
        {
            Background = Brush(background),
            CornerRadius = new CornerRadius(12),
            Margin = margin ?? new Thickness(0),
            Padding = new Thickness(10, 5, 10, 5),
            Child = new TextBlock
            {
                Text = text,
                Foreground = Brushes.White,
                FontSize = 12,
                FontWeight = FontWeights.SemiBold
            }
        };
    }

    private async Task RecordAndCloseAsync(ReviewAction action)
    {
        if (_recorded)
        {
            return;
        }

        _recorded = true;
        _closeTimer.Stop();
        ShowFeedback(action);
        await Task.Delay(360);
        await _recordActionAsync(action);
        Close();
    }

    private void ShowFeedback(ReviewAction action)
    {
        if (_feedbackOverlay?.Child is not Border pill || pill.Child is not StackPanel stack)
        {
            return;
        }

        var label = stack.Children.OfType<TextBlock>().LastOrDefault();
        label!.Text = action switch
        {
            ReviewAction.Known => "Marked known",
            ReviewAction.Later => "Saved for later",
            _ => "Skipped"
        };

        _feedbackOverlay.Visibility = Visibility.Visible;
    }

    private void PositionWindow()
    {
        var workArea = SystemParameters.WorkArea;
        Left = workArea.Right - Width - 22;
        Top = workArea.Bottom - Height - 22;
    }

    private static SolidColorBrush Brush(string color)
    {
        return (SolidColorBrush)new BrushConverter().ConvertFromString(color)!;
    }

    private static string BuildSourceText(WordEntry word)
    {
        var chapter = word.Chapter is null ? "TOEFL" : $"Ch {word.Chapter}";
        var order = word.Order is null ? "" : $" - #{word.Order}";
        return $"{chapter}{order}";
    }
}
