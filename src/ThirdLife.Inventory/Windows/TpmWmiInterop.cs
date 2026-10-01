using System.Runtime.InteropServices;

namespace ThirdLife.Inventory.Windows;

// IWbemServices slot 24 is used only for the compiled, no-input IsReadyInformation
// read. Every other slot is inert; query/connection interfaces are reused unchanged.
[ComImport]
[Guid("9556DC99-828C-11CF-A37E-00AA003240C7")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface ITpmWbemReadinessServices
{
    void Reserved01();
    void Reserved02();
    void Reserved03();
    void Reserved04();
    void Reserved05();
    void Reserved06();
    void Reserved07();
    void Reserved08();
    void Reserved09();
    void Reserved10();
    void Reserved11();
    void Reserved12();
    void Reserved13();
    void Reserved14();
    void Reserved15();
    void Reserved16();
    void Reserved17();
    void Reserved18();
    void Reserved19();
    void Reserved20();
    void Reserved21();

    [PreserveSig]
    int ExecMethod(
        [MarshalAs(UnmanagedType.BStr)] string objectPath,
        [MarshalAs(UnmanagedType.BStr)] string methodName,
        int flags,
        nint context,
        nint inputParameters,
        [MarshalAs(UnmanagedType.Interface)] out IStorageWbemObject? outputParameters,
        nint callResult);
}
