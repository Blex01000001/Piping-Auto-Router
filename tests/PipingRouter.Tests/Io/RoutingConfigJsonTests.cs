using PipingRouter.Engine.Io;
using PipingRouter.Engine.Routing;
using Xunit;
using static PipingRouter.Tests.Io.IssueAssert;

namespace PipingRouter.Tests.Io;

public class RoutingConfigJsonTests
{
    private static string ExampleText => File.ReadAllText(RepoPaths.Combine("config", "routing.example.json"));

    [Fact]
    public void ExampleFile_LoadsWithoutIssues()
    {
        var r = RoutingConfigJson.Load(RepoPaths.Combine("config", "routing.example.json"));
        NoIssues(r);
        var c = r.Value!;
        Assert.Equal(100, c.VoxelSizeMm);
        Assert.Equal((1.0, 50.0, 20.0), (c.Weights.Length, c.Weights.Bend, c.Weights.ZonePenaltyPerCell));
        Assert.Equal(DirectionSetKind.Axis6, c.Directions);
        Assert.Equal((5_000_000L, 0L), (c.Search.MaxExpandedNodes, c.Search.Seed));
        Assert.True(c.DiagnoseOnFailure);
    }

    [Fact]
    public void RoundTrip_IsStable()
    {
        var c = RoutingConfigJson.Parse(ExampleText).ValueOrThrow();
        string t1 = RoutingConfigJson.Serialize(c);
        var again = RoutingConfigJson.Parse(t1);
        NoIssues(again);
        Assert.Equal(t1, RoutingConfigJson.Serialize(again.Value!));
    }

    [Fact]
    public void Planning_ExampleHasAPenalty_AndRoundTrips()
    {
        var c = RoutingConfigJson.Parse(ExampleText).ValueOrThrow();
        Assert.Equal(1_000_000, c.Planning.FailurePenalty);
        var back = RoutingConfigJson.Parse(RoutingConfigJson.Serialize(c)).ValueOrThrow();
        Assert.Equal(c.Planning, back.Planning);
        var custom = RoutingConfigJson.Parse(Mutate(ExampleText, "\"failurePenalty\": 1000000", "\"failurePenalty\": 2500.5")).ValueOrThrow();
        Assert.Equal(2500.5, RoutingConfigJson.Parse(RoutingConfigJson.Serialize(custom)).ValueOrThrow().Planning.FailurePenalty);
    }

    [Fact]
    public void Planning_IsOptional_WithADefault()
    {
        const string json = """{ "schemaVersion": 1, "voxelSizeMm": 50, "weights": { "length": 1, "bend": 10 } }""";
        Assert.Equal(PlanningSettings.Default, RoutingConfigJson.Parse(json).ValueOrThrow().Planning);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-5")]
    [InlineData("2e12")]
    public void Planning_PenaltyMustBePositiveAndBounded(string v) =>
        HasError(RoutingConfigJson.Parse(Mutate(ExampleText, "\"failurePenalty\": 1000000", $"\"failurePenalty\": {v}")), "OutOfRange", "planning.failurePenalty");

    [Fact]
    public void LargeSeed_RoundTrips()
    {
        var c = RoutingConfigJson.Parse(Mutate(ExampleText, "\"seed\": 0", "\"seed\": 9007199254740993")).ValueOrThrow();
        Assert.Equal(9007199254740993L, c.Search.Seed);
        Assert.Equal(9007199254740993L, RoutingConfigJson.Parse(RoutingConfigJson.Serialize(c)).ValueOrThrow().Search.Seed);
    }

    [Fact]
    public void OptionalSections_UseDefaults()
    {
        const string json = """{ "schemaVersion": 1, "voxelSizeMm": 50, "weights": { "length": 1, "bend": 10 } }""";
        var c = RoutingConfigJson.Parse(json).ValueOrThrow();
        Assert.Equal(0, c.Weights.ZonePenaltyPerCell);
        Assert.Equal(0, c.Search.Seed);
        Assert.True(c.Search.MaxExpandedNodes > 0);
        Assert.True(c.DiagnoseOnFailure);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-100")]
    public void VoxelSizeMustBePositive(string v) =>
        HasError(RoutingConfigJson.Parse(Mutate(ExampleText, "\"voxelSizeMm\": 100", $"\"voxelSizeMm\": {v}")), "OutOfRange", "voxelSizeMm");

    [Fact]
    public void LengthWeightMustBePositive() =>
        HasError(RoutingConfigJson.Parse(Mutate(ExampleText, "\"length\": 1", "\"length\": 0")), "OutOfRange", "weights.length");

    [Fact]
    public void NegativeBendWeight() =>
        HasError(RoutingConfigJson.Parse(Mutate(ExampleText, "\"bend\": 50", "\"bend\": -1")), "OutOfRange", "weights.bend");

    [Fact]
    public void Diag45_IsReservedButNotSupported() =>
        HasError(RoutingConfigJson.Parse(Mutate(ExampleText, "\"axis6\"", "\"axis6+diag45\"")), "NotSupported", "directions");

    [Fact]
    public void UnknownDirectionSet() =>
        HasError(RoutingConfigJson.Parse(Mutate(ExampleText, "\"axis6\"", "\"hex\"")), "InvalidValue", "directions");

    [Fact]
    public void MaxExpandedNodesMustBePositive() =>
        HasError(RoutingConfigJson.Parse(Mutate(ExampleText, "\"maxExpandedNodes\": 5000000", "\"maxExpandedNodes\": 0")),
            "OutOfRange", "search.maxExpandedNodes");

    [Fact]
    public void SeedMustBeInteger() =>
        HasError(RoutingConfigJson.Parse(Mutate(ExampleText, "\"seed\": 0", "\"seed\": 1.5")), "WrongType", "search.seed");

    [Fact]
    public void MissingVoxelSize() =>
        HasError(RoutingConfigJson.Parse(Mutate(ExampleText, "\"voxelSizeMm\": 100,", "")), "MissingField", "voxelSizeMm");
}
