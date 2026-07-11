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
    private const double WindowWidth = 510;
    private const double CardWidth = 494;
    private double CardHeight => _compact ? 238 : 278;
    private double WindowHeight => CardHeight + 16;

    private readonly Func<ReviewAction, Task> _recordActionAsync;
    private readonly SpeechService _speech = new();
    private readonly WordEntry _word;
    private readonly DispatcherTimer _closeTimer = new();
    private readonly int _durationSeconds;
    private readonly Func<TimeSpan, Task>? _snoozeAsync;
    private readonly bool _compact;
    private Border? _feedbackOverlay;
    private ScaleTransform? _timerScale;
    private Border? _card;
    private TranslateTransform? _cardTranslate;
    private bool _recorded;

    public ReminderOverlayWindow(
        WordEntry word,
        int durationSeconds,
        Func<ReviewAction, Task> recordActionAsync,
        Func<TimeSpan, Task>? snoozeAsync = null,
        bool compact = false)
    {
        _word = word;
        _recordActionAsync = recordActionAsync;
        _durationSeconds = Math.Max(5, durationSeconds);
        _snoozeAsync = snoozeAsync;
        _compact = compact;

        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        Title = "Word review card";
        SizeToContent = SizeToContent.Manual;
        Topmost = true;
        WindowStyle = WindowStyle.None;
        Width = WindowWidth;
        Height = WindowHeight;
        Content = BuildCard();
        KeyDown += Window_KeyDown;

        Loaded += (_, _) =>
        {
            PositionWindow();
            StartEntranceAnimation();
            StartTimerAnimation();
        };

        _closeTimer.Interval = TimeSpan.FromSeconds(_durationSeconds);
        _closeTimer.Tick += (_, _) =>
        {
            _closeTimer.Stop();
            Close();
        };
        _closeTimer.Start();
        Closed += async (_, _) => await App.Data.SavePopupPositionAsync(Left, Top);
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
            Width = WindowWidth,
            Height = WindowHeight,
            Margin = new Thickness(0)
        };

        _card = new Border
        {
            Width = CardWidth,
            Height = CardHeight,
            Margin = new Thickness(8),
            Background = Brush("#E82A2225"),
            BorderThickness = new Thickness(0),
            CornerRadius = new CornerRadius(14),
            SnapsToDevicePixels = true,
            Opacity = 0
        };
        _cardTranslate = new TranslateTransform(0, 10);
        _card.RenderTransform = _cardTranslate;
        root.Children.Add(_card);

        var body = new Grid
        {
            Margin = new Thickness(18, 13, 14, 13)
        };
        body.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        body.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        body.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        body.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        _card.Child = body;

        body.Children.Add(BuildTopStrip());
        body.Children.Add(BuildHeader());
        body.Children.Add(BuildMeaningPanel());
        body.Children.Add(BuildActions());

        _feedbackOverlay = BuildFeedbackOverlay();
        root.Children.Add(_feedbackOverlay);

        return root;
    }

    private void StartEntranceAnimation()
    {
        if (_card is null || _cardTranslate is null)
        {
            return;
        }

        _card.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(170))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        });
        _cardTranslate.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(10, 0, TimeSpan.FromMilliseconds(190))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        });
    }

    private UIElement BuildTopStrip()
    {
        var grid = new Grid { Height = 12 };
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
        var grid = new Grid { Margin = new Thickness(0, _compact ? 3 : 6, 0, _compact ? 4 : 7), Cursor = Cursors.SizeAll };
        grid.MouseLeftButtonDown += Header_MouseLeftButtonDown;
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetRow(grid, 1);

        var copy = new StackPanel { Orientation = Orientation.Vertical };
        var chips = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 6) };
        chips.Children.Add(BuildChip("TOEFL Review", "#5B3333"));
        chips.Children.Add(BuildChip(BuildSourceText(_word), "#3A3033", new Thickness(8, 0, 0, 0)));

        copy.Children.Add(chips);
        copy.Children.Add(new TextBlock
        {
            Text = _word.Term,
            Foreground = Brushes.White,
            FontFamily = new FontFamily("Segoe UI Variable Display, Segoe UI"),
            FontSize = _compact ? 26 : 31,
            FontWeight = FontWeights.SemiBold,
            LineHeight = 35,
            TextTrimming = TextTrimming.CharacterEllipsis
        });
        copy.Children.Add(new TextBlock
        {
            Text = $"{_word.PartOfSpeech}  {_word.Pronunciation}".Trim(),
            Foreground = Brush("#D8CBCF"),
            FontSize = 13,
            Margin = new Thickness(0, 4, 0, 0),
            TextTrimming = TextTrimming.CharacterEllipsis
        });
        grid.Children.Add(copy);

        var listenButton = BuildIconButton("\uE767", "Listen");
        listenButton.MouseLeftButtonUp += async (_, _) =>
        {
            await _speech.SpeakAsync(_word.Term);
            await App.Data.RecordPronunciationAsync(_word);
        };
        Grid.SetColumn(listenButton, 1);
        grid.Children.Add(listenButton);

        return grid;
    }

    private UIElement BuildMeaningPanel()
    {
        var panel = new Border
        {
            Background = Brush("#D934282C"),
            BorderThickness = new Thickness(0),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(14, 10, 14, 10),
            Margin = new Thickness(0, 0, 0, 9)
        };
        Grid.SetRow(panel, 2);

        var content = new StackPanel();
        content.Children.Add(new TextBlock
        {
            Text = _word.ShortMeaning ?? "",
            Foreground = Brushes.White,
            FontSize = 15,
            LineHeight = 20,
            TextWrapping = TextWrapping.Wrap,
            VerticalAlignment = VerticalAlignment.Top,
            MaxHeight = _compact ? 42 : 58,
            TextTrimming = TextTrimming.CharacterEllipsis
        });
        if (!_compact && _word.ExampleSentences.FirstOrDefault() is { } example)
        {
            content.Children.Add(new TextBlock
            {
                Text = $"Example: {example}",
                Foreground = Brush("#C7B9BD"),
                FontSize = 12,
                Margin = new Thickness(0, 5, 0, 0),
                TextWrapping = TextWrapping.Wrap,
                MaxHeight = 32,
                TextTrimming = TextTrimming.CharacterEllipsis
            });
        }

        panel.Child = content;

        return panel;
    }

    private UIElement BuildActions()
    {
        var grid = new Grid { Height = 38 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.35, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(0.9, GridUnitType.Star) });
        Grid.SetRow(grid, 3);

        grid.Children.Add(BuildAction("Know", "\uE73E", "#FF7A5F", "#FF7A5F", ReviewAction.Known, 0, true));
        grid.Children.Add(BuildAction("Later", "\uE916", "#3A3033", "#4A3F43", ReviewAction.Later, 1));
        grid.Children.Add(BuildAction("Skip", "\uE711", "#3A3033", "#4A3F43", ReviewAction.Skipped, 2));
        grid.Children.Add(BuildSnoozeAction());
        grid.Children.Add(BuildDetailsAction());

        return grid;
    }

    private Border BuildAction(string label, string icon, string background, string stroke, ReviewAction action, int column, bool isPrimary = false)
    {
        var button = BuildActionShell(background, stroke, column);
        System.Windows.Automation.AutomationProperties.SetName(button, label);
        button.Child = BuildActionContent(label, icon, isPrimary);
        button.MouseLeftButtonUp += async (_, _) => await RecordAndCloseAsync(action);
        return button;
    }

    private Border BuildDetailsAction()
    {
        var button = BuildActionShell("#342C30", "#342C30", 4);
        button.Child = BuildActionContent("Details", "\uE946");
        System.Windows.Automation.AutomationProperties.SetName(button, "Open word details");
        button.MouseLeftButtonUp += (_, _) =>
        {
            _closeTimer.Stop();
            App.MainWindow?.Activate();
            Close();
        };
        return button;
    }

    private Border BuildSnoozeAction()
    {
        var button = BuildActionShell("#342C30", "#342C30", 3);
        button.Child = BuildActionContent("Snooze", "\uE823");
        System.Windows.Automation.AutomationProperties.SetName(button, "Snooze for 15 minutes");
        button.MouseLeftButtonUp += async (_, _) => await SnoozeAndCloseAsync(TimeSpan.FromMinutes(15));
        var menu = new ContextMenu();
        foreach (var minutes in new[] { 5, 15, 30 })
        {
            var item = new MenuItem { Header = $"Snooze {minutes} minutes", Tag = minutes };
            item.Click += async (_, _) => await SnoozeAndCloseAsync(TimeSpan.FromMinutes((int)item.Tag));
            menu.Items.Add(item);
        }
        button.ContextMenu = menu;
        return button;
    }

    private Border BuildActionShell(string background, string stroke, int column)
    {
        var button = new Border
        {
            Height = 34,
            Margin = new Thickness(column == 0 ? 0 : 6, 0, 0, 0),
            Background = Brush(background),
            BorderBrush = Brush(stroke),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(5),
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
            FontSize = 13,
            Foreground = foreground,
            Margin = new Thickness(0, 0, 5, 0),
            VerticalAlignment = VerticalAlignment.Center
        });
        stack.Children.Add(new TextBlock
        {
            Text = label,
            Foreground = foreground,
            FontSize = 13,
            VerticalAlignment = VerticalAlignment.Center
        });
        return stack;
    }

    private Border BuildIconButton(string glyph, string accessibleName)
    {
        var button = new Border
        {
            Width = 40,
            Height = 40,
            Margin = new Thickness(12, 0, 0, 0),
            Background = Brush("#3A3033"),
            CornerRadius = new CornerRadius(6),
            Cursor = Cursors.Hand,
            ToolTip = accessibleName,
            SnapsToDevicePixels = true
        };
        System.Windows.Automation.AutomationProperties.SetName(button, accessibleName);

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
            Margin = new Thickness(8),
            Background = Brush("#CC2A2225"),
            CornerRadius = new CornerRadius(14),
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
            Padding = new Thickness(9, 4, 9, 4),
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

    private async Task SnoozeAndCloseAsync(TimeSpan duration)
    {
        if (_recorded)
        {
            return;
        }

        _recorded = true;
        _closeTimer.Stop();
        if (_snoozeAsync is not null)
        {
            await _snoozeAsync(duration);
        }
        else
        {
            await App.Data.SnoozeWordAsync(_word, duration);
        }

        Close();
    }

    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState != MouseButtonState.Pressed || IsInteractiveSource(e.OriginalSource as DependencyObject))
        {
            return;
        }

        DragMove();
    }

    private static bool IsInteractiveSource(DependencyObject? source)
    {
        for (var current = source; current is not null; current = VisualTreeHelper.GetParent(current))
        {
            if (current is Border border && border.Cursor == Cursors.Hand)
            {
                return true;
            }
        }

        return false;
    }

    private async void Window_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.D1:
            case Key.NumPad1:
                await RecordAndCloseAsync(ReviewAction.Known);
                break;
            case Key.D2:
            case Key.NumPad2:
                await RecordAndCloseAsync(ReviewAction.Later);
                break;
            case Key.D3:
            case Key.NumPad3:
                await RecordAndCloseAsync(ReviewAction.Skipped);
                break;
            case Key.S:
                await SnoozeAndCloseAsync(TimeSpan.FromMinutes(15));
                break;
            case Key.L:
                await _speech.SpeakAsync(_word.Term);
                await App.Data.RecordPronunciationAsync(_word);
                break;
            case Key.Escape:
                _closeTimer.Stop();
                Close();
                break;
        }
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
        var preferredLeft = App.Data.Settings.PopupLeft;
        var preferredTop = App.Data.Settings.PopupTop;
        Left = preferredLeft.HasValue
            ? Math.Clamp(preferredLeft.Value, workArea.Left, Math.Max(workArea.Left, workArea.Right - Width))
            : workArea.Right - Width - 22;
        Top = preferredTop.HasValue
            ? Math.Clamp(preferredTop.Value, workArea.Top, Math.Max(workArea.Top, workArea.Bottom - Height))
            : workArea.Bottom - Height - 22;
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
