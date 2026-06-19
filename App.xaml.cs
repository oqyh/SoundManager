using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Windows;
using System.Windows.Media;

namespace SoundManager;

public partial class App : Application
{
    public static AppSettings Settings = null!;

    static Mutex? _mutex;
    static EventWaitHandle? _showEvent;
    const string MutexName = @"Local\SoundManager_GoldKingZ_SingleInstance";
    const string EventName = @"Local\SoundManager_GoldKingZ_ShowEvent";

    void OnStartup(object sender, StartupEventArgs e)
    {
        _mutex = new Mutex(true, MutexName, out bool isNew);
        if (!isNew)
        {
            try { EventWaitHandle.OpenExisting(EventName).Set(); } catch { }
            Shutdown();
            return;
        }

        _showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, EventName);

        Settings = AppSettings.Load();

        if (!Settings.FirstRunDone)
        {
            Settings.FirstRunDone = true;
            if (Settings.RunAtStartup)
            StartupManager.Set(true);
            Settings.Save();
        }

        ThemeManager.Apply(Settings.DarkMode);

        var win = new MainWindow();

        var waiter = new Thread(() =>
        {
            while (_showEvent.WaitOne())
                win.Dispatcher.BeginInvoke(new Action(win.RestoreFromTray));
        })
        { IsBackground = true };
        waiter.Start();

        bool launchedAtStartup = e.Args.Any(a => string.Equals(a, "--startup", StringComparison.OrdinalIgnoreCase));

        if (launchedAtStartup && Settings.AutoHideOnStartup)
            win.StartInTray();
        else
            win.Show();
    }
}

public class AppSettings
{
    public bool DarkMode { get; set; } = true; 
    public bool MinimizeToTray { get; set; } = true;
    public bool RunAtStartup { get; set; } = true;
    public bool AutoHideOnStartup { get; set; } = true;
    public bool FirstRunDone { get; set; } = false;
    public string PlaybackDefaultId { get; set; } = "";
    public string PlaybackCommsId { get; set; } = "";
    public string RecordingDefaultId { get; set; } = "";
    public string RecordingCommsId { get; set; } = "";
    static string Folder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SoundManager");
    static string FilePath => Path.Combine(Folder, "settings.json");

    public static AppSettings Load()
    {
        try { return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath)) ?? new(); }
        catch { return new(); }
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Folder);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { }
    }
}

public static class ThemeManager
{
    public static void Apply(bool dark)
    {
        var r = Application.Current.Resources;
        if (dark)
        {
            Set(r, "Bg", "#0F1115");          Set(r, "Surface", "#1A1D24");
            Set(r, "SurfaceHover", "#232732"); Set(r, "Text", "#E6E8EC");
            Set(r, "TextDim", "#8A8F98");      Set(r, "Border", "#2A2E38");
            Set(r, "Accent", "#4C8BF5");       Set(r, "ActiveBg", "#16263F");
            Set(r, "AccentComms", "#9D7BF0");
            Set(r, "ScrollThumb", "#3A3F4B");  Set(r, "ScrollThumbHover", "#565D6D");
        }
        else
        {
            Set(r, "Bg", "#F5F6F8");          Set(r, "Surface", "#FFFFFF");
            Set(r, "SurfaceHover", "#EDEFF2"); Set(r, "Text", "#1A1D24");
            Set(r, "TextDim", "#6B7280");      Set(r, "Border", "#E2E5EA");
            Set(r, "Accent", "#2563EB");       Set(r, "ActiveBg", "#DCE9FF");
            Set(r, "AccentComms", "#7C3AED");
            Set(r, "ScrollThumb", "#C7CCD4");  Set(r, "ScrollThumbHover", "#A9B0BC");
        }
    }

    static void Set(ResourceDictionary r, string key, string hex) => r[key] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
}