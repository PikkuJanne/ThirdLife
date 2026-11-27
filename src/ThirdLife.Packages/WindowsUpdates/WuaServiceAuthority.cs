using System.Runtime.InteropServices;

namespace ThirdLife.Packages.WindowsUpdates;

internal readonly record struct WuaServiceRegistration(
    bool Managed, bool Default, bool Registered, bool ScanPackage, Guid? ServiceId);

internal static unsafe class WuaServiceAuthority
{
    private static readonly Guid WindowsUpdate = new("9482f4b4-e343-43b6-b170-9a65bc822c77");
    private static readonly Guid Session3 = new("918efd1e-b5d8-4c90-8540-aeb9bdc56f9d");
    private static readonly Guid Service2 = new("1518b460-6518-4172-940f-c75883b24ceb");

    // The caller holds an IUnknown for the same session that owns the search or operation.
    // wuapi.h defines these dual-interface vtables. Only the fixed read calls below are used.
    internal static bool IsBuiltInForSession(nint sessionUnknown)
    {
        if (sessionUnknown == 0) { return false; }
        using var session = Query(sessionUnknown, Session3);
        using var manager = GetInterface(session.Pointer, 17); // IUpdateSession3::CreateUpdateServiceManager
        using var services = GetInterface(manager.Pointer, 7); // IUpdateServiceManager2::get_Services
        int count = 0;
        Check(((delegate* unmanaged[Stdcall]<nint, int*, int>)Table(services.Pointer)[9])(services.Pointer, &count));
        return IsBuiltIn(count, index => ReadRegistration(services.Pointer, index));
    }

    internal static bool IsBuiltIn(int count, Func<int, WuaServiceRegistration> readRegistration)
    {
        if (count is < 1 or > 16) { return false; }
        var found = false;
        for (var index = 0; index < count; index++)
        {
            var service = readRegistration(index);
            if (service.Managed) { return false; }
            if (!service.Default && !service.Registered) { continue; }
            if (found || !service.Default || !service.Registered || service.ScanPackage || service.ServiceId != WindowsUpdate)
            {
                return false;
            }
            found = true;
        }
        return found;
    }

    private static WuaServiceRegistration ReadRegistration(nint collection, int index)
    {
        nint pointer = 0;
        int status = ((delegate* unmanaged[Stdcall]<nint, int, nint*, int>)Table(collection)[7])(collection, index, &pointer);
        using var item = new OwnedInterface(status, pointer); // IUpdateServiceCollection::get_Item
        using var service = Query(item.Pointer, Service2);
        var managed = GetBoolean(service.Pointer, 10); // IUpdateService::get_IsManaged
        var isDefault = GetBoolean(service.Pointer, 20); // IUpdateService2::get_IsDefaultAUService
        var registered = GetBoolean(service.Pointer, 11); // IUpdateService::get_IsRegisteredWithAU
        if (managed || (!isDefault && !registered))
        {
            return new(managed, isDefault, registered, false, null);
        }
        var scanPackage = GetBoolean(service.Pointer, 16); // IUpdateService::get_IsScanPackageService
        return new(managed, isDefault, registered, scanPackage, ReadServiceId(service.Pointer));
    }

    private static Guid? ReadServiceId(nint service)
    {
        nint bstr = 0;
        try
        {
            Check(((delegate* unmanaged[Stdcall]<nint, nint*, int>)Table(service)[15])(service, &bstr));
            return bstr != 0 && SysStringLen(bstr) == 36 &&
                Guid.TryParseExact(new ReadOnlySpan<char>((void*)bstr, 36), "D", out var id) ? id : null;
        }
        finally { Marshal.FreeBSTR(bstr); }
    }

    private static bool GetBoolean(nint source, int slot)
    {
        short value = 0;
        Check(((delegate* unmanaged[Stdcall]<nint, short*, int>)Table(source)[slot])(source, &value));
        return value switch { 0 => false, -1 => true, _ => throw new InvalidDataException() };
    }

    private static OwnedInterface GetInterface(nint source, int slot)
    {
        nint pointer = 0;
        int status = ((delegate* unmanaged[Stdcall]<nint, nint*, int>)Table(source)[slot])(source, &pointer);
        return new(status, pointer);
    }

    private static OwnedInterface Query(nint source, Guid interfaceId)
    {
        nint pointer = 0;
        int status = ((delegate* unmanaged[Stdcall]<nint, Guid*, nint*, int>)Table(source)[0])(source, &interfaceId, &pointer);
        return new(status, pointer);
    }

    private sealed class OwnedInterface : IDisposable
    {
        internal OwnedInterface(int status, nint pointer)
        {
            Pointer = pointer;
            if (status != 0 || pointer == 0)
            {
                Dispose();
                Check(status);
                throw new InvalidDataException();
            }
        }

        internal nint Pointer { get; private set; }

        public void Dispose()
        {
            if (Pointer != 0)
            {
                _ = ((delegate* unmanaged[Stdcall]<nint, uint>)Table(Pointer)[2])(Pointer);
                Pointer = 0;
            }
        }
    }

    private static nint* Table(nint source) => *(nint**)source;
    private static void Check(int status)
    {
        Marshal.ThrowExceptionForHR(status, new nint(-1));
        if (status != 0) { throw new InvalidDataException(); }
    }

    [DllImport("oleaut32.dll", ExactSpelling = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern uint SysStringLen(nint bstr);
}
