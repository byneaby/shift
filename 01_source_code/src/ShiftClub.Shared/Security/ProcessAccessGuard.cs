using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Principal;

namespace ShiftClub.Shared.Security;

/// <summary>
/// Senet-style: replace process DACL so End Task → Access Denied.
/// Important: do NOT merge with the default owner ALLOW (that still permits terminate).
/// SYSTEM keeps full access (watchdog / updates).
/// </summary>
public static class ProcessAccessGuard
{
    private const uint ProcessTerminate = 0x0001;
    private const uint ProcessCreateThread = 0x0002;
    private const uint ProcessVmOperation = 0x0008;
    private const uint ProcessVmWrite = 0x0020;
    private const uint ProcessVmRead = 0x0010;
    private const uint ProcessQueryInformation = 0x0400;
    private const uint ProcessQueryLimited = 0x1000;
    private const uint ProcessSuspendResume = 0x0800;
    private const uint ProcessSetInformation = 0x0200;
    private const uint Synchronize = 0x00100000;
    private const uint ProcessAllAccess = 0x001FFFFF;

    private const uint DeniedRights =
        ProcessTerminate
        | ProcessCreateThread
        | ProcessVmOperation
        | ProcessVmWrite
        | ProcessSuspendResume
        | ProcessSetInformation;

    /// <summary>Enough for Task Manager to list the process, not kill it.</summary>
    private const uint QueryRights =
        ProcessQueryLimited | ProcessQueryInformation | ProcessVmRead | Synchronize;

    private const uint WriteDac = 0x00040000;
    private const uint ReadControl = 0x00020000;

    public static bool ProtectCurrentProcess() => ProtectProcess(Environment.ProcessId);

    public static bool ProtectProcess(int processId)
    {
        try
        {
            return ReplaceDaclDenyTerminate(processId);
        }
        catch
        {
            return false;
        }
    }

    public static bool UnprotectProcess(int processId)
    {
        try
        {
            return ReplaceDaclAllowTrusted(processId);
        }
        catch
        {
            return false;
        }
    }

    public static bool UnprotectCurrentProcess() => UnprotectProcess(Environment.ProcessId);

    /// <summary>
    /// Soften DACL so a same-session Updater (non-admin) can WaitForExit / Kill the Shell.
    /// Admin/SYSTEM-only Unprotect caused Access Denied and aborted OTA mid-start.
    /// </summary>
    public static bool UnprotectForUpdate(int processId)
    {
        try
        {
            return ReplaceDaclAllowUpdater(processId);
        }
        catch
        {
            return false;
        }
    }

    public static bool UnprotectCurrentProcessForUpdate() => UnprotectForUpdate(Environment.ProcessId);

    private static bool ReplaceDaclDenyTerminate(int processId)
    {
        var handle = OpenProcess(WriteDac | ReadControl | ProcessQueryLimited, false, (uint)processId);
        if (handle == IntPtr.Zero)
            return false;

        var pins = new List<GCHandle>();
        try
        {
            var system = SidBytes(WellKnownSidType.LocalSystemSid, pins);
            var admins = SidBytes(WellKnownSidType.BuiltinAdministratorsSid, pins);
            var everyone = SidBytes(WellKnownSidType.WorldSid, pins);
            var users = SidBytes(WellKnownSidType.BuiltinUsersSid, pins);
            var interactive = SidBytes(WellKnownSidType.InteractiveSid, pins);
            var authUsers = SidBytes(WellKnownSidType.AuthenticatedUserSid, pins);

            SecurityIdentifier? ownerSid = null;
            try { ownerSid = WindowsIdentity.GetCurrent().User; } catch { /* ignore */ }

            var entries = new List<EXPLICIT_ACCESS>
            {
                // Deny first (SetEntriesInAcl puts denies appropriately when building fresh ACL)
                Deny(everyone, DeniedRights),
                Deny(users, DeniedRights),
                Deny(interactive, DeniedRights),
                Deny(authUsers, DeniedRights),
                Grant(system, ProcessAllAccess),
                // Admins: query only — Task Manager as admin still gets Access Denied on End Task
                // (SeDebugPrivilege can bypass; typical club guest session does not use it for End Task)
                Grant(admins, QueryRights),
                Grant(everyone, QueryRights)
            };

            if (ownerSid is not null)
            {
                var owner = new byte[ownerSid.BinaryLength];
                ownerSid.GetBinaryForm(owner, 0);
                var gch = GCHandle.Alloc(owner, GCHandleType.Pinned);
                pins.Add(gch);
                entries.Insert(0, Deny(gch.AddrOfPinnedObject(), DeniedRights));
                entries.Add(Grant(gch.AddrOfPinnedObject(), QueryRights));
            }

            var arr = entries.ToArray();
            // oldAcl = NULL → brand-new DACL (do not keep owner ALLOW ALL)
            var err = SetEntriesInAcl((uint)arr.Length, arr, IntPtr.Zero, out var newDacl);
            if (err != 0 || newDacl == IntPtr.Zero)
                return false;

            try
            {
                err = SetSecurityInfo(
                    handle,
                    SE_OBJECT_TYPE.SE_KERNEL_OBJECT,
                    SECURITY_INFORMATION.DACL_SECURITY_INFORMATION,
                    IntPtr.Zero,
                    IntPtr.Zero,
                    newDacl,
                    IntPtr.Zero);
                return err == 0;
            }
            finally
            {
                LocalFree(newDacl);
            }
        }
        finally
        {
            foreach (var p in pins)
            {
                if (p.IsAllocated) p.Free();
            }
            CloseHandle(handle);
        }
    }

    private static bool ReplaceDaclAllowTrusted(int processId)
    {
        var handle = OpenProcess(WriteDac | ReadControl | ProcessQueryLimited, false, (uint)processId);
        if (handle == IntPtr.Zero)
            return false;

        var pins = new List<GCHandle>();
        try
        {
            var system = SidBytes(WellKnownSidType.LocalSystemSid, pins);
            var admins = SidBytes(WellKnownSidType.BuiltinAdministratorsSid, pins);
            var entries = new[]
            {
                Grant(system, ProcessAllAccess),
                Grant(admins, ProcessAllAccess)
            };

            var err = SetEntriesInAcl((uint)entries.Length, entries, IntPtr.Zero, out var newDacl);
            if (err != 0 || newDacl == IntPtr.Zero)
                return false;
            try
            {
                err = SetSecurityInfo(
                    handle,
                    SE_OBJECT_TYPE.SE_KERNEL_OBJECT,
                    SECURITY_INFORMATION.DACL_SECURITY_INFORMATION,
                    IntPtr.Zero,
                    IntPtr.Zero,
                    newDacl,
                    IntPtr.Zero);
                return err == 0;
            }
            finally
            {
                LocalFree(newDacl);
            }
        }
        finally
        {
            foreach (var p in pins)
            {
                if (p.IsAllocated) p.Free();
            }
            CloseHandle(handle);
        }
    }

    private static bool ReplaceDaclAllowUpdater(int processId)
    {
        var handle = OpenProcess(WriteDac | ReadControl | ProcessQueryLimited, false, (uint)processId);
        if (handle == IntPtr.Zero)
            return false;

        var pins = new List<GCHandle>();
        try
        {
            var system = SidBytes(WellKnownSidType.LocalSystemSid, pins);
            var admins = SidBytes(WellKnownSidType.BuiltinAdministratorsSid, pins);
            var interactive = SidBytes(WellKnownSidType.InteractiveSid, pins);
            var users = SidBytes(WellKnownSidType.BuiltinUsersSid, pins);

            // TERMINATE | SYNCHRONIZE | QUERY — enough for WaitForExit / Kill without full ACL wipe.
            const uint updaterRights =
                ProcessTerminate | Synchronize | ProcessQueryInformation | ProcessQueryLimited | ProcessAllAccess;

            var entries = new List<EXPLICIT_ACCESS>
            {
                Grant(system, ProcessAllAccess),
                Grant(admins, ProcessAllAccess),
                Grant(interactive, updaterRights),
                Grant(users, updaterRights)
            };

            try
            {
                var ownerSid = WindowsIdentity.GetCurrent().User;
                if (ownerSid is not null)
                {
                    var owner = new byte[ownerSid.BinaryLength];
                    ownerSid.GetBinaryForm(owner, 0);
                    var gch = GCHandle.Alloc(owner, GCHandleType.Pinned);
                    pins.Add(gch);
                    entries.Add(Grant(gch.AddrOfPinnedObject(), ProcessAllAccess));
                }
            }
            catch { /* ignore */ }

            var arr = entries.ToArray();
            var err = SetEntriesInAcl((uint)arr.Length, arr, IntPtr.Zero, out var newDacl);
            if (err != 0 || newDacl == IntPtr.Zero)
                return false;
            try
            {
                err = SetSecurityInfo(
                    handle,
                    SE_OBJECT_TYPE.SE_KERNEL_OBJECT,
                    SECURITY_INFORMATION.DACL_SECURITY_INFORMATION,
                    IntPtr.Zero,
                    IntPtr.Zero,
                    newDacl,
                    IntPtr.Zero);
                return err == 0;
            }
            finally
            {
                LocalFree(newDacl);
            }
        }
        finally
        {
            foreach (var p in pins)
            {
                if (p.IsAllocated) p.Free();
            }
            CloseHandle(handle);
        }
    }

    private static IntPtr SidBytes(WellKnownSidType type, List<GCHandle> pins)
    {
        var sid = new SecurityIdentifier(type, null);
        var bytes = new byte[sid.BinaryLength];
        sid.GetBinaryForm(bytes, 0);
        var gch = GCHandle.Alloc(bytes, GCHandleType.Pinned);
        pins.Add(gch);
        return gch.AddrOfPinnedObject();
    }

    private static EXPLICIT_ACCESS Deny(IntPtr sid, uint rights) => new()
    {
        grfAccessPermissions = rights,
        grfAccessMode = ACCESS_MODE.DENY_ACCESS,
        grfInheritance = 0,
        Trustee = new TRUSTEE
        {
            TrusteeForm = TRUSTEE_FORM.TRUSTEE_IS_SID,
            TrusteeType = TRUSTEE_TYPE.TRUSTEE_IS_UNKNOWN,
            ptstrName = sid
        }
    };

    private static EXPLICIT_ACCESS Grant(IntPtr sid, uint rights) => new()
    {
        grfAccessPermissions = rights,
        grfAccessMode = ACCESS_MODE.GRANT_ACCESS,
        grfInheritance = 0,
        Trustee = new TRUSTEE
        {
            TrusteeForm = TRUSTEE_FORM.TRUSTEE_IS_SID,
            TrusteeType = TRUSTEE_TYPE.TRUSTEE_IS_UNKNOWN,
            ptstrName = sid
        }
    };

    private enum SE_OBJECT_TYPE { SE_KERNEL_OBJECT = 6 }

    [Flags]
    private enum SECURITY_INFORMATION : uint { DACL_SECURITY_INFORMATION = 0x00000004 }

    private enum ACCESS_MODE { GRANT_ACCESS = 1, DENY_ACCESS = 3 }
    private enum TRUSTEE_FORM { TRUSTEE_IS_SID = 0 }
    private enum TRUSTEE_TYPE { TRUSTEE_IS_UNKNOWN = 0 }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct TRUSTEE
    {
        public IntPtr pMultipleTrustee;
        public int MultipleTrusteeOperation;
        public TRUSTEE_FORM TrusteeForm;
        public TRUSTEE_TYPE TrusteeType;
        public IntPtr ptstrName;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct EXPLICIT_ACCESS
    {
        public uint grfAccessPermissions;
        public ACCESS_MODE grfAccessMode;
        public uint grfInheritance;
        public TRUSTEE Trustee;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint processAccess, bool bInheritHandle, uint processId);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr hObject);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr LocalFree(IntPtr hMem);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern uint SetSecurityInfo(
        IntPtr handle,
        SE_OBJECT_TYPE objectType,
        SECURITY_INFORMATION securityInfo,
        IntPtr psidOwner,
        IntPtr psidGroup,
        IntPtr pDacl,
        IntPtr pSacl);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint SetEntriesInAcl(
        uint cCountOfExplicitEntries,
        [In] EXPLICIT_ACCESS[] pListOfExplicitEntries,
        IntPtr oldAcl,
        out IntPtr newAcl);
}
