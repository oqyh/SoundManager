using System;
using Microsoft.Win32;

namespace SoundManager;

public static class StartupManager
{
    const string Key = @"Software\Microsoft\Windows\CurrentVersion\Run";
    const string Name = "SoundManager";

    static string ExePath => Environment.ProcessPath ?? "";

    public static bool IsEnabled()
    {
        using var k = Registry.CurrentUser.OpenSubKey(Key, false);
        return k?.GetValue(Name) != null;
    }

    public static void Set(bool enable)
    {
        using var k = Registry.CurrentUser.OpenSubKey(Key, true);
        if (k == null) return;
        if (enable) k.SetValue(Name, $"\"{ExePath}\" --startup");
        else k.DeleteValue(Name, false);
    }
}