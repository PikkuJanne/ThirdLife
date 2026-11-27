using System.Text.Json.Serialization;

namespace ThirdLife.Core.Handover;

[JsonConverter(typeof(StableStringEnumConverter<HandoverMode>))]
public enum HandoverMode
{
    [JsonStringEnumMemberName("assisted")]
    Assisted = 1,
    [JsonStringEnumMemberName("sealed")]
    Sealed,
}

[JsonConverter(typeof(StableStringEnumConverter<RecipientSetupArea>))]
public enum RecipientSetupArea
{
    [JsonStringEnumMemberName("personal_account")]
    PersonalAccount = 1,
    [JsonStringEnumMemberName("backup")]
    Backup,
    [JsonStringEnumMemberName("recovery_material")]
    RecoveryMaterial,
    [JsonStringEnumMemberName("browser")]
    Browser,
    [JsonStringEnumMemberName("accessibility")]
    Accessibility,
    [JsonStringEnumMemberName("other_preferences")]
    OtherPreferences,
}

[JsonConverter(typeof(StableStringEnumConverter<RecipientSetupChoice>))]
public enum RecipientSetupChoice
{
    [JsonStringEnumMemberName("deferred")]
    Deferred = 1,
    [JsonStringEnumMemberName("guidance_requested")]
    GuidanceRequested,
    [JsonStringEnumMemberName("declined")]
    Declined,
}

public sealed record RecipientSetupDecision(RecipientSetupArea Area, RecipientSetupChoice Choice);

/// <summary>Records guidance choices. It does not confer execution authority or verify setup.</summary>
public sealed class RecipientHandover
{
    private static readonly RecipientSetupArea[] SetupAreas = Enum.GetValues<RecipientSetupArea>();

    private RecipientHandover(HandoverMode mode, RecipientSetupDecision[] decisions)
    {
        Mode = mode;
        Decisions = Array.AsReadOnly(decisions);
    }

    public HandoverMode Mode { get; }
    public IReadOnlyList<RecipientSetupDecision> Decisions { get; }

    public static RecipientHandover Start(HandoverMode mode) =>
        new(DomainValue.RequireDefined(mode, nameof(mode)),
            SetupAreas.Select(area => new RecipientSetupDecision(area, RecipientSetupChoice.Deferred)).ToArray());

    public RecipientHandover ChangeMode(HandoverMode mode) =>
        mode == Mode ? this : Start(mode);

    public RecipientHandover RecordChoice(RecipientSetupArea area, RecipientSetupChoice choice, bool recipientPresent)
    {
        DomainValue.RequireDefined(area, nameof(area));
        DomainValue.RequireDefined(choice, nameof(choice));
        if (Mode != HandoverMode.Assisted || !recipientPresent)
        {
            throw new InvalidOperationException("Recording a recipient choice requires assisted handover with the recipient present.");
        }

        var decisions = Decisions.Select(previous => previous.Area == area ? previous with { Choice = choice } : previous);
        return new(Mode, decisions.ToArray());
    }
}
