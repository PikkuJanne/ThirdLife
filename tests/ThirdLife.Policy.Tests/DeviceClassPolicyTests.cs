using System.Text;
using System.Text.Json.Nodes;
using ThirdLife.Core.Evidence;
using ThirdLife.Policy.Decisions;
using ThirdLife.Policy.Schema;
using static ThirdLife.Policy.Tests.PolicyEvaluationFixture;

namespace ThirdLife.Policy.Tests;

public sealed class DeviceClassPolicyTests
{
    [Fact]
    public void SelectedMobileAndStationaryPoliciesApplyTheirOwnBatteryRequirements()
    {
        var mobile = BatteryPolicy("laptop", minimumChargeRatio: 60);
        var stationary = BatteryPolicy("desktop", minimumChargeRatio: 40);
        var weakerBattery = Fact(
            "weak-battery-test-finding", "battery.full_charge_ratio_percent",
            EvidenceValue.FromInteger(50), EvidenceClassification.Inferred, unit: "percent");
        var evidence = ReadyObservations()
            .Where(observation => observation.EvidenceKey != weakerBattery.EvidenceKey)
            .Append(weakerBattery).ToArray();
        PolicyDocument[] offered = [stationary, mobile];

        var laptopPolicy = DeviceClassPolicy.Select(PolicyDeviceClass.Laptop, offered);
        var desktopPolicy = DeviceClassPolicy.Select(PolicyDeviceClass.Desktop, offered);

        Assert.Same(mobile, laptopPolicy);
        Assert.Same(stationary, desktopPolicy);
        var laptopDecision = DispositionEngine.Evaluate(laptopPolicy!, evidence, ActiveCapabilities());
        var desktopDecision = DispositionEngine.Evaluate(desktopPolicy!, evidence, ActiveCapabilities());
        Assert.Equal(Disposition.RepairAndRetest, laptopDecision.Disposition);
        Assert.Equal(Disposition.ReadyToPrepare, desktopDecision.Disposition);
        var battery = Assert.Single(laptopDecision.Citations);
        Assert.Equal("POL-BATTERY-003", battery.RuleId.Value);
        Assert.Equal(weakerBattery.Metadata.EvidenceId, Assert.Single(battery.Evidence).EvidenceId);
        Assert.DoesNotContain("score", laptopDecision.Explanation, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void MissingPolicyForAClassHasNoFallback()
    {
        var mobile = BatteryPolicy("laptop", minimumChargeRatio: 60);

        Assert.Null(DeviceClassPolicy.Select(PolicyDeviceClass.Desktop, [mobile]));
        Assert.Null(DeviceClassPolicy.Select(PolicyDeviceClass.Laptop, []));
        Assert.Throws<ArgumentOutOfRangeException>(() => DeviceClassPolicy.Select((PolicyDeviceClass)0, [mobile]));
    }

    [Fact]
    public void DuplicatePoliciesForOneClassDoNotSelectByEnumerationOrder()
    {
        var original = BatteryPolicy("laptop", minimumChargeRatio: 60);
        var competing = BatteryPolicy("laptop", minimumChargeRatio: 70);

        Assert.Throws<InvalidOperationException>(() => DeviceClassPolicy.Select(PolicyDeviceClass.Laptop, [original, competing]));
        Assert.Throws<InvalidOperationException>(() => DeviceClassPolicy.Select(PolicyDeviceClass.Laptop, [competing, original]));
    }

    private static PolicyDocument BatteryPolicy(string deviceClass, long minimumChargeRatio)
    {
        var document = JsonNode.Parse(CandidatePolicyJson())!.AsObject();
        document["policy_id"] = "synthetic-" + deviceClass + "-battery-policy";
        document["target"]!["device_class"] = deviceClass;
        var battery = Assert.Single(document["rules"]!.AsArray(), rule =>
            rule!["id"]!.GetValue<string>() == "POL-BATTERY-003")!;
        battery["condition"]!["value"] = minimumChargeRatio;
        battery["requirement_type"] = "repairable";
        battery["severity"] = "required";
        battery["decision"] = new JsonObject
        {
            ["blocks_ready"] = true,
            ["unmet_disposition"] = "repair_and_retest",
            ["unavailable_disposition"] = "human_review_required",
        };

        return PolicySchema.Parse(Encoding.UTF8.GetBytes(document.ToJsonString()));
    }
}
