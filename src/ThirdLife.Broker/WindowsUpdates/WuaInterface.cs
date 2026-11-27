using System.Runtime.InteropServices;

namespace ThirdLife.Broker.WindowsUpdates;

// x64 Windows SDK wuapi.h. Each call site identifies its fixed interface and slot.
// No automation binder, runtime-callable wrapper, or request-provided native name is used.
internal sealed unsafe class WuaInterface : IDisposable
{
    internal nint Pointer { get; private set; }
    private nint* Methods => *(nint**)Pointer;

    private WuaInterface(int status, nint pointer)
    {
        Pointer = pointer;
        if (status == 0 && pointer != 0) { return; }
        Dispose();
        Check(status);
        throw new InvalidDataException();
    }

    internal static WuaInterface Activate(Guid classId, Guid interfaceId)
    {
        // Use only the local in-process COM server and exclude downloaded code and activate-as-activator.
        const uint context = 0x1 | 0x400 | 0x8000;
        nint pointer = 0;
        var status = CoCreateInstance(&classId, 0, context, &interfaceId, &pointer);
        return new(status, pointer);
    }

    internal WuaInterface Query(Guid interfaceId)
    {
        nint value = 0;
        var status = ((delegate* unmanaged[Stdcall]<nint, Guid*, nint*, int>)Methods[0])(Pointer, &interfaceId, &value);
        return new(status, value);
    }

    internal WuaInterface Object(int slot)
    {
        nint value = 0;
        var status = ((delegate* unmanaged[Stdcall]<nint, nint*, int>)Methods[slot])(Pointer, &value);
        return new(status, value);
    }

    internal WuaInterface? OptionalObject(int slot)
    {
        nint value = 0;
        var status = ((delegate* unmanaged[Stdcall]<nint, nint*, int>)Methods[slot])(Pointer, &value);
        return status == 0 && value == 0 ? null : new(status, value);
    }

    internal WuaInterface Item(int slot, int index)
    {
        nint value = 0;
        var status = ((delegate* unmanaged[Stdcall]<nint, int, nint*, int>)Methods[slot])(Pointer, index, &value);
        return new(status, value);
    }

    internal int Number(int slot)
    {
        int value = 0;
        Check(((delegate* unmanaged[Stdcall]<nint, int*, int>)Methods[slot])(Pointer, &value));
        return value;
    }

    internal bool Flag(int slot)
    {
        short value = 0;
        Check(((delegate* unmanaged[Stdcall]<nint, short*, int>)Methods[slot])(Pointer, &value));
        return value switch { 0 => false, -1 => true, _ => throw new InvalidDataException() };
    }

    internal string Text(int slot, uint limit)
    {
        nint value = 0;
        try
        {
            Check(((delegate* unmanaged[Stdcall]<nint, nint*, int>)Methods[slot])(Pointer, &value));
            var size = value == 0 ? 0u : SysStringLen(value);
            if (size > limit) { throw new InvalidDataException(); }
            return size == 0 ? string.Empty : new string((char*)value, 0, (int)size);
        }
        finally { Marshal.FreeBSTR(value); }
    }

    internal long? DownloadBytes()
    {
        // DECIMAL is a 16-byte OLE structure, independent of the managed decimal layout.
        NativeDecimal value = default;
        Check(((delegate* unmanaged[Stdcall]<nint, NativeDecimal*, int>)Methods[31])(Pointer, &value)); // IUpdate::get_MaxDownloadSize
        if (value.Sign != 0 || value.Scale > 28 || value.Reserved != 0) { return null; }
        var size = new decimal((int)value.Low, (int)value.Mid, (int)value.High, false, value.Scale);
        return size <= 9_007_199_254_740_991m && decimal.Truncate(size) == size ? (long)size : null;
    }

    internal void SetFlag(int slot, bool enabled) =>
        Check(((delegate* unmanaged[Stdcall]<nint, short, int>)Methods[slot])(Pointer, enabled ? (short)-1 : (short)0));

    internal void SetObject(int slot, WuaInterface value) =>
        Check(((delegate* unmanaged[Stdcall]<nint, nint, int>)Methods[slot])(Pointer, value.Pointer));

    internal void SetClientId()
    {
        var id = Marshal.StringToBSTR("ThirdLife Setup Core");
        try { Check(((delegate* unmanaged[Stdcall]<nint, nint, int>)Methods[8])(Pointer, id)); } // IUpdateSession::put_ClientApplicationID
        finally { Marshal.FreeBSTR(id); }
    }

    internal void AddSingleUpdate(WuaInterface update)
    {
        int index = -1;
        Check(((delegate* unmanaged[Stdcall]<nint, nint, int*, int>)Methods[12])(Pointer, update.Pointer, &index)); // IUpdateCollection::Add
        if (index != 0 || Number(10) != 1) { throw new InvalidDataException(); }
    }

    internal void Call(int slot) => Check(((delegate* unmanaged[Stdcall]<nint, int>)Methods[slot])(Pointer));

    internal WuaInterface BeginSearch(string exactCriteria, nint completed)
    {
        var criteria = Marshal.StringToBSTR(exactCriteria);
        try
        {
            nint job = 0;
            var status = ((delegate* unmanaged[Stdcall]<nint, nint, nint, EmptyVariant, nint*, int>)Methods[15])(
                Pointer, criteria, completed, default, &job); // IUpdateSearcher::BeginSearch
            return new(status, job);
        }
        finally { Marshal.FreeBSTR(criteria); }
    }

    internal WuaInterface BeginOperation(bool install, nint progress, nint completed)
    {
        nint job = 0;
        var slot = install ? 17 : 15; // IUpdateInstaller::BeginInstall / IUpdateDownloader::BeginDownload
        var status = ((delegate* unmanaged[Stdcall]<nint, nint, nint, EmptyVariant, nint*, int>)Methods[slot])(
            Pointer, progress, completed, default, &job);
        return new(status, job);
    }

    internal WuaInterface End(int slot, WuaInterface job)
    {
        nint result = 0;
        var status = ((delegate* unmanaged[Stdcall]<nint, nint, nint*, int>)Methods[slot])(Pointer, job.Pointer, &result);
        return new(status, result);
    }

    public void Dispose()
    {
        if (Pointer != 0)
        {
            _ = ((delegate* unmanaged[Stdcall]<nint, uint>)Methods[2])(Pointer);
            Pointer = 0;
        }
    }

    internal static void Check(int status)
    {
        Marshal.ThrowExceptionForHR(status, new nint(-1));
        if (status != 0) { throw new InvalidDataException(); }
    }

    [StructLayout(LayoutKind.Sequential, Size = 24)]
    private struct EmptyVariant { }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeDecimal
    {
        public ushort Reserved;
        public byte Scale;
        public byte Sign;
        public uint High;
        public uint Low;
        public uint Mid;
    }

    [DllImport("ole32.dll", ExactSpelling = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern int CoCreateInstance(Guid* classId, nint outer, uint context, Guid* interfaceId, nint* result);

    [DllImport("oleaut32.dll", ExactSpelling = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern uint SysStringLen(nint bstr);
}
