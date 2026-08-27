using System.Text.Json.Serialization;

namespace ThirdLife.Core.Jobs;

[JsonConverter(typeof(StableStringEnumConverter<ActionState>))]
public enum ActionState
{
    [JsonStringEnumMemberName("planned")]
    Planned = 1,

    [JsonStringEnumMemberName("approved")]
    Approved,

    [JsonStringEnumMemberName("started")]
    Started,

    [JsonStringEnumMemberName("applied")]
    Applied,

    [JsonStringEnumMemberName("verified")]
    Verified,

    [JsonStringEnumMemberName("failed")]
    Failed,

    [JsonStringEnumMemberName("skipped")]
    Skipped,

    [JsonStringEnumMemberName("rolled_back")]
    RolledBack,

    [JsonStringEnumMemberName("requires_review")]
    RequiresReview,
}
