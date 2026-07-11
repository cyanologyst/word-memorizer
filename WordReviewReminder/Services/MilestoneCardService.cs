using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using WordReviewReminder.Core;

namespace WordReviewReminder.Services;

public static class MilestoneCardService
{
    public static Task ExportAsync(AchievementSnapshot achievement, string destinationPath)
    {
        const int width = 1200;
        const int height = 630;
        var visual = new DrawingVisual();
        using (var drawing = visual.RenderOpen())
        {
            drawing.DrawRoundedRectangle(new SolidColorBrush(Color.FromRgb(23, 16, 20)), null, new Rect(0, 0, width, height), 28, 28);
            drawing.DrawRoundedRectangle(new SolidColorBrush(Color.FromRgb(42, 34, 37)), new Pen(new SolidColorBrush(Color.FromRgb(255, 122, 95)), 2), new Rect(42, 42, width - 84, height - 84), 20, 20);
            drawing.DrawRectangle(new SolidColorBrush(Color.FromRgb(255, 122, 95)), null, new Rect(42, 42, 9, height - 84));

            var badgePath = Path.Combine(AppContext.BaseDirectory, "Assets", "Achievements", achievement.Definition.IconFileName);
            if (File.Exists(badgePath))
            {
                var badge = new BitmapImage(new Uri(badgePath));
                drawing.DrawImage(badge, new Rect(84, 156, 270, 270));
            }

            DrawText(drawing, "WORD REVIEW REMINDER", 420, 105, 22, FontWeights.SemiBold, Color.FromRgb(242, 195, 107), 680);
            DrawText(drawing, achievement.Definition.Title, 420, 160, 54, FontWeights.SemiBold, Colors.White, 690);
            DrawText(drawing, achievement.Definition.Description, 420, 245, 25, FontWeights.Normal, Color.FromRgb(216, 203, 207), 690);
            DrawText(drawing, achievement.IsUnlocked ? "ACHIEVEMENT UNLOCKED" : achievement.ProgressLabel, 420, 400, 22, FontWeights.SemiBold, achievement.IsUnlocked ? Color.FromRgb(98, 209, 135) : Color.FromRgb(255, 122, 95), 690);
            var date = achievement.UnlockedAt?.ToLocalTime().ToString("MMMM d, yyyy") ?? $"{achievement.ProgressPercent:N0}% complete";
            DrawText(drawing, date, 420, 454, 20, FontWeights.Normal, Color.FromRgb(174, 162, 166), 690);
        }

        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(destinationPath);
        encoder.Save(stream);
        return Task.CompletedTask;
    }

    private static void DrawText(DrawingContext drawing, string text, double x, double y, double size, FontWeight weight, Color color, double maxWidth)
    {
        var formatted = new FormattedText(
            text,
            CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            new Typeface(new FontFamily("Segoe UI Variable Display"), FontStyles.Normal, weight, FontStretches.Normal),
            size,
            new SolidColorBrush(color),
            1.0)
        {
            MaxTextWidth = maxWidth,
            MaxTextHeight = 130,
            Trimming = TextTrimming.WordEllipsis
        };
        drawing.DrawText(formatted, new Point(x, y));
    }
}
