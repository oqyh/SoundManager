using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using NAudio.CoreAudioApi;
using Drawing = System.Drawing;
using WinForms = System.Windows.Forms;

namespace SoundManager;

public partial class MainWindow : Window
{
    WinForms.NotifyIcon _tray = null!;
    bool _exit = false;
    bool _balloonShown = false;

    Drawing.Icon? _icoSmall;
    Drawing.Icon? _icoBig;

    MMDeviceEnumerator? _enum;
    DeviceWatcher? _watcher;
    bool _enforcing = false;

    System.Windows.Threading.DispatcherTimer? _debounce;

    static readonly System.Windows.Media.SolidColorBrush MissingBrush = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#E5484D"));

    public MainWindow()
    {
        InitializeComponent();
        SetWindowIcon();
        SetupTray();
        SetupDeviceWatcher();
        ApplySavedDefaults();
        SourceInitialized += (_, _) => { ApplyTitleBar(App.Settings.DarkMode); ApplyWindowIcons(); };
        Loaded += (_, _) => Refresh();
    }

    public void StartInTray() => new WindowInteropHelper(this).EnsureHandle();

    void SetWindowIcon()
    {
        try
        {
            var info = Application.GetResourceStream(new Uri("pack://application:,,,/icon.ico", UriKind.Absolute));
            if (info != null)
                Icon = BitmapFrame.Create(info.Stream, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
        }
        catch { }
    }

    static void OpenSoundPanel()
    {
        try { Process.Start(new ProcessStartInfo("control.exe", "mmsys.cpl") { UseShellExecute = true }); }
        catch { }
    }

    static void OpenVolumeMixer()
    {
        try { Process.Start(new ProcessStartInfo("ms-settings:apps-volume") { UseShellExecute = true }); }
        catch { }
    }

    void OpenSoundPanel_Click(object sender, RoutedEventArgs e) => OpenSoundPanel();
    void OpenVolumeMixer_Click(object sender, RoutedEventArgs e) => OpenVolumeMixer();

    void Refresh()
    {
        var play = Map(DataFlow.Render);
        var rec  = Map(DataFlow.Capture);

        PlaybackList.ItemsSource  = play;
        RecordingList.ItemsSource = rec;

        SetStatus(PlaybackDefaultName,  play, d => d.IsDefault,      App.Settings.PlaybackDefaultId);
        SetStatus(PlaybackCommsName,    play, d => d.IsDefaultComms, App.Settings.PlaybackCommsId);
        SetStatus(RecordingDefaultName, rec,  d => d.IsDefault,      App.Settings.RecordingDefaultId);
        SetStatus(RecordingCommsName,   rec,  d => d.IsDefaultComms, App.Settings.RecordingCommsId);
    }

    void SetStatus(TextBlock label, List<DeviceVM> liveList, Func<DeviceVM, bool> pick, string savedId)
    {
        if (string.IsNullOrEmpty(savedId))
        {
            label.Text = "Not set";
            label.SetResourceReference(TextBlock.ForegroundProperty, "TextDim");
            return;
        }

        if (!AudioManager.DeviceExists(savedId))
        {
            string savedName = AudioManager.NameById(savedId) ?? "Saved device";
            label.Text = savedName + "  (not found)";
            label.Foreground = MissingBrush;
            return;
        }

        var active = liveList.FirstOrDefault(pick);
        label.Text = active?.Name ?? "Not set";
        label.SetResourceReference(TextBlock.ForegroundProperty, "Text");
    }

    void AutoRefresh()
    {
        ApplySavedDefaults();
        if (IsVisible) Refresh();
    }

    void RefreshIfVisible() { if (IsVisible) Refresh(); }

    static List<DeviceVM> Map(DataFlow flow)
    {
        var list = new List<DeviceVM>();
        foreach (var d in AudioManager.GetDevices(flow))
            list.Add(new DeviceVM { Id = d.Id, Name = d.Name, Flow = flow, IsDefault = d.IsDefault, IsDefaultComms = d.IsDefaultComms });
        return list.OrderBy(d => d.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    void RememberDefault(DeviceVM vm)
    {
        if (vm.Flow == DataFlow.Render) App.Settings.PlaybackDefaultId = vm.Id;
        else App.Settings.RecordingDefaultId = vm.Id;
        App.Settings.Save();
    }

    void RememberComms(DeviceVM vm)
    {
        if (vm.Flow == DataFlow.Render) App.Settings.PlaybackCommsId = vm.Id;
        else App.Settings.RecordingCommsId = vm.Id;
        App.Settings.Save();
    }

    void ApplyUserChoice(DeviceVM vm, bool setDefault, bool setComms)
    {
        _enforcing = true;
        try
        {
            if (setDefault) { RememberDefault(vm); try { AudioManager.SetDefault(vm.Id); } catch { } }
            if (setComms)   { RememberComms(vm);   try { AudioManager.SetDefaultComms(vm.Id); } catch { } }
        }
        finally { _enforcing = false; }
        Refresh();
    }

    void Device_Click(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).DataContext is DeviceVM vm)
            ApplyUserChoice(vm, setDefault: true, setComms: false);
    }

    static DeviceVM? FromMenu(object sender)
        => sender is MenuItem mi && mi.Parent is ContextMenu cm &&
           cm.PlacementTarget is FrameworkElement fe && fe.DataContext is DeviceVM vm ? vm : null;

    void MenuDefault_Click(object sender, RoutedEventArgs e)
    {
        if (FromMenu(sender) is { } vm) ApplyUserChoice(vm, true, false);
    }

    void MenuComms_Click(object sender, RoutedEventArgs e)
    {
        if (FromMenu(sender) is { } vm) ApplyUserChoice(vm, false, true);
    }

    void MenuBoth_Click(object sender, RoutedEventArgs e)
    {
        if (FromMenu(sender) is { } vm) ApplyUserChoice(vm, true, true);
    }

    void SetupDeviceWatcher()
    {
        _debounce = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(400)
        };
        _debounce.Tick += (_, _) =>
        {
            _debounce!.Stop();
            if (!_enforcing) AutoRefresh();
            if (!IsVisible) TrimMemory();
        };

        try
        {
            _enum = new MMDeviceEnumerator();
            _watcher = new DeviceWatcher
            {
                DefaultChanged = (flow, role, id) =>
                {
                    if (_enforcing) return;
                    Dispatcher.BeginInvoke(new Action(() => EnforceDefault(flow, role, id)));
                },
                Changed = () =>
                {
                    if (_enforcing) return;
                    Dispatcher.BeginInvoke(new Action(() => { _debounce!.Stop(); _debounce!.Start(); }));
                }
            };
            _enum.RegisterEndpointNotificationCallback(_watcher);
        }
        catch { }
    }

    string TargetFor(DataFlow flow, Role role)
    {
        if (flow == DataFlow.Render)
            return role == Role.Communications ? App.Settings.PlaybackCommsId : App.Settings.PlaybackDefaultId;
        if (flow == DataFlow.Capture)
            return role == Role.Communications ? App.Settings.RecordingCommsId : App.Settings.RecordingDefaultId;
        return "";
    }

    void EnforceDefault(DataFlow flow, Role role, string id)
    {
        if (_enforcing) return;
        string target = TargetFor(flow, role);
        if (string.IsNullOrEmpty(target) || !AudioManager.DeviceExists(target)) { RefreshIfVisible(); return; }
        if (string.Equals(id, target, StringComparison.OrdinalIgnoreCase)) { RefreshIfVisible(); return; }

        _enforcing = true;
        try
        {
            for (int attempt = 0; attempt < 3; attempt++)
            {
                if (role == Role.Communications) AudioManager.SetDefaultComms(target);
                else AudioManager.SetDefault(target);

                string now = CurrentDefault(flow, role);
                if (string.Equals(now, target, StringComparison.OrdinalIgnoreCase)) break;
            }
        }
        catch { }
        _enforcing = false;
        RefreshIfVisible();
    }

    static string CurrentDefault(DataFlow flow, Role role)
    {
        try
        {
            using var en = new MMDeviceEnumerator();
            using var d = en.GetDefaultAudioEndpoint(flow, role);
            return d.ID;
        }
        catch { return ""; }
    }

    void ApplySavedDefaults()
    {
        _enforcing = true;
        try
        {
            Force(DataFlow.Render,  Role.Multimedia,     App.Settings.PlaybackDefaultId);
            Force(DataFlow.Render,  Role.Communications, App.Settings.PlaybackCommsId);
            Force(DataFlow.Capture, Role.Multimedia,     App.Settings.RecordingDefaultId);
            Force(DataFlow.Capture, Role.Communications, App.Settings.RecordingCommsId);
        }
        finally { _enforcing = false; }
        RefreshIfVisible();
    }

    static void Force(DataFlow flow, Role role, string id)
    {
        if (!AudioManager.DeviceExists(id)) return;
        if (string.Equals(CurrentDefault(flow, role), id, StringComparison.OrdinalIgnoreCase)) return;
        try
        {
            if (role == Role.Communications) AudioManager.SetDefaultComms(id);
            else AudioManager.SetDefault(id);
        }
        catch { }
    }

    void OpenSettings_Click(object sender, RoutedEventArgs e)
    {
        DarkToggle.IsChecked = App.Settings.DarkMode;
        TrayToggle.IsChecked = App.Settings.MinimizeToTray;

        bool startup = StartupManager.IsEnabled();
        App.Settings.RunAtStartup = startup;
        StartupToggle.IsChecked = startup;
        HideStartupToggle.IsChecked = App.Settings.AutoHideOnStartup;
        UpdateAutoHideEnabled(startup);

        SettingsOverlay.Visibility = Visibility.Visible;
    }

    void UpdateAutoHideEnabled(bool runAtStartup)
    {
        HideStartupToggle.IsEnabled = runAtStartup;
        AutoHideRow.Opacity = runAtStartup ? 1.0 : 0.4;
    }

    void CloseSettings_Click(object sender, RoutedEventArgs e) => SettingsOverlay.Visibility = Visibility.Collapsed;
    void Overlay_Click(object sender, MouseButtonEventArgs e) => SettingsOverlay.Visibility = Visibility.Collapsed;
    void Card_Click(object sender, MouseButtonEventArgs e) => e.Handled = true;

    void DarkToggle_Click(object sender, RoutedEventArgs e)
    {
        App.Settings.DarkMode = DarkToggle.IsChecked == true;
        ThemeManager.Apply(App.Settings.DarkMode);
        ApplyTitleBar(App.Settings.DarkMode);
        App.Settings.Save();
        Refresh();
    }

    void TrayToggle_Click(object sender, RoutedEventArgs e)
    {
        App.Settings.MinimizeToTray = TrayToggle.IsChecked == true;
        App.Settings.Save();
    }

    void StartupToggle_Click(object sender, RoutedEventArgs e)
    {
        bool enable = StartupToggle.IsChecked == true;
        StartupManager.Set(enable);
        App.Settings.RunAtStartup = enable;

        if (!enable)
        {
            App.Settings.AutoHideOnStartup = false;
            HideStartupToggle.IsChecked = false;
        }

        App.Settings.Save();
        UpdateAutoHideEnabled(enable);
    }

    void HideStartupToggle_Click(object sender, RoutedEventArgs e)
    {
        App.Settings.AutoHideOnStartup = HideStartupToggle.IsChecked == true;
        App.Settings.Save();
    }

    void ToggleTheme()
    {
        App.Settings.DarkMode = !App.Settings.DarkMode;
        ThemeManager.Apply(App.Settings.DarkMode);
        ApplyTitleBar(App.Settings.DarkMode);
        App.Settings.Save();
        if (SettingsOverlay.Visibility == Visibility.Visible)
            DarkToggle.IsChecked = App.Settings.DarkMode;
        if (IsVisible) Refresh();
    }

    void OpenAbout_Click(object sender, RoutedEventArgs e) => AboutOverlay.Visibility = Visibility.Visible;
    void CloseAbout_Click(object sender, RoutedEventArgs e) => AboutOverlay.Visibility = Visibility.Collapsed;
    void AboutOverlay_Click(object sender, MouseButtonEventArgs e) => AboutOverlay.Visibility = Visibility.Collapsed;

    void Discord_Click(object sender, RoutedEventArgs e) => OpenUrl("https://discord.com/invite/U7AuQhu");
    void Github_Click(object sender, RoutedEventArgs e) => OpenUrl("https://github.com/oqyh");
    void Kofi_Click(object sender, RoutedEventArgs e) => OpenUrl("https://ko-fi.com/goldkingz");

    static void OpenUrl(string url)
    {
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
        catch { }
    }

    void SetupTray()
    {
        _tray = new WinForms.NotifyIcon
        {
            Text = "SoundManager",
            Visible = true,
            Icon = LoadTrayIcon()
        };
        _tray.DoubleClick += (_, _) => ShowWindow();
        _tray.BalloonTipClicked += (_, _) => ShowWindow();

        var menu = new WinForms.ContextMenuStrip();
        menu.Items.Add("Open SoundManager", null, (_, _) => ShowWindow());
        menu.Items.Add(new WinForms.ToolStripSeparator());
        menu.Items.Add("Windows sound settings", null, (_, _) => OpenSoundPanel());
        menu.Items.Add("Volume mixer", null, (_, _) => OpenVolumeMixer());
        menu.Items.Add(new WinForms.ToolStripSeparator());
        menu.Items.Add("Toggle theme", null, (_, _) => ToggleTheme());
        menu.Items.Add(new WinForms.ToolStripSeparator());
        menu.Items.Add("Exit", null, (_, _) => ExitApp());
        _tray.ContextMenuStrip = menu;
    }

    static Drawing.Icon? IconFromResource(int size)
    {
        try
        {
            var info = Application.GetResourceStream(new Uri("pack://application:,,,/icon.ico", UriKind.Absolute));
            if (info != null) return new Drawing.Icon(info.Stream, new Drawing.Size(size, size));
        }
        catch { }
        return null;
    }

    static Drawing.Icon? IconFull()
    {
        try
        {
            var info = Application.GetResourceStream(new Uri("pack://application:,,,/icon.ico", UriKind.Absolute));
            if (info != null) return new Drawing.Icon(info.Stream);
        }
        catch { }
        return null;
    }

    static Drawing.Icon? IconFromExe()
    {
        try
        {
            var p = Environment.ProcessPath;
            if (p != null) return Drawing.Icon.ExtractAssociatedIcon(p);
        }
        catch { }
        return null;
    }

    static Drawing.Icon LoadTrayIcon() => IconFull() ?? IconFromExe() ?? Drawing.SystemIcons.Application;

    void ApplyWindowIcons()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == 0) return;

        _icoSmall = IconFromResource(16) ?? IconFromExe();
        _icoBig   = IconFromResource(32) ?? IconFromExe();

        if (_icoSmall != null) SendMessage(hwnd, WM_SETICON, ICON_SMALL, _icoSmall.Handle);
        if (_icoBig   != null) SendMessage(hwnd, WM_SETICON, ICON_BIG,   _icoBig.Handle);
    }

    public void RestoreFromTray() => ShowWindow();

    void ShowWindow()
    {
        Show();
        WindowState = WindowState.Normal;
        Activate();
        Topmost = true; Topmost = false;
        Refresh();
    }

    void ExitApp()
    {
        _exit = true;
        try { if (_enum != null && _watcher != null) _enum.UnregisterEndpointNotificationCallback(_watcher); } catch { }
        _enum?.Dispose();
        _tray.Visible = false;
        _tray.Dispose();
        Application.Current.Shutdown();
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (!_exit && App.Settings.MinimizeToTray)
        {
            e.Cancel = true;
            Hide();
            TrimMemory();
            if (!_balloonShown)
            {
                _balloonShown = true;
                _tray.ShowBalloonTip(2000, "SoundManager",
                    "Still running — find me here in the tray. Double-click to reopen.",
                    WinForms.ToolTipIcon.Info);
            }
        }
        else
        {
            try { if (_enum != null && _watcher != null) _enum.UnregisterEndpointNotificationCallback(_watcher); } catch { }
            _enum?.Dispose();
            _tray?.Dispose();
        }
        base.OnClosing(e);
    }

    static void TrimMemory()
    {
        try
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            EmptyWorkingSet(Process.GetCurrentProcess().Handle);
        }
        catch { }
    }

    const int WM_SETICON = 0x0080;
    const int ICON_SMALL = 0;
    const int ICON_BIG = 1;

    [DllImport("user32.dll")]
    static extern nint SendMessage(nint hwnd, int msg, nint wParam, nint lParam);

    [DllImport("dwmapi.dll")]
    static extern int DwmSetWindowAttribute(nint hwnd, int attr, ref int val, int size);

    [DllImport("psapi.dll")]
    static extern bool EmptyWorkingSet(nint hProcess);

    void ApplyTitleBar(bool dark)
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == 0) return;
        int v = dark ? 1 : 0;
        DwmSetWindowAttribute(hwnd, 20, ref v, 4);
    }
}