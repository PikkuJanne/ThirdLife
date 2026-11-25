using System.Text.Json.Serialization;

namespace ThirdLife.Core.Evidence;

[JsonConverter(typeof(StableStringEnumConverter<Disposition>))]
public enum Disposition
{
    [JsonStringEnumMemberName("ready_to_prepare")]
    ReadyToPrepare = 1,

    [JsonStringEnumMemberName("repair_and_retest")]
    RepairAndRetest,

    [JsonStringEnumMemberName("human_review_required")]
    HumanReviewRequired,

    // Retained for serialized history and v1 policy replay.
    [JsonStringEnumMemberName("alternative_operating_system_candidate")]
    AlternativeOperatingSystemCandidate,

    [JsonStringEnumMemberName("do_not_deploy")]
    DoNotDeploy,
}

public static class DispositionVocabulary
{
    public static bool IsCurrent(Disposition disposition) => disposition is Disposition.ReadyToPrepare or
        Disposition.RepairAndRetest or Disposition.HumanReviewRequired or Disposition.DoNotDeploy;
}
