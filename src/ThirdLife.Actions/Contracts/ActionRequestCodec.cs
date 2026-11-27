using System.Text;
using System.Text.Json;
using ThirdLife.Actions.Registry;
using ThirdLife.Core.WindowsUpdates;

namespace ThirdLife.Actions.Contracts;

public static class ActionRequestCodec
{
    public const int ContractVersion = 1;
    public const int MaximumDocumentBytes = 4096;
    private const string InvalidRequest = "Action request does not satisfy the ThirdLife action v1 contract.";
    private const string NoOpType = "no_op";
    private const string InstallPackageType = "install_package";
    private const string UpdateScanType = "windows_update_scan";
    private const string UpdateDownloadType = "windows_update_download";
    private const string UpdateInstallType = "windows_update_install";
    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    public static ActionRequest Parse(ReadOnlyMemory<byte> utf8Json)
    {
        if (utf8Json.IsEmpty || utf8Json.Length > MaximumDocumentBytes)
        {
            throw new JsonException(InvalidRequest);
        }

        try
        {
            var ownedBytes = utf8Json.ToArray();
            _ = StrictUtf8.GetCharCount(ownedBytes);
            using var document = JsonDocument.Parse(ownedBytes, new JsonDocumentOptions
            {
                AllowDuplicateProperties = false,
                MaxDepth = 4,
            });
            return ReadRequest(document.RootElement);
        }
        catch (Exception exception) when (exception is JsonException or ArgumentException or InvalidOperationException)
        {
            // Parser and registry failures may include untrusted input.
            throw new JsonException(InvalidRequest);
        }
    }

    public static byte[] Serialize(ActionRequest request)
    {
        var type = request switch
        {
            NoOpActionRequest => NoOpType,
            InstallPackageActionRequest => InstallPackageType,
            WindowsUpdateActionRequest { Kind: ActionKind.ScanWindowsUpdate } => UpdateScanType,
            WindowsUpdateActionRequest { Kind: ActionKind.DownloadWindowsUpdate } => UpdateDownloadType,
            WindowsUpdateActionRequest { Kind: ActionKind.InstallWindowsUpdate } => UpdateInstallType,
            _ => throw new JsonException(InvalidRequest),
        };

        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteNumber("contract_version", ContractVersion);
            writer.WriteString("action_id", request.ActionId);
            writer.WriteString("type", type);
            writer.WriteStartObject("parameters");
            if (request is InstallPackageActionRequest package)
            {
                WriteInstallPackage(writer, package);
            }
            else if (request is WindowsUpdateActionRequest { Exact: { } exact })
            {
                writer.WriteString("update_id", exact.Identity.UpdateId);
                writer.WriteNumber("revision", exact.Identity.Revision);
                writer.WriteString("classification", exact.Classification switch
                {
                    WindowsUpdateClass.Security => "security",
                    WindowsUpdateClass.Critical => "critical",
                    _ => throw new JsonException(InvalidRequest),
                });
                if (exact.MaximumDownloadBytes is { } bytes)
                {
                    writer.WriteNumber("maximum_download_bytes", bytes);
                }
                else
                {
                    writer.WriteNull("maximum_download_bytes");
                }
            }

            writer.WriteEndObject();
            writer.WriteEndObject();
        }

        return stream.Length <= MaximumDocumentBytes
            ? stream.ToArray()
            : throw new JsonException(InvalidRequest);
    }

    private static ActionRequest ReadRequest(JsonElement root)
    {
        RequireFields(root, ["contract_version", "action_id", "type", "parameters"]);
        if (!root.GetProperty("contract_version").TryGetInt32(out var version) || version != ContractVersion)
        {
            throw new JsonException();
        }

        var actionId = ReadGuid(root, "action_id");
        var parameters = root.GetProperty("parameters");
        return ReadString(root, "type") switch
        {
            NoOpType => ReadNoOp(actionId, parameters),
            InstallPackageType => ReadInstallPackage(actionId, parameters),
            UpdateScanType => ReadUpdateScan(actionId, parameters),
            UpdateDownloadType => ActionRegistry.CreateDownloadWindowsUpdate(actionId, ReadUpdate(parameters)),
            UpdateInstallType => ActionRegistry.CreateInstallWindowsUpdate(actionId, ReadUpdate(parameters)),
            _ => throw new JsonException(),
        };
    }

    private static NoOpActionRequest ReadNoOp(Guid actionId, JsonElement parameters)
    {
        RequireFields(parameters, []);
        return ActionRegistry.CreateNoOp(actionId);
    }

    private static WindowsUpdateActionRequest ReadUpdateScan(Guid actionId, JsonElement parameters)
    {
        RequireFields(parameters, []);
        return ActionRegistry.CreateScanWindowsUpdate(actionId);
    }

    private static WindowsUpdateCandidate ReadUpdate(JsonElement parameters)
    {
        RequireFields(parameters, ["update_id", "revision", "classification", "maximum_download_bytes"]);
        var updateId = ReadGuid(parameters, "update_id");
        var classification = ReadString(parameters, "classification") switch
        {
            "security" => WindowsUpdateClass.Security,
            "critical" => WindowsUpdateClass.Critical,
            _ => throw new JsonException(),
        };
        if (!parameters.GetProperty("revision").TryGetInt32(out var revision))
        {
            throw new JsonException();
        }

        var size = parameters.GetProperty("maximum_download_bytes");
        long? estimate = null;
        if (size.ValueKind != JsonValueKind.Null)
        {
            estimate = size.TryGetInt64(out var bytes) ? bytes : throw new JsonException();
        }

        return new WindowsUpdateCandidate(new WindowsUpdateIdentity(updateId, revision), classification, estimate);
    }

    private static InstallPackageActionRequest ReadInstallPackage(Guid actionId, JsonElement parameters)
    {
        RequireFields(parameters, ["source_id", "package_id", "exact_version", "publisher", "scope", "architecture"]);
        var scope = ReadString(parameters, "scope") switch
        {
            "user" => ActionScope.User,
            "machine" => ActionScope.Machine,
            _ => throw new JsonException(),
        };
        var architecture = ReadString(parameters, "architecture") switch
        {
            "x64" => ActionArchitecture.X64,
            "arm64" => ActionArchitecture.Arm64,
            "x86" => ActionArchitecture.X86,
            _ => throw new JsonException(),
        };

        return ActionRegistry.CreateInstallPackage(
            actionId,
            ReadString(parameters, "source_id"),
            ReadString(parameters, "package_id"),
            ReadString(parameters, "exact_version"),
            ReadString(parameters, "publisher"),
            scope,
            architecture);
    }

    private static void WriteInstallPackage(Utf8JsonWriter writer, InstallPackageActionRequest package)
    {
        writer.WriteString("source_id", package.SourceId);
        writer.WriteString("package_id", package.PackageId);
        writer.WriteString("exact_version", package.ExactVersion);
        writer.WriteString("publisher", package.Publisher);
        writer.WriteString("scope", package.Scope switch
        {
            ActionScope.User => "user",
            ActionScope.Machine => "machine",
            _ => throw new JsonException(InvalidRequest),
        });
        writer.WriteString("architecture", package.Architecture switch
        {
            ActionArchitecture.X64 => "x64",
            ActionArchitecture.Arm64 => "arm64",
            ActionArchitecture.X86 => "x86",
            _ => throw new JsonException(InvalidRequest),
        });
    }

    private static void RequireFields(JsonElement value, ReadOnlySpan<string> expected)
    {
        var count = 0;
        foreach (var property in value.EnumerateObject())
        {
            if (!expected.Contains(property.Name))
            {
                throw new JsonException();
            }

            count++;
        }

        if (count != expected.Length)
        {
            throw new JsonException();
        }
    }

    private static string ReadString(JsonElement value, string name) =>
        value.GetProperty(name).GetString() ?? throw new JsonException();

    private static Guid ReadGuid(JsonElement value, string name)
    {
        var text = ReadString(value, name);
        return Guid.TryParseExact(text, "D", out var id) && id != Guid.Empty &&
            string.Equals(text, id.ToString("D"), StringComparison.Ordinal)
            ? id : throw new JsonException();
    }
}
