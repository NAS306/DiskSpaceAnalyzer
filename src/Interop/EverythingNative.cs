using System.Runtime.InteropServices;

namespace DiskSpaceAnalyzer.Interop;

internal static class EverythingNative
{
    private const string Dll = "Everything64.dll";
    [DllImport(Dll)] internal static extern void Everything_Reset();
    [DllImport(Dll, CharSet = CharSet.Unicode)] internal static extern void Everything_SetSearchW(string search);
    [DllImport(Dll)] internal static extern void Everything_SetRequestFlags(uint flags);
    [DllImport(Dll)] internal static extern void Everything_SetMax(uint max);
    [DllImport(Dll)] internal static extern void Everything_SetReplyWindow(nint window);
    [DllImport(Dll)] internal static extern void Everything_SetReplyID(uint id);
    [DllImport(Dll)] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool Everything_QueryW([MarshalAs(UnmanagedType.Bool)] bool wait);
    [DllImport(Dll)] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool Everything_IsQueryReply(uint message, nint wParam, nint lParam, uint id);
    [DllImport(Dll)] internal static extern uint Everything_GetLastError();
    [DllImport(Dll)] internal static extern uint Everything_GetNumResults();
    [DllImport(Dll)] internal static extern uint Everything_GetTotResults();
    [DllImport(Dll)] internal static extern nint Everything_GetResultPathW(uint index);
    [DllImport(Dll)] internal static extern nint Everything_GetResultFileNameW(uint index);
    [DllImport(Dll)] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool Everything_IsFolderResult(uint index);
    [DllImport(Dll)] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool Everything_GetResultSize(uint index, out long size);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] internal static extern nint FindWindowW(string className, string? windowName);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] internal static extern nint SendMessageTimeoutW(nint hwnd, uint message, nint wParam, nint lParam, uint flags, uint timeout, out nint result);
}
