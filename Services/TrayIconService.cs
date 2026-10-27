using System.Drawing;
using Forms = System.Windows.Forms;

namespace YueXinCat.Services;

/// <summary>
/// Small Windows tray shell kept separate from the WPF window. The menu actions
/// raise events so MainWindow remains the owner of salary and keyboard state.
/// </summary>
public sealed class TrayIconService : IDisposable
{
    private Forms.NotifyIcon? _notifyIcon;
    private Forms.ToolStripMenuItem? _keyboardItem;
    private Forms.ToolStripMenuItem? _pauseItem;
    private bool _disposed;

    public event EventHandler? OpenRequested;
    public event EventHandler? KeyboardToggleRequested;
    public event EventHandler? PauseToggleRequested;
    public event EventHandler? ExitRequested;

    public bool IsVisible => _notifyIcon?.Visible == true;

    public void Initialize(bool keyboardEnabled, bool paused)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_notifyIcon is not null)
        {
            UpdateState(keyboardEnabled, paused);
            return;
        }

        var menu = new Forms.ContextMenuStrip();
        var open = new Forms.ToolStripMenuItem("打开月薪喵");
        open.Click += (_, _) => OpenRequested?.Invoke(this, EventArgs.Empty);

        _keyboardItem = new Forms.ToolStripMenuItem();
        _keyboardItem.Click += (_, _) => KeyboardToggleRequested?.Invoke(this, EventArgs.Empty);

        _pauseItem = new Forms.ToolStripMenuItem();
        _pauseItem.Click += (_, _) => PauseToggleRequested?.Invoke(this, EventArgs.Empty);

        var exit = new Forms.ToolStripMenuItem("退出月薪喵");
        exit.Click += (_, _) => ExitRequested?.Invoke(this, EventArgs.Empty);

        menu.Items.Add(open);
        menu.Items.Add(_keyboardItem);
        menu.Items.Add(_pauseItem);
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add(exit);

        _notifyIcon = new Forms.NotifyIcon
        {
            Icon = SystemIcons.Application,
            Text = "月薪喵",
            ContextMenuStrip = menu,
            Visible = true
        };
        _notifyIcon.DoubleClick += (_, _) => OpenRequested?.Invoke(this, EventArgs.Empty);
        UpdateState(keyboardEnabled, paused);
    }

    public void UpdateState(bool keyboardEnabled, bool paused)
    {
        if (_keyboardItem is not null)
            _keyboardItem.Text = keyboardEnabled ? "关闭键盘跟随" : "开启键盘跟随";
        if (_pauseItem is not null)
        {
            _pauseItem.Text = paused ? "继续计薪" : "暂停计薪";
            _pauseItem.Checked = paused;
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        if (_notifyIcon is not null)
        {
            _notifyIcon.Visible = false;
            _notifyIcon.ContextMenuStrip?.Dispose();
            _notifyIcon.Dispose();
            _notifyIcon = null;
        }
        GC.SuppressFinalize(this);
    }
}
