using System;
using System.Runtime.InteropServices;
using UnityEngine;

public static class XRInputGuard
{
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN || UNITY_STANDALONE_LINUX || UNITY_EDITOR_LINUX
    [DllImport("XRInputGuard")]
    private static extern int XRGuard_GetCatchCount(int site);
    [DllImport("XRInputGuard")]
    private static extern int XRGuard_IsEnabled();
    [DllImport("XRInputGuard")]
    private static extern ulong XRGuard_PlayerBase();
    [DllImport("XRInputGuard")]
    private static extern IntPtr XRGuard_StatusString();

    private static string NativeStatusString()
    {
        // Keep IntPtr + PtrToStringAnsi: a string return would make the marshaler free our static native buffer and corrupt the heap.
        try { return Marshal.PtrToStringAnsi(XRGuard_StatusString()) ?? "?"; }
        catch { return "?"; }
    }

    public static string Status()
    {
        try
        {
            return string.Format("active={0} base=0x{1:X} catches=[{2},{3},{4}] {5}",
                XRGuard_IsEnabled(), XRGuard_PlayerBase(),
                XRGuard_GetCatchCount(0), XRGuard_GetCatchCount(1), XRGuard_GetCatchCount(2),
                NativeStatusString());
        }
        catch (Exception e) { return "XRInputGuard missing: " + e.GetType().Name; }
    }
#else
    public static bool Available { get { return false; } }
    public static string Status() { return "XRInputGuard n/a on this platform"; }
#endif

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Report()
    {
        Debug.Log(Status());
    }
}
