using PipingRouter.Engine;
using PipingRouter.Engine.Geometry;
using PipingRouter.Engine.Io;
using PipingRouter.Engine.Rules;
using Xunit;
using static PipingRouter.Tests.Io.IssueAssert;

namespace PipingRouter.Tests.Io;

public class RuleConfigJsonTests
{
    private static string ExampleText => File.ReadAllText(RepoPaths.Combine("config", "rules.example.json"));

    [Fact]
    public void ExampleFile_LoadsWithoutIssues_AndIsMarkedExample()
    {
        var r = RuleConfigJson.Load(RepoPaths.Combine("config", "rules.example.json"));
        NoIssues(r);
        var c = r.Value!;
        Assert.True(c.IsExample);
        Assert.Contains("示範", c.Disclaimer);
    }

    [Fact]
    public void ExampleFile_MapsValues()
    {
        var c = RuleConfigJson.Parse(ExampleText).ValueOrThrow();
        Assert.Equal(1e-6, c.EpsilonMm);
        Assert.Equal(100, c.Clearance.PipeToObstacleMinMm);
        Assert.Equal(300, c.MinStraightBetweenBendsMm);
        Assert.Equal(ZonePolicy.Forbid, c.ServiceZonePolicy.Maintenance);
        Assert.Equal(Severity.Warning, c.SeverityOf(RuleIds.UnsupportedComponent));
        Assert.Equal(Severity.Error, c.SeverityOf(RuleIds.Collision));

        Assert.Equal(new[] { "checkValve", "controlValve", "flowMeter", "handValve" }, c.ComponentTypes.Keys.ToArray());

        var hv = c.ComponentTypes["handValve"];
        Assert.Equal(900, hv.Handwheel!.HeightAboveFloorMm.Min);
        Assert.Equal(1500, hv.Handwheel.HeightAboveFloorMm.Max);
        Assert.Equal(new Vec3(0, 0, 250), hv.Handwheel.CenterOffsetFromAxisMm);
        var zone = Assert.IsType<CylinderClearanceZone>(hv.Handwheel.ClearanceZone);
        Assert.Equal((300.0, 200.0), (zone.RadiusMm, zone.HeightMm));
        Assert.Null(hv.StraightRun);

        var fm = c.ComponentTypes["flowMeter"];
        Assert.Equal((10.0, 5.0), (fm.StraightRun!.UpstreamDiameters, fm.StraightRun.DownstreamDiameters));
        Assert.Null(fm.Handwheel);
    }

    [Fact]
    public void RoundTrip_IsStable()
    {
        var c = RuleConfigJson.Parse(ExampleText).ValueOrThrow();
        string t1 = RuleConfigJson.Serialize(c);
        var again = RuleConfigJson.Parse(t1);
        NoIssues(again);
        Assert.Equal(t1, RuleConfigJson.Serialize(again.Value!));
    }

    [Fact]
    public void BoxClearanceZone_ParsesAndRoundTrips()
    {
        string json = Mutate(ExampleText,
            "\"type\": \"cylinder\",\n          \"radiusMm\": 300,\n          \"heightMm\": 200",
            "\"type\": \"box\",\n          \"sizeMm\": [600, 600, 200]");
        var c = RuleConfigJson.Parse(json).ValueOrThrow();
        var zone = Assert.IsType<BoxClearanceZone>(c.ComponentTypes["handValve"].Handwheel!.ClearanceZone);
        Assert.Equal(new Vec3(600, 600, 200), zone.SizeMm);
        var again = RuleConfigJson.Parse(RuleConfigJson.Serialize(c)).ValueOrThrow();
        Assert.IsType<BoxClearanceZone>(again.ComponentTypes["handValve"].Handwheel!.ClearanceZone);
    }

    [Fact]
    public void MissingOptionalSections_UseDefaults()
    {
        const string json = """
        {
          "schemaVersion": 1, "profile": "EXAMPLE",
          "geometry": { "epsilonMm": 0.001 },
          "clearance": { "pipeToObstacleMinMm": 50, "pipeToPipeMinMm": 50 },
          "straightRun": { "minBetweenBendsMm": 200 },
          "componentTypes": {}
        }
        """;
        var c = RuleConfigJson.Parse(json).ValueOrThrow();
        Assert.Equal(ZonePolicy.Forbid, c.ServiceZonePolicy.Maintenance);
        Assert.Equal(ZonePolicy.Forbid, c.ServiceZonePolicy.Operation);
        foreach (var id in RuleIds.All) Assert.Equal(RuleIds.DefaultSeverity(id), c.SeverityOf(id));
        Assert.Equal("", c.Disclaimer);
    }

    [Fact]
    public void SeverityOverride_IsApplied()
    {
        var c = RuleConfigJson.Parse(Mutate(ExampleText, "\"MinClearance\": \"error\"", "\"MinClearance\": \"warning\"")).ValueOrThrow();
        Assert.Equal(Severity.Warning, c.SeverityOf(RuleIds.MinClearance));
    }

    [Fact]
    public void PenalizePolicy_IsParsed()
    {
        var c = RuleConfigJson.Parse(Mutate(ExampleText, "\"maintenance\": \"forbid\"", "\"maintenance\": \"penalize\"")).ValueOrThrow();
        Assert.Equal(ZonePolicy.Penalize, c.ServiceZonePolicy.Maintenance);
        Assert.Equal(ZonePolicy.Forbid, c.ServiceZonePolicy.Operation);
    }

    // ---------------------------------------------------------------- 錯誤
    [Fact]
    public void MissingProfile() =>
        HasError(RuleConfigJson.Parse(Mutate(ExampleText, "\"profile\": \"EXAMPLE\",", "")), "MissingField", "profile");

    [Fact]
    public void UnsupportedSchemaVersion() =>
        HasError(RuleConfigJson.Parse(Mutate(ExampleText, "\"schemaVersion\": 1", "\"schemaVersion\": 9")), "UnsupportedSchemaVersion", "schemaVersion");

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    public void EpsilonMustBePositive(string value) =>
        HasError(RuleConfigJson.Parse(Mutate(ExampleText, "\"epsilonMm\": 1E-06", $"\"epsilonMm\": {value}")), "OutOfRange", "geometry.epsilonMm");

    [Fact]
    public void NegativeClearance() =>
        HasError(RuleConfigJson.Parse(Mutate(ExampleText, "\"pipeToObstacleMinMm\": 100", "\"pipeToObstacleMinMm\": -5")),
            "OutOfRange", "clearance.pipeToObstacleMinMm");

    [Fact]
    public void NegativeMinStraight() =>
        HasError(RuleConfigJson.Parse(Mutate(ExampleText, "\"minBetweenBendsMm\": 300", "\"minBetweenBendsMm\": -1")),
            "OutOfRange", "straightRun.minBetweenBendsMm");

    [Fact]
    public void HandwheelRangeMinGreaterThanMax() =>
        HasError(RuleConfigJson.Parse(Mutate(ExampleText, "\"min\": 900", "\"min\": 2000")),
            "OutOfRange", "componentTypes.handValve.handwheel.heightAboveFloorMm.min");

    [Fact]
    public void UnknownSeverityRuleId() =>
        HasError(RuleConfigJson.Parse(Mutate(ExampleText, "\"MinClearance\": \"error\"", "\"MinClearence\": \"error\"")),
            "InvalidValue", "severities.MinClearence");

    [Fact]
    public void InvalidSeverityValue() =>
        HasError(RuleConfigJson.Parse(Mutate(ExampleText, "\"MinClearance\": \"error\"", "\"MinClearance\": \"fatal\"")),
            "InvalidValue", "severities.MinClearance");

    [Fact]
    public void InvalidZonePolicy() =>
        HasError(RuleConfigJson.Parse(Mutate(ExampleText, "\"operation\": \"forbid\"", "\"operation\": \"allow\"")),
            "InvalidValue", "serviceZonePolicy.operation");

    [Fact]
    public void UnknownClearanceZoneType() =>
        HasError(RuleConfigJson.Parse(Mutate(ExampleText, "\"type\": \"cylinder\"", "\"type\": \"cone\"")),
            "InvalidValue", "componentTypes.handValve.handwheel.clearanceZone.type");

    [Fact]
    public void HandwheelMissingParts()
    {
        var r = RuleConfigJson.Parse(Mutate(ExampleText, "\"centerOffsetFromAxisMm\": [0, 0, 250],", ""));
        HasError(r, "MissingField", "componentTypes.handValve.handwheel.centerOffsetFromAxisMm");
    }

    [Fact]
    public void NegativeStraightRunMultiple() =>
        HasError(RuleConfigJson.Parse(Mutate(ExampleText, "\"upstream\": 10", "\"upstream\": -2")),
            "OutOfRange", "componentTypes.flowMeter.straightRunInDiameters.upstream");

    [Fact]
    public void NonPositiveComponentSize() =>
        HasError(RuleConfigJson.Parse(Mutate(ExampleText, "\"lengthMm\": 400", "\"lengthMm\": 0")),
            "OutOfRange", "componentTypes.flowMeter.size.lengthMm");

    [Fact]
    public void UnknownField_IsWarning()
    {
        var r = RuleConfigJson.Parse(Mutate(ExampleText, "\"profile\": \"EXAMPLE\",", "\"profile\": \"EXAMPLE\", \"profil\": 1,"));
        Assert.True(r.Ok, Dump(r));
        HasWarning(r, "UnknownField", "profil");
    }

    [Fact]
    public void Load_MissingFile() =>
        HasError(RuleConfigJson.Load(RepoPaths.Combine("config", "no-such.json")), "FileNotFound", "");
}
