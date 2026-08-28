namespace ThirdLife.Diagnostics;

public sealed class DiagnosticContractException : Exception
{
    internal DiagnosticContractException(
        string resultCode,
        string message,
        bool durableStateAmbiguous = false)
        : base(message)
    {
        ResultCode = DiagnosticText.RequireCode(resultCode, nameof(resultCode), 64);
        DurableStateAmbiguous = durableStateAmbiguous;
    }

    public string ResultCode { get; }

    public bool DurableStateAmbiguous { get; }
}
