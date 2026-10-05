using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace ShiftClub.Client.Service;

/// <summary>
/// Start a GUI process in the active user session (services run in Session 0).
/// </summary>
internal static class SessionProcessStarter
{
    public static bool TryStartInActiveSession(string exePath, string workDir)
    {
        try
        {
            var sessionId = WTSGetActiveConsoleSessionId();
            if (sessionId == 0xFFFFFFFF)
                return false;

            if (!WTSQueryUserToken(sessionId, out var userToken))
                return false;

            try
            {
                if (!DuplicateTokenEx(
                        userToken,
                        0x10000000, // MAXIMUM_ALLOWED
                        IntPtr.Zero,
                        SECURITY_IMPERSONATION_LEVEL.SecurityImpersonation,
                        TOKEN_TYPE.TokenPrimary,
                        out var primaryToken))
                    return false;

                try
                {
                    var si = new STARTUPINFO();
                    si.cb = Marshal.SizeOf<STARTUPINFO>();
                    si.lpDesktop = "winsta0\\default";

                    var cmd = new StringBuilder($"\"{exePath}\"");
                    var ok = CreateProcessAsUser(
                        primaryToken,
                        null,
                        cmd,
                        IntPtr.Zero,
                        IntPtr.Zero,
                        false,
                        0,
                        IntPtr.Zero,
                        workDir,
                        ref si,
                        out var pi);
                    if (!ok)
                        return false;

                    CloseHandle(pi.hThread);
                    CloseHandle(pi.hProcess);
                    return true;
                }
                finally
                {
                    CloseHandle(primaryToken);
                }
            }
            finally
            {
                CloseHandle(userToken);
            }
        }
        catch
        {
            return false;
        }
    }

    private enum SECURITY_IMPERSONATION_LEVEL
    {
        SecurityImpersonation = 2
    }

    private enum TOKEN_TYPE
    {
        TokenPrimary = 1
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct STARTUPINFO
    {
        public int cb;
        public string? lpReserved;
        public string? lpDesktop;
        public string? lpTitle;
        public int dwX, dwY, dwXSize, dwYSize, dwXCountChars, dwYCountChars, dwFillAttribute, dwFlags;
        public short wShowWindow, cbReserved2;
        public IntPtr lpReserved2, hStdInput, hStdOutput, hStdError;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PROCESS_INFORMATION
    {
        public IntPtr hProcess, hThread;
        public int dwProcessId, dwThreadId;
    }

    [DllImport("kernel32.dll")]
    private static extern uint WTSGetActiveConsoleSessionId();

    [DllImport("wtsapi32.dll", SetLastError = true)]
    private static extern bool WTSQueryUserToken(uint sessionId, out IntPtr phToken);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool DuplicateTokenEx(
        IntPtr hExistingToken,
        uint dwDesiredAccess,
        IntPtr lpTokenAttributes,
        SECURITY_IMPERSONATION_LEVEL impersonationLevel,
        TOKEN_TYPE tokenType,
        out IntPtr phNewToken);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CreateProcessAsUser(
        IntPtr hToken,
        string? lpApplicationName,
        StringBuilder lpCommandLine,
        IntPtr lpProcessAttributes,
        IntPtr lpThreadAttributes,
        bool bInheritHandles,
        uint dwCreationFlags,
        IntPtr lpEnvironment,
        string? lpCurrentDirectory,
        ref STARTUPINFO lpStartupInfo,
        out PROCESS_INFORMATION lpProcessInformation);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr hObject);
}
