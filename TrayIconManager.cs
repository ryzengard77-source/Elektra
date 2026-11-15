/*
 * Creator: Justin L. Ross | Farmington, Maine. | Remote Location: Iceland | Black Star Research Facility.
 */

using System;
using System.Drawing;
using System.Windows.Forms;
using System.Windows.Threading;
using NAudio.Wave;
using Elektra.Audio;
using Elektra.UI.Assets;
using Elektra.UI.Interop;

namespace Elektra.UI;

public sealed class TrayIconManager : IDisposable
{
    private readonly NotifyIcon _notifyIcon;
    private readonly ContextMenuStrip _menu;
    private readonly MainWindow _mainWindow;
    private readonly AudioCaptureService _audioService;
    private readonly AudioTelemetryBridge _telemetry;
    private readonly Dispatcher _uiDispatcher;
    private readonly Action _onExitRequested;
    private readonly ToolStripMenuItem _micSubMenu;
    private readonly ToolStripMenuItem _startupItem;

    private Icon? _currentIcon;
    private PlasmaState _lastIconState;
    private bool _disposed;

    public TrayIconManager(
        MainWindow mainWindow,
        AudioCaptureService audioService,
        AudioTelemetryBridge telemetry,
        Dispatcher uiDispatcher,
        Action onExitRequested)
    {
        _mainWindow = mainWindow;
        _audioService = audioService;
        _telemetry = telemetry;
        _uiDispatcher = uiDispatcher;
        _onExitRequested = onExitRequested;

        _micSubMenu = new ToolStripMenuItem("Microphone Input");
        _startupItem = new ToolStripMenuItem("Start with Windows")
        {
            Checked = WindowsStartupManager.IsStartupEnabled()
        };
        _startupItem.Click += (_, _) =>
        {
            bool next = !_startupItem.Checked;
            WindowsStartupManager.SetStartupEnabled(next);
            _startupItem.Checked = WindowsStartupManager.IsStartupEnabled();
        };

        _menu = new ContextMenuStrip();
        _menu.Items.Add("Show / Hide Elektra (Win+Alt+E)", null, (_, _) =>
        {
            if (_mainWindow.IsVisible) _mainWindow.HideToTray();
            else _mainWindow.RestoreFromTray();
        });
        _menu.Items.Add(new ToolStripSeparator());
        _menu.Items.Add(_micSubMenu);
        _menu.Items.Add(_startupItem);
        _menu.Items.Add(new ToolStripSeparator());
        _menu.Items.Add("Exit", null, (_, _) => _onExitRequested());

        _menu.Opening += (_, _) =>
        {
            _startupItem.Checked = WindowsStartupManager.IsStartupEnabled();
            PopulateMicrophoneSubMenu();
        };

        PlasmaState init = _telemetry.CurrentState;
        _currentIcon = PlasmaIconGenerator.CreateTrayIcon(init);
        _lastIconState = init;

        _notifyIcon = new NotifyIcon
        {
            Text = FormatTooltip(init),
            Icon = _currentIcon,
            ContextMenuStrip = _menu,
            Visible = true
        };

        _notifyIcon.DoubleClick += (_, _) =>
        {
            if (_mainWindow.IsVisible) _mainWindow.HideToTray();
            else _mainWindow.RestoreFromTray();
        };

        _telemetry.StateChanged += OnTelemetryStateChanged;
    }

    private void OnTelemetryStateChanged(PlasmaState newState)
    {
        if (_disposed) return;
        if (_uiDispatcher.CheckAccess())
            ApplyTrayState(newState);
        else
            _uiDispatcher.BeginInvoke(DispatcherPriority.Background, new Action<PlasmaState>(ApplyTrayState), newState);
    }

    private void ApplyTrayState(PlasmaState _)
    {
        if (_disposed) return;
        PlasmaState latest = _telemetry.CurrentState;
        if (latest == _lastIconState) return;

        var nextIcon = PlasmaIconGenerator.CreateTrayIcon(latest);
        var oldIcon = _currentIcon;

        _notifyIcon.Icon = nextIcon;
        _notifyIcon.Text = FormatTooltip(latest);

        _currentIcon = nextIcon;
        _lastIconState = latest;
        oldIcon?.Dispose();
    }

    private static string FormatTooltip(PlasmaState s) => s switch
    {
        PlasmaState.Initializing => "Elektra — Loading speech model...",
        PlasmaState.Listening    => "Elektra — Listening...",
        PlasmaState.Thinking     => "Elektra — Searching...",
        PlasmaState.Speaking     => "Elektra — Speaking...",
        PlasmaState.Warning      => "Elektra — Security warning",
        _                        => "Elektra — Ready (Say \"Elektra\")"
    };

    private void PopulateMicrophoneSubMenu()
    {
        _micSubMenu.DropDownItems.Clear();
        int count = WaveInEvent.DeviceCount;
        if (count == 0)
        {
            _micSubMenu.DropDownItems.Add(new ToolStripMenuItem("No microphones detected") { Enabled = false });
            return;
        }

        int active = _audioService.CurrentDeviceNumber;
        for (int i = 0; i < count; i++)
        {
            int idx = i;
            var caps = WaveInEvent.GetCapabilities(idx);
            var item = new ToolStripMenuItem($"{idx + 1}. {caps.ProductName.Trim()}") { Checked = idx == active };
            item.Click += (_, _) =>
            {
                if (idx != _audioService.CurrentDeviceNumber && _audioService.SwitchDevice(idx))
                {
                    _notifyIcon.ShowBalloonTip(2000, "Microphone Switched", $"Listening on: {caps.ProductName.Trim()}", ToolTipIcon.Info);
                }
            };
            _micSubMenu.DropDownItems.Add(item);
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _telemetry.StateChanged -= OnTelemetryStateChanged;
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
        _menu.Dispose();
        _currentIcon?.Dispose();
    }
}