using System.Runtime.InteropServices;
using System.Security;
using System.Security.Principal;
using ThirdLife.Core.Handover;
using ThirdLife.Inventory.Windows;
using ThirdLife.UI.Shell;

namespace ThirdLife.UI.Features.Finalization;

internal enum ToggleKeysDispatchResult
{
    PreconditionsChanged = 1,
    WindowsDeclined,
    Dispatched,
}

internal interface IRecipientToggleKeys
{
    ToggleKeysObservation Read();
    ToggleKeysDispatchResult Dispatch(ToggleKeysApproval approval, SavedRecipientHandover current);
}

/// <summary>Changes only the Toggle Keys enabled bit for the verified current Windows user.</summary>
internal sealed class WindowsToggleKeys : IRecipientToggleKeys
{
    private const uint GetToggleKeys = 0x0034;
    private const uint SetToggleKeys = 0x0035;
    private const uint Enabled = 0x0001;
    private const uint Available = 0x0002;
    private const uint PersistToUserProfile = 0x0001;
    private const uint BroadcastSettingChange = 0x0002;
    private static readonly uint NativeSize = (uint)Marshal.SizeOf<NativeToggleKeys>();
    private readonly SecurityIdentifier? _registeredUser = CurrentUser();
    private readonly Guid _userBinding = Guid.NewGuid();

    public ToggleKeysObservation Read() => TryRead(out var native)
        ? new(_userBinding, (native.Flags & Enabled) != 0)
        : new(Guid.Empty, null);

    public ToggleKeysDispatchResult Dispatch(ToggleKeysApproval approval, SavedRecipientHandover current)
    {
        ArgumentNullException.ThrowIfNull(approval);
        ArgumentNullException.ThrowIfNull(current);
        if (!TryRead(out var native) || !approval.CanDispatch(current, new(_userBinding, (native.Flags & Enabled) != 0)))
        {
            return ToggleKeysDispatchResult.PreconditionsChanged;
        }

        native.Flags = approval.Change.Requested ? native.Flags | Enabled : native.Flags & ~Enabled;
        return SystemParametersInfo(SetToggleKeys, NativeSize, ref native, PersistToUserProfile | BroadcastSettingChange)
            ? ToggleKeysDispatchResult.Dispatched : ToggleKeysDispatchResult.WindowsDeclined;
    }

    private bool TryRead(out NativeToggleKeys native)
    {
        native = new NativeToggleKeys { Size = NativeSize };
        return CurrentUser() is { } currentUser && _registeredUser?.Equals(currentUser) == true &&
            SystemParametersInfo(GetToggleKeys, NativeSize, ref native, 0) && (native.Flags & Available) != 0;
    }

    private static SecurityIdentifier? CurrentUser()
    {
        if (WindowsPlatform.CheckCurrent() != WindowsPlatformEligibility.Supported ||
            !StartupSafety.AllowsShell(StartupSafety.CheckCurrentProcess()))
        {
            return null;
        }

        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            return identity.ImpersonationLevel == TokenImpersonationLevel.None ? identity.User : null;
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException or SecurityException or System.ComponentModel.Win32Exception)
        {
            return null;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeToggleKeys
    {
        public uint Size;
        public uint Flags;
    }

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("user32.dll", EntryPoint = "SystemParametersInfoW", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SystemParametersInfo(uint action, uint size, ref NativeToggleKeys native, uint updateFlags);
}
