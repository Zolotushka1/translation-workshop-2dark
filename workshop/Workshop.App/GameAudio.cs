using System.Runtime.InteropServices;

namespace Workshop.App;

/// <summary>Mute only the two workshop processes; keep and restore each session's initial mute.</summary>
public sealed class GameAudio : IDisposable
{
    private readonly Dictionary<string, bool> original = [];
    public Dictionary<uint, bool> LastStates { get; } = [];
    private bool restoring;
    public void Apply(int? english, int? russian, bool single, bool useRussian, bool pausedEnglish = false, bool pausedRussian = false)
    {
        LastStates.Clear();
        var enumerator = (IDeviceEnumerator)new DeviceEnumerator();
        try
        {
            Marshal.ThrowExceptionForHR(enumerator.GetDefaultAudioEndpoint(0, 1, out var device));
            try
            {
                Guid iid = typeof(ISessionManager).GUID;
                Marshal.ThrowExceptionForHR(device.Activate(ref iid, 23, 0, out var instance));
                try
                {
                    var manager = (ISessionManager)instance;
                    Marshal.ThrowExceptionForHR(manager.GetSessionEnumerator(out var sessions));
                    try
                    {
                        Marshal.ThrowExceptionForHR(sessions.GetCount(out int count));
                        for (int i = 0; i < count; i++)
                        {
                            Marshal.ThrowExceptionForHR(sessions.GetSession(i, out var session));
                            try
                            {
                                Marshal.ThrowExceptionForHR(session.GetProcessId(out uint pid));
                                if (pid != english && pid != russian) continue;
                                Marshal.ThrowExceptionForHR(session.GetSessionInstanceIdentifier(out string id));
                                var volume = (ISimpleVolume)session;
                                Marshal.ThrowExceptionForHR(volume.GetMute(out bool muted));
                                original.TryAdd(id, muted);
                                bool pause = pid == english ? pausedEnglish : pausedRussian;
                                bool wanted = restoring ? original[id] : pause || single && (useRussian ? pid != russian : pid != english);
                                if (wanted != muted) { Guid context = Guid.Empty; Marshal.ThrowExceptionForHR(volume.SetMute(wanted, ref context)); }
                                Marshal.ThrowExceptionForHR(volume.GetMute(out bool actual));
                                if (actual != wanted) throw new COMException("Аудиосеанс не подтвердил изменение звука.");
                                LastStates[pid] = actual;
                                if (restoring) original.Remove(id);
                            }
                            finally { Marshal.ReleaseComObject(session); }
                        }
                    }
                    finally { Marshal.ReleaseComObject(sessions); }
                }
                finally { Marshal.ReleaseComObject(instance); }
            }
            finally { Marshal.ReleaseComObject(device); }
        }
        finally { Marshal.ReleaseComObject(enumerator); }
    }
    public void Restore(int? english, int? russian)
    {
        restoring = true; try { Apply(english, russian, false, false); } finally { restoring = false; }
    }
    public void Dispose() { original.Clear(); }

    [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")] private class DeviceEnumerator { }
    [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)] private interface IDeviceEnumerator
    {
        [PreserveSig] int EnumAudioEndpoints(int flow, uint mask, out nint devices);
        [PreserveSig] int GetDefaultAudioEndpoint(int flow, int role, out IDevice device);
    }
    [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)] private interface IDevice
    {
        [PreserveSig] int Activate(ref Guid iid, uint context, nint parameters, [MarshalAs(UnmanagedType.IUnknown)] out object instance);
    }
    [ComImport, Guid("77AA99A0-1BD6-484F-8BC7-2C654C9A9B6F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)] private interface ISessionManager
    {
        [PreserveSig] int GetAudioSessionControl(nint session, uint flags, out nint control);
        [PreserveSig] int GetSimpleAudioVolume(nint session, uint flags, out nint volume);
        [PreserveSig] int GetSessionEnumerator(out ISessionEnumerator sessions);
    }
    [ComImport, Guid("E2F5BB11-0570-40CA-ACDD-3AA01277DEE8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)] private interface ISessionEnumerator
    {
        [PreserveSig] int GetCount(out int count);
        [PreserveSig] int GetSession(int index, out ISessionControl session);
    }
    [ComImport, Guid("BFB7FF88-7239-4FC9-8FA2-07C950BE9C6D"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)] private interface ISessionControl
    {
        [PreserveSig] int GetState(out int state);
        [PreserveSig] int GetDisplayName([MarshalAs(UnmanagedType.LPWStr)] out string name);
        [PreserveSig] int SetDisplayName([MarshalAs(UnmanagedType.LPWStr)] string name, nint context);
        [PreserveSig] int GetIconPath([MarshalAs(UnmanagedType.LPWStr)] out string path);
        [PreserveSig] int SetIconPath([MarshalAs(UnmanagedType.LPWStr)] string path, nint context);
        [PreserveSig] int GetGroupingParam(out Guid grouping);
        [PreserveSig] int SetGroupingParam(ref Guid grouping, nint context);
        [PreserveSig] int RegisterAudioSessionNotification(nint notification);
        [PreserveSig] int UnregisterAudioSessionNotification(nint notification);
        [PreserveSig] int GetSessionIdentifier([MarshalAs(UnmanagedType.LPWStr)] out string id);
        [PreserveSig] int GetSessionInstanceIdentifier([MarshalAs(UnmanagedType.LPWStr)] out string id);
        [PreserveSig] int GetProcessId(out uint pid);
    }
    [ComImport, Guid("87CE5498-68D6-44E5-9215-6DA47EF883D8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)] private interface ISimpleVolume
    {
        [PreserveSig] int SetMasterVolume(float volume, ref Guid context);
        [PreserveSig] int GetMasterVolume(out float volume);
        [PreserveSig] int SetMute([MarshalAs(UnmanagedType.Bool)] bool muted, ref Guid context);
        [PreserveSig] int GetMute([MarshalAs(UnmanagedType.Bool)] out bool muted);
    }
}
