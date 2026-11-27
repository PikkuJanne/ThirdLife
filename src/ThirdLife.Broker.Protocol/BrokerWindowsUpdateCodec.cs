using System.Text.Json;
using ThirdLife.Actions.Contracts;
using ThirdLife.Actions.Registry;
using ThirdLife.Actions.WindowsUpdates;
using ThirdLife.Core.Jobs;
using ThirdLife.Core.Sanitization;
using ThirdLife.Core.WindowsUpdates;

namespace ThirdLife.Broker.Protocol;

public static partial class BrokerProtocolCodec
{
    private static BrokerWindowsUpdateRequest ReadWindowsUpdateRequest(JsonElement root, BrokerEnvelope envelope)
    {
        RequireFields(root, ["job_id", "gate_id", "scan_id", "policy_decision_id", "policy_sha256", "approval_id",
            "download_action_id", "install_action_id", "approval_sha256", "action"]);
        if (ActionRequestCodec.Parse(StrictUtf8.GetBytes(root.GetProperty("action").GetRawText())) is not
            WindowsUpdateActionRequest { Exact: { } exact } action)
        {
            throw new JsonException();
        }
        var impact = ActionRegistry.DescribeWindowsUpdateServicing(exact);
        var approval = new WindowsUpdateApprovedActions(ReadGuid(root, "approval_id"),
            new(ReadGuid(root, "download_action_id"), impact.Download), new(ReadGuid(root, "install_action_id"), impact.Install));
        var request = BrokerWindowsUpdateRequest.Create(envelope, new JobId(ReadString(root, "job_id")),
            new SanitizationGateDecisionId(ReadString(root, "gate_id")), ReadGuid(root, "scan_id"), ReadString(root, "policy_decision_id"),
            ReadString(root, "policy_sha256"), approval, exact, action.ActionId);
        return request.ApprovalSha256 == ReadString(root, "approval_sha256") && request.Action.Kind == action.Kind
            ? request : throw new JsonException();
    }

    private static void WriteWindowsUpdateRequest(Utf8JsonWriter writer, BrokerWindowsUpdateRequest request)
    {
        writer.WriteString("job_id", request.JobId.Value);
        writer.WriteString("gate_id", request.GateId.Value);
        writer.WriteString("scan_id", request.ScanId);
        writer.WriteString("policy_decision_id", request.PolicyDecisionId);
        writer.WriteString("policy_sha256", request.PolicySha256);
        writer.WriteString("approval_id", request.ApprovedActions.ApprovalId);
        writer.WriteString("download_action_id", request.ApprovedActions.Download.ActionId);
        writer.WriteString("install_action_id", request.ApprovedActions.Install.ActionId);
        writer.WriteString("approval_sha256", request.ApprovalSha256);
        writer.WritePropertyName("action");
        writer.WriteRawValue(ActionRequestCodec.Serialize(request.Action));
    }

    private static BrokerWindowsUpdateResult ReadWindowsUpdateResult(JsonElement root, BrokerEnvelope envelope)
    {
        RequireFields(root, ["action_id", "approval_sha256", "dispatch_state", "status", "reboot_required", "progress_percent"]);
        var dispatch = ReadString(root, "dispatch_state") switch
        {
            "not_started" => BrokerWindowsUpdateDispatchState.NotStarted,
            "attempted" => BrokerWindowsUpdateDispatchState.Attempted,
            "unconfirmed" => BrokerWindowsUpdateDispatchState.Unconfirmed,
            _ => throw new JsonException(),
        };
        var reboot = root.GetProperty("reboot_required");
        var percent = root.GetProperty("progress_percent");
        var operation = new WindowsUpdateOperation((WindowsUpdateStatus)ReadInt32(root, "status"),
            reboot.ValueKind == JsonValueKind.Null ? null : reboot.GetBoolean(),
            percent.ValueKind == JsonValueKind.Null ? null : ReadInt32(root, "progress_percent"));
        return new(envelope, ReadGuid(root, "action_id"), ReadString(root, "approval_sha256"), dispatch, operation);
    }

    private static void WriteWindowsUpdateResult(Utf8JsonWriter writer, BrokerWindowsUpdateResult result)
    {
        writer.WriteString("action_id", result.ActionId);
        writer.WriteString("approval_sha256", result.ApprovalSha256);
        writer.WriteString("dispatch_state", result.DispatchState switch
        {
            BrokerWindowsUpdateDispatchState.NotStarted => "not_started",
            BrokerWindowsUpdateDispatchState.Attempted => "attempted",
            BrokerWindowsUpdateDispatchState.Unconfirmed => "unconfirmed",
            _ => throw new JsonException(),
        });
        writer.WriteNumber("status", (int)result.Operation.Status);
        if (result.Operation.RebootRequired is { } reboot) { writer.WriteBoolean("reboot_required", reboot); }
        else { writer.WriteNull("reboot_required"); }
        if (result.Operation.ProgressPercent is { } percent) { writer.WriteNumber("progress_percent", percent); }
        else { writer.WriteNull("progress_percent"); }
    }
}
