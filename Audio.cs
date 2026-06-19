using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows;
using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;

namespace SoundManager;

public record AudioDevice(string Id, string Name, bool IsDefault, bool IsDefaultComms);

public class DeviceVM
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public DataFlow Flow { get; set; }
    public bool IsDefault { get; set; }
    public bool IsDefaultComms { get; set; }
    public bool IsActive => IsDefault || IsDefaultComms;
    public Visibility DefaultVis => IsDefault ? Visibility.Visible : Visibility.Collapsed;
    public Visibility CommsVis => IsDefaultComms ? Visibility.Visible : Visibility.Collapsed;
}

public static class AudioManager
{
    public static List<AudioDevice> GetDevices(DataFlow flow)
    {
        var list = new List<AudioDevice>();
        using var en = new MMDeviceEnumerator();
        string defId = "", commId = "";
        try { using var d = en.GetDefaultAudioEndpoint(flow, Role.Multimedia); defId = d.ID; } catch { }
        try { using var d = en.GetDefaultAudioEndpoint(flow, Role.Communications); commId = d.ID; } catch { }

        var col = en.EnumerateAudioEndPoints(flow, DeviceState.Active);
        foreach (var d in col)
        {
            list.Add(new AudioDevice(d.ID, d.FriendlyName, d.ID == defId, d.ID == commId));
            d.Dispose();
        }
        return list;
    }

    public static bool DeviceExists(string id)
    {
        if (string.IsNullOrEmpty(id)) return false;
        try
        {
            using var en = new MMDeviceEnumerator();
            var col = en.EnumerateAudioEndPoints(DataFlow.All, DeviceState.Active);
            foreach (var d in col)
            {
                bool match = d.ID == id;
                d.Dispose();
                if (match) return true;
            }
        }
        catch { }
        return false;
    }

    public static string? NameById(string id)
    {
        if (string.IsNullOrEmpty(id)) return null;
        string? result = null;
        try
        {
            using var en = new MMDeviceEnumerator();
            var col = en.EnumerateAudioEndPoints(DataFlow.All, DeviceState.All);
            foreach (var d in col)
            {
                if (d.ID == id) result = d.FriendlyName;
                d.Dispose();
            }
        }
        catch { }
        return result;
    }

    public static void SetDefault(string deviceId)
    {
        var cfg = (IPolicyConfig)new CPolicyConfigClient();
        try
        {
            cfg.SetDefaultEndpoint(deviceId, ERole.eConsole);
            cfg.SetDefaultEndpoint(deviceId, ERole.eMultimedia);
        }
        finally { Marshal.FinalReleaseComObject(cfg); }
    }

    public static void SetDefaultComms(string deviceId)
    {
        var cfg = (IPolicyConfig)new CPolicyConfigClient();
        try { cfg.SetDefaultEndpoint(deviceId, ERole.eCommunications); }
        finally { Marshal.FinalReleaseComObject(cfg); }
    }
}

public class DeviceWatcher : IMMNotificationClient
{
    public Action? Changed;
    public Action<DataFlow, Role, string>? DefaultChanged;

    public void OnDeviceStateChanged(string deviceId, DeviceState newState) => Changed?.Invoke();
    public void OnDeviceAdded(string pwstrDeviceId) => Changed?.Invoke();
    public void OnDeviceRemoved(string deviceId) => Changed?.Invoke();
    public void OnDefaultDeviceChanged(DataFlow flow, Role role, string defaultDeviceId)
        => DefaultChanged?.Invoke(flow, role, defaultDeviceId);
    public void OnPropertyValueChanged(string pwstrDeviceId, PropertyKey key) { }
}

enum ERole { eConsole = 0, eMultimedia = 1, eCommunications = 2 }

[Guid("f8679f50-850a-41cf-9c72-430f290290c8")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface IPolicyConfig
{
    [PreserveSig] int GetMixFormat(string n, nint f);
    [PreserveSig] int GetDeviceFormat(string n, bool d, nint f);
    [PreserveSig] int ResetDeviceFormat(string n);
    [PreserveSig] int SetDeviceFormat(string n, nint e, nint m);
    [PreserveSig] int GetProcessingPeriod(string n, bool d, nint def, nint min);
    [PreserveSig] int SetProcessingPeriod(string n, nint p);
    [PreserveSig] int GetShareMode(string n, nint m);
    [PreserveSig] int SetShareMode(string n, nint m);
    [PreserveSig] int GetPropertyValue(string n, bool fx, nint key, nint pv);
    [PreserveSig] int SetPropertyValue(string n, bool fx, nint key, nint pv);
    [PreserveSig] int SetDefaultEndpoint(string n, ERole role);
    [PreserveSig] int SetEndpointVisibility(string n, bool visible);
}

[ComImport, Guid("870af99c-171d-4f9e-af0d-e63df40c2bc9")]
class CPolicyConfigClient { }