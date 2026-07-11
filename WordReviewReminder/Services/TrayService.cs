using System.Drawing;

namespace WordReviewReminder.Services;

public sealed class TrayService : IDisposable
{
    private readonly System.Windows.Forms.NotifyIcon _icon;

    public TrayService(
        Action reviewNow,
        Action pause,
        Action openMiniWidget,
        Action backup,
        Action exit)
    {
        var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon.ico");
        _icon = new System.Windows.Forms.NotifyIcon
        {
            Icon = File.Exists(iconPath) ? new Icon(iconPath) : SystemIcons.Application,
            Text = "Word Review Reminder",
            Visible = true,
            ContextMenuStrip = new System.Windows.Forms.ContextMenuStrip()
        };

        AddItem("Review now", reviewNow, isDefault: true);
        AddItem("Pause for 30 minutes", pause);
        AddItem("Open mini widget", openMiniWidget);
        _icon.ContextMenuStrip.Items.Add(new System.Windows.Forms.ToolStripSeparator());
        AddItem("Create backup", backup);
        AddItem("Quit", exit);
        _icon.DoubleClick += (_, _) => reviewNow();
    }

    private void AddItem(string label, Action action, bool isDefault = false)
    {
        var item = new System.Windows.Forms.ToolStripMenuItem(label);
        item.Click += (_, _) => action();
        item.Font = new Font(item.Font, isDefault ? FontStyle.Bold : FontStyle.Regular);
        _icon.ContextMenuStrip!.Items.Add(item);
    }

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Dispose();
    }
}
