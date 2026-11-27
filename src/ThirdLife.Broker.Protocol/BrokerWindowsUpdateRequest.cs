using System.Buffers;
using System.Security.Cryptography;
using System.Text.Json;
using ThirdLife.Actions.Contracts;
using ThirdLife.Actions.Registry;
using ThirdLife.Actions.WindowsUpdates;
using ThirdLife.Core.Jobs;
using ThirdLife.Core.Sanitization;
using ThirdLife.Core.WindowsUpdates;

namespace ThirdLife.Broker.Protocol;

public sealed class BrokerWindowsUpdateRequest : BrokerMessage
{
    private BrokerWindowsUpdateRequest(BrokerEnvelope envelope, JobId jobId, SanitizationGateDecisionId gateId, Guid scanId,
        string policyDecisionId, string policySha256, WindowsUpdateApprovedActions approvedActions, WindowsUpdateCandidate exact,
        Guid actionId) : base(envelope)
    {
        if (envelope.ProtocolVersion != BrokerProtocolLimits.WindowsUpdateVersion)
        {
            throw new ArgumentException("A servicing request requires the Windows Update protocol.", nameof(envelope));
        }

        ApprovalSha256 = ComputeApprovalSha256(jobId, gateId, scanId, policyDecisionId, policySha256, approvedActions, exact);
        JobId = jobId;
        GateId = gateId;
        ScanId = scanId;
        PolicyDecisionId = policyDecisionId;
        PolicySha256 = policySha256;
        ApprovedActions = approvedActions;
        Action = actionId == approvedActions.Download.ActionId ? ActionRegistry.CreateDownloadWindowsUpdate(actionId, exact) :
            actionId == approvedActions.Install.ActionId ? ActionRegistry.CreateInstallWindowsUpdate(actionId, exact) :
            throw new ArgumentException("The selected phase is outside the approval.", nameof(actionId));
    }

    public override BrokerMessageKind Kind => BrokerMessageKind.WindowsUpdateRequest;
    public JobId JobId { get; }
    public SanitizationGateDecisionId GateId { get; }
    public Guid ScanId { get; }
    public string PolicyDecisionId { get; }
    public string PolicySha256 { get; }
    public WindowsUpdateApprovedActions ApprovedActions { get; }
    public string ApprovalSha256 { get; }
    public WindowsUpdateActionRequest Action { get; }

    public static BrokerWindowsUpdateRequest Create(BrokerEnvelope envelope, JobId jobId, SanitizationGateDecisionId gateId,
        Guid scanId, string policyDecisionId, string policySha256, WindowsUpdateApprovedActions approvedActions,
        WindowsUpdateCandidate exact, Guid actionId) =>
        new(envelope, jobId, gateId, scanId, policyDecisionId, policySha256, approvedActions, exact, actionId);

    public static string ComputeApprovalSha256(JobId jobId, SanitizationGateDecisionId gateId, Guid scanId, string policyDecisionId,
        string policySha256, WindowsUpdateApprovedActions approvedActions, WindowsUpdateCandidate exact)
    {
        ArgumentNullException.ThrowIfNull(jobId);
        ArgumentNullException.ThrowIfNull(gateId);
        ArgumentNullException.ThrowIfNull(approvedActions);
        ArgumentNullException.ThrowIfNull(exact);
        ArgumentOutOfRangeException.ThrowIfEqual(scanId, Guid.Empty);
        const string prefix = "policy-decision-";
        if (policyDecisionId is null || !policyDecisionId.StartsWith(prefix, StringComparison.Ordinal) ||
            !Guid.TryParseExact(policyDecisionId.AsSpan(prefix.Length), "N", out var decision) || decision == Guid.Empty ||
            policyDecisionId != prefix + decision.ToString("N"))
        {
            throw new ArgumentException("The policy decision identifier is invalid.", nameof(policyDecisionId));
        }
        _ = BrokerMessageValidation.RequireLowerHex32(policySha256, nameof(policySha256));
        if (!approvedActions.MatchesCurrentActions(exact))
        {
            throw new ArgumentException("The approved actions differ from the current servicing contract.", nameof(approvedActions));
        }

        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WriteString("contract", "thirdlife.windows-update.approval.v1");
            writer.WriteString("job_id", jobId.Value);
            writer.WriteString("gate_id", gateId.Value);
            writer.WriteString("scan_id", scanId);
            writer.WriteString("policy_decision_id", policyDecisionId);
            writer.WriteString("policy_sha256", policySha256);
            writer.WriteString("approval_id", approvedActions.ApprovalId);
            writer.WriteString("update_id", exact.Identity.UpdateId);
            writer.WriteNumber("revision", exact.Identity.Revision);
            writer.WriteNumber("classification", (int)exact.Classification);
            writer.WriteNumber("maximum_download_bytes", exact.MaximumDownloadBytes!.Value);
            WritePhase(writer, "download", approvedActions.Download);
            WritePhase(writer, "install", approvedActions.Install);
            writer.WriteEndObject();
        }
        return Convert.ToHexStringLower(SHA256.HashData(buffer.WrittenSpan));
    }

    private static void WritePhase(Utf8JsonWriter writer, string name, WindowsUpdatePhaseAction phase)
    {
        writer.WriteStartObject(name);
        writer.WriteString("action_id", phase.ActionId);
        var impact = phase.Impact;
        writer.WriteNumber("kind", (int)impact.Kind);
        writer.WriteNumber("scope", (int)impact.Scope);
        writer.WriteNumber("privilege", (int)impact.Privilege);
        writer.WriteNumber("reboot", (int)impact.Reboot);
        writer.WriteNumber("network_use", (int)impact.NetworkUse);
        writer.WriteNumber("verification", (int)impact.Verification);
        writer.WriteNumber("rollback", (int)impact.Rollback);
        writer.WriteNumber("idempotency", (int)impact.Idempotency);
        writer.WriteNumber("availability", (int)impact.Availability);
        writer.WriteEndObject();
    }
}

public enum BrokerWindowsUpdateDispatchState
{
    NotStarted = 1,
    Attempted,
    Unconfirmed,
}

public sealed class BrokerWindowsUpdateResult : BrokerMessage
{
    public BrokerWindowsUpdateResult(BrokerEnvelope envelope, Guid actionId, string approvalSha256,
        BrokerWindowsUpdateDispatchState dispatchState, WindowsUpdateOperation operation) : base(envelope)
    {
        ArgumentOutOfRangeException.ThrowIfEqual(actionId, Guid.Empty);
        ArgumentNullException.ThrowIfNull(operation);
        if (envelope.ProtocolVersion != BrokerProtocolLimits.WindowsUpdateVersion ||
            operation.Status is < WindowsUpdateStatus.Succeeded or > WindowsUpdateStatus.Unavailable ||
            (dispatchState == BrokerWindowsUpdateDispatchState.Attempted &&
                operation.Status is not (WindowsUpdateStatus.Succeeded or WindowsUpdateStatus.Failed or WindowsUpdateStatus.Cancelled)) ||
            (dispatchState == BrokerWindowsUpdateDispatchState.NotStarted &&
                (operation.Status is WindowsUpdateStatus.Succeeded or WindowsUpdateStatus.Partial ||
                operation.RebootRequired.HasValue || operation.ProgressPercent.HasValue)) ||
            (dispatchState == BrokerWindowsUpdateDispatchState.Unconfirmed &&
                operation != new WindowsUpdateOperation(WindowsUpdateStatus.Partial, null)))
        {
            throw new ArgumentException("The receipt is inconsistent with its dispatch state.", nameof(operation));
        }
        ActionId = actionId;
        ApprovalSha256 = BrokerMessageValidation.RequireLowerHex32(approvalSha256, nameof(approvalSha256));
        DispatchState = BrokerMessageValidation.RequireDefined(dispatchState, nameof(dispatchState));
        Operation = operation;
    }

    public override BrokerMessageKind Kind => BrokerMessageKind.WindowsUpdateResult;
    public Guid ActionId { get; }
    public string ApprovalSha256 { get; }
    public BrokerWindowsUpdateDispatchState DispatchState { get; }
    public WindowsUpdateOperation Operation { get; }
}
