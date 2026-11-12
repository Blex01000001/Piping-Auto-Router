using PipingRouter.Engine;
using PipingRouter.Engine.Geometry;
using PipingRouter.Engine.Io;
using PipingRouter.Engine.Scene;
using Xunit;
using static PipingRouter.Tests.Io.IssueAssert;

namespace PipingRouter.Tests.Io;

public class SceneJsonTests
{
    internal const string FullScene = """
    {
      "schemaVersion": 1,
      "name": "全場景 0.1+0.2",
      "isSynthetic": true,
      "units": "mm",
      "floorZ": 0,
      "bounds": { "min": [0, 0, 0], "max": [10000, 8000, 5000] },
      "obstacles": [
        { "id": "EQ-1", "kind": "equipment", "shape": { "type": "box", "min": [1000, 1000, 0], "max": [2000, 2000, 1500] } },
        { "id": "V-1", "kind": "vessel", "shape": { "type": "cylinder", "baseCenter": [5000, 4000, 0], "radius": 800, "height": 3500, "axis": "Z" } },
        { "id": "S-1", "kind": "steel", "shape": { "type": "box", "min": [0, 0, 3000], "max": [10000, 200, 3200] } }
      ],
      "serviceZones": [
        { "id": "MZ-1", "kind": "maintenance", "shape": { "type": "box", "min": [3000, 3000, 0], "max": [4000, 4000, 2000] } }
      ],
      "lines": [
        {
          "id": "L-1", "outerDiameterMm": 114.3, "insulationThicknessMm": 50,
          "start": { "position": [500, 500, 1000], "direction": "+X" },
          "end": { "position": [9000, 7000, 1000], "direction": "-Y" },
          "componentSequence": ["checkValve", "handValve"]
        }
      ],
      "pipes": [
        {
          "id": "P-1", "lineId": "L-1", "outerDiameterMm": 114.3, "insulationThicknessMm": 50,
          "points": [[500, 500, 1000], [3000, 500, 1000], [3000, 3000, 1000]],
          "components": [
            { "id": "HV-1", "type": "handValve", "alongPolyline": { "segmentIndex": 0, "distanceFromStartMm": 800 } }
          ]
        }
      ]
    }
    """;

    private static string Scene(string extra = "", string head = "\"schemaVersion\":1,\"isSynthetic\":true,") =>
        "{" + head + "\"bounds\":{\"min\":[0,0,0],\"max\":[1000,1000,1000]}" + (extra == "" ? "" : "," + extra) + "}";

    // ---------------------------------------------------------------- 正常讀入
    [Fact]
    public void Minimal_Parses_WithoutIssues()
    {
        var r = SceneJson.Parse(Scene());
        NoIssues(r);
        Assert.Empty(r.Value!.Obstacles);
        Assert.Equal(new Vec3(1000, 1000, 1000), r.Value.Bounds.Max);
        Assert.Equal(0, r.Value.FloorZ);
    }

    [Fact]
    public void FullScene_MapsAllFields()
    {
        var r = SceneJson.Parse(FullScene);
        NoIssues(r);
        var s = r.Value!;
        Assert.Equal("全場景 0.1+0.2", s.Name);
        Assert.True(s.IsSynthetic);

        Assert.Equal(3, s.Obstacles.Count);
        Assert.Equal(ObstacleKind.Equipment, s.Obstacles[0].Kind);
        var box = Assert.IsType<BoxShape>(s.Obstacles[0].Shape);
        Assert.Equal(new Vec3(2000, 2000, 1500), box.Box.Max);

        var cyl = Assert.IsType<CylinderShape>(s.Obstacles[1].Shape).Cylinder;
        Assert.Equal(new Vec3(5000, 4000, 0), cyl.BaseCenter);   // 底面圓心
        Assert.Equal(800, cyl.Radius);
        Assert.Equal(3500, cyl.Height);
        Assert.Equal(Axis.Z, cyl.Axis);

        var zone = Assert.Single(s.ServiceZones);
        Assert.Equal(ServiceZoneKind.Maintenance, zone.Kind);

        var line = Assert.Single(s.Lines);
        Assert.Equal(AxisDirection.PosX, line.Start.Direction);
        Assert.Equal(AxisDirection.NegY, line.End.Direction);
        Assert.Equal(114.3 / 2 + 50, line.CollisionRadiusMm, 9);
        Assert.Equal(new[] { "checkValve", "handValve" }, line.ComponentSequence);

        var pipe = Assert.Single(s.Pipes);
        Assert.Equal("L-1", pipe.LineId);
        Assert.Equal(3, pipe.Points.Count);
        var comp = Assert.Single(pipe.Components);
        Assert.Equal((0, 800.0), (comp.SegmentIndex, comp.DistanceFromStartMm));
    }

    [Fact]
    public void CommentsAndTrailingCommas_AreAccepted()
    {
        var json = """
        {
          // 註解
          "schemaVersion": 1, "isSynthetic": true,
          "bounds": { "min": [0,0,0], "max": [1,1,1], },
        }
        """;
        NoIssues(SceneJson.Parse(json));
    }

    // ---------------------------------------------------------------- 來回一致
    [Fact]
    public void RoundTrip_IsStable()
    {
        var first = SceneJson.Parse(FullScene).ValueOrThrow();
        string text1 = SceneJson.Serialize(first);
        var second = SceneJson.Parse(text1);
        NoIssues(second);
        string text2 = SceneJson.Serialize(second.Value!);
        Assert.Equal(text1, text2);
    }

    [Fact]
    public void Serialize_KeepsChineseUnescaped_AndDoublesExact()
    {
        string json = Mutate(Scene(), "\"isSynthetic\":true,", "\"isSynthetic\":true,\"name\":\"中文名稱\",\"floorZ\":0.30000000000000004,");
        var scene = SceneJson.Parse(json).ValueOrThrow();
        string text = SceneJson.Serialize(scene);
        Assert.Contains("中文名稱", text);
        Assert.Contains("0.30000000000000004", text);
        Assert.Equal(0.1 + 0.2, SceneJson.Parse(text).ValueOrThrow().FloorZ);
    }

    [Fact]
    public void Serialize_IsStrictJson_WithoutBom()
    {
        string text = SceneJson.Serialize(SceneJson.Parse(FullScene).ValueOrThrow());
        using var doc = System.Text.Json.JsonDocument.Parse(text);   // 嚴格解析（無註解、無尾端逗號）
        Assert.Contains("\"baseCenter\": [5000, 4000, 0]", text);    // 向量單行
        Assert.EndsWith("\n", text);
    }

    // ---------------------------------------------------------------- 格式錯誤
    [Theory]
    [InlineData("")]
    [InlineData("{")]
    [InlineData("{\"a\": }")]
    [InlineData("這不是 JSON")]
    public void InvalidJson_ReturnsError_NotException(string json)
    {
        HasError(SceneJson.Parse(json), "InvalidJson", "");
    }

    [Fact]
    public void NullInput_ReturnsError()
    {
        HasError(SceneJson.Parse(null), "InvalidJson", "");
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("null")]
    [InlineData("42")]
    public void RootNotObject_ReturnsWrongType(string json)
    {
        HasError(SceneJson.Parse(json), "WrongType", "");
    }

    [Fact]
    public void DuplicateProperty_IsError()
    {
        string json = "{\"schemaVersion\":1,\"schemaVersion\":1,\"isSynthetic\":true,\"bounds\":{\"min\":[0,0,0],\"max\":[1,1,1]}}";
        HasError(SceneJson.Parse(json), "DuplicateField", "schemaVersion");
    }

    // ---------------------------------------------------------------- 欄位驗證
    [Fact]
    public void MissingSchemaVersion()
    {
        HasError(SceneJson.Parse(Scene(head: "\"isSynthetic\":true,")), "MissingField", "schemaVersion");
    }

    [Fact]
    public void UnsupportedSchemaVersion()
    {
        HasError(SceneJson.Parse(Scene(head: "\"schemaVersion\":2,\"isSynthetic\":true,")),
            "UnsupportedSchemaVersion", "schemaVersion");
    }

    [Fact]
    public void MissingBounds()
    {
        HasError(SceneJson.Parse("{\"schemaVersion\":1,\"isSynthetic\":true}"), "MissingField", "bounds");
    }

    [Fact]
    public void BoundsMinGreaterThanMax()
    {
        string json = Mutate(Scene(), "\"min\":[0,0,0]", "\"min\":[5000,0,0]");
        HasError(SceneJson.Parse(json), "OutOfRange", "bounds.min");
    }

    [Fact]
    public void VectorWrongLength()
    {
        string json = Mutate(Scene(), "\"min\":[0,0,0]", "\"min\":[0,0]");
        HasError(SceneJson.Parse(json), "WrongType", "bounds.min");
    }

    [Fact]
    public void VectorElementNotNumber()
    {
        string json = Mutate(Scene(), "\"min\":[0,0,0]", "\"min\":[0,\"a\",0]");
        HasError(SceneJson.Parse(json), "WrongType", "bounds.min[1]");
    }

    [Fact]
    public void WrongTypeForNumber()
    {
        string json = Mutate(FullScene, "\"radius\": 800", "\"radius\": \"abc\"");
        HasError(SceneJson.Parse(json), "WrongType", "obstacles[1].shape.radius");
    }

    [Fact]
    public void WrongUnits()
    {
        string json = Mutate(Scene(), "\"isSynthetic\":true,", "\"isSynthetic\":true,\"units\":\"m\",");
        HasError(SceneJson.Parse(json), "InvalidValue", "units");
    }

    [Fact]
    public void CylinderRadiusMustBePositive()
    {
        HasError(SceneJson.Parse(Mutate(FullScene, "\"radius\": 800", "\"radius\": 0")), "OutOfRange", "obstacles[1].shape.radius");
        HasError(SceneJson.Parse(Mutate(FullScene, "\"radius\": 800", "\"radius\": -5")), "OutOfRange", "obstacles[1].shape.radius");
    }

    [Fact]
    public void CylinderHeightMustBePositive()
    {
        HasError(SceneJson.Parse(Mutate(FullScene, "\"height\": 3500", "\"height\": 0")), "OutOfRange", "obstacles[1].shape.height");
    }

    [Fact]
    public void CylinderAxisInvalid()
    {
        HasError(SceneJson.Parse(Mutate(FullScene, "\"axis\": \"Z\"", "\"axis\": \"W\"")), "InvalidValue", "obstacles[1].shape.axis");
    }

    [Theory]
    [InlineData("x")]
    [InlineData("y")]
    [InlineData("Z")]
    public void CylinderAxis_CaseInsensitive(string axis)
    {
        // 改成 X/Y 軸向後圓柱會下探到 z<0，產生 OutsideBounds 警告是正確的，這裡只確認軸向被讀懂
        var r = SceneJson.Parse(Mutate(FullScene, "\"axis\": \"Z\"", $"\"axis\": \"{axis}\""));
        Assert.True(r.Ok, Dump(r));
        var cyl = Assert.IsType<CylinderShape>(r.Value!.Obstacles[1].Shape).Cylinder;
        Assert.Equal(Enum.Parse<Axis>(axis, ignoreCase: true), cyl.Axis);
    }

    [Fact]
    public void UnknownShapeType()
    {
        HasError(SceneJson.Parse(Mutate(FullScene, "\"type\": \"cylinder\"", "\"type\": \"sphere\"")),
            "InvalidValue", "obstacles[1].shape.type");
    }

    [Fact]
    public void UnknownObstacleKind()
    {
        HasError(SceneJson.Parse(Mutate(FullScene, "\"steel\"", "\"plasma\"")),
            "InvalidValue", "obstacles[2].kind");
    }

    [Fact]
    public void ServiceZoneMustBeBox()
    {
        string json = Mutate(FullScene,
            "\"shape\": { \"type\": \"box\", \"min\": [3000, 3000, 0], \"max\": [4000, 4000, 2000] }",
            "\"shape\": { \"type\": \"cylinder\", \"baseCenter\": [3500, 3500, 0], \"radius\": 100, \"height\": 100, \"axis\": \"Z\" }");
        HasError(SceneJson.Parse(json), "InvalidValue", "serviceZones[0].shape");
    }

    [Fact]
    public void DuplicateIds_AcrossCollections()
    {
        string json = Mutate(FullScene, "\"id\": \"MZ-1\"", "\"id\": \"EQ-1\"");
        HasError(SceneJson.Parse(json), "DuplicateId", "serviceZones[0].id");
    }

    [Fact]
    public void EmptyId()
    {
        HasError(SceneJson.Parse(Mutate(FullScene, "\"id\": \"EQ-1\"", "\"id\": \"\"")), "InvalidValue", "obstacles[0].id");
    }

    [Fact]
    public void LineDiameterAndInsulation()
    {
        const string head = "\"id\": \"L-1\", \"outerDiameterMm\": 114.3, \"insulationThicknessMm\": 50";
        HasError(SceneJson.Parse(Mutate(FullScene, head, head.Replace("114.3", "0"))), "OutOfRange", "lines[0].outerDiameterMm");
        HasError(SceneJson.Parse(Mutate(FullScene, head, head.Replace("50", "-1"))), "OutOfRange", "lines[0].insulationThicknessMm");
    }

    [Fact]
    public void PortDirectionInvalid()
    {
        HasError(SceneJson.Parse(Mutate(FullScene, "\"direction\": \"+X\"", "\"direction\": \"up\"")),
            "InvalidValue", "lines[0].start.direction");
    }

    [Fact]
    public void PortOutsideBounds_IsError()
    {
        HasError(SceneJson.Parse(Mutate(FullScene, "[9000, 7000, 1000], \"direction\"", "[19000, 7000, 1000], \"direction\"")),
            "OutsideBounds", "lines[0].end.position");
    }

    [Fact]
    public void StartEqualsEnd_IsError()
    {
        string json = Mutate(FullScene, "[9000, 7000, 1000], \"direction\"", "[500, 500, 1000], \"direction\"");
        HasError(SceneJson.Parse(json), "InvalidValue", "lines[0].end");
    }

    [Fact]
    public void PipeLineIdMustExist()
    {
        HasError(SceneJson.Parse(Mutate(FullScene, "\"lineId\": \"L-1\"", "\"lineId\": \"L-999\"")),
            "UnknownReference", "pipes[0].lineId");
    }

    [Fact]
    public void PipeNeedsTwoPoints()
    {
        HasError(SceneJson.Parse(Mutate(FullScene, "[[500, 500, 1000], [3000, 500, 1000], [3000, 3000, 1000]]", "[[500, 500, 1000]]")),
            "OutOfRange", "pipes[0].points");
    }

    [Fact]
    public void PipeConsecutiveDuplicatePoints()
    {
        HasError(SceneJson.Parse(Mutate(FullScene, "[3000, 500, 1000], [3000, 3000, 1000]", "[500, 500, 1000], [3000, 3000, 1000]")),
            "InvalidValue", "pipes[0].points[1]");
    }

    [Fact]
    public void ComponentBeyondSegmentLength()
    {
        HasError(SceneJson.Parse(Mutate(FullScene, "\"distanceFromStartMm\": 800", "\"distanceFromStartMm\": 2600")),
            "OutOfRange", "pipes[0].components[0].alongPolyline.distanceFromStartMm");
    }

    [Fact]
    public void ComponentAtExactSegmentEnd_IsAllowed()
    {
        NoIssues(SceneJson.Parse(Mutate(FullScene, "\"distanceFromStartMm\": 800", "\"distanceFromStartMm\": 2500")));
    }

    [Fact]
    public void ComponentSegmentIndexOutOfRange()
    {
        HasError(SceneJson.Parse(Mutate(FullScene, "\"segmentIndex\": 0", "\"segmentIndex\": 2")),
            "OutOfRange", "pipes[0].components[0].alongPolyline.segmentIndex");
    }

    // ---------------------------------------------------------------- 一次回報多個問題
    [Fact]
    public void MultipleErrors_AreAllReported()
    {
        string json = FullScene;
        json = Mutate(json, "\"radius\": 800", "\"radius\": -1");
        json = Mutate(json, "\"id\": \"MZ-1\"", "\"id\": \"EQ-1\"");
        json = Mutate(json, "\"direction\": \"+X\"", "\"direction\": \"up\"");
        json = Mutate(json, "\"lineId\": \"L-1\"", "\"lineId\": \"nope\"");
        var r = SceneJson.Parse(json);
        HasError(r, "OutOfRange", "obstacles[1].shape.radius");
        HasError(r, "DuplicateId", "serviceZones[0].id");
        HasError(r, "InvalidValue", "lines[0].start.direction");
        HasError(r, "UnknownReference", "pipes[0].lineId");
        Assert.True(r.Errors.Count() >= 4);
    }

    // ---------------------------------------------------------------- 警告：不影響結果
    [Fact]
    public void UnknownField_IsWarning_AndStillOk()
    {
        string json = Mutate(FullScene, "\"floorZ\": 0,", "\"floorZ\": 0, \"floorz\": 5,");
        var r = SceneJson.Parse(json);
        Assert.True(r.Ok, Dump(r));
        HasWarning(r, "UnknownField", "floorz");
    }

    [Fact]
    public void UnknownNestedField_IsWarning()
    {
        string json = Mutate(FullScene, "\"radius\": 800,", "\"radius\": 800, \"raduis\": 1,");
        HasWarning(SceneJson.Parse(json), "UnknownField", "obstacles[1].shape.raduis");
    }

    [Fact]
    public void MissingSyntheticFlag_IsWarning()
    {
        var r = SceneJson.Parse(Scene(head: "\"schemaVersion\":1,"));
        Assert.True(r.Ok, Dump(r));
        HasWarning(r, "MissingSyntheticFlag", "isSynthetic");
    }

    [Fact]
    public void ObstacleOutsideBounds_IsWarning()
    {
        string json = Mutate(FullScene, "\"max\": [2000, 2000, 1500]", "\"max\": [20000, 2000, 1500]");
        var r = SceneJson.Parse(json);
        Assert.True(r.Ok, Dump(r));
        HasWarning(r, "OutsideBounds", "obstacles[0].shape");
    }

    [Fact]
    public void NonAxialPipeSegment_IsWarning()
    {
        string json = Mutate(FullScene, "[3000, 500, 1000], [3000, 3000, 1000]", "[3000, 500, 1000], [3500, 3000, 1000]");
        var r = SceneJson.Parse(json);
        Assert.True(r.Ok, Dump(r));
        HasWarning(r, "NonAxialSegment", "pipes[0].points[1]");
    }

    [Fact]
    public void ValueOrThrow_ListsErrorsWithPaths()
    {
        var r = SceneJson.Parse(Mutate(FullScene, "\"radius\": 800", "\"radius\": 0"));
        var ex = Assert.Throws<InvalidDataException>(() => r.ValueOrThrow());
        Assert.Contains("obstacles[1].shape.radius", ex.Message);
    }

    // ---------------------------------------------------------------- 檔案
    [Fact]
    public void Load_MissingFile_ReturnsFileNotFound()
    {
        string path = Path.Combine(Path.GetTempPath(), "piping-test-" + Guid.NewGuid().ToString("N"), "nope.json");
        HasError(SceneJson.Load(path), "FileNotFound", "");
    }

    [Fact]
    public void SaveThenLoad_RoundTrips_InNewDirectory()
    {
        string dir = Path.Combine(Path.GetTempPath(), "piping-test-" + Guid.NewGuid().ToString("N"), "nested");
        string path = Path.Combine(dir, "scene.json");
        try
        {
            var scene = SceneJson.Parse(FullScene).ValueOrThrow();
            SceneJson.Save(path, scene);
            var loaded = SceneJson.Load(path);
            NoIssues(loaded);
            Assert.Equal(SceneJson.Serialize(scene), SceneJson.Serialize(loaded.Value!));
            Assert.False(File.ReadAllBytes(path).Take(3).SequenceEqual(new byte[] { 0xEF, 0xBB, 0xBF }), "不應有 BOM");
        }
        finally
        {
            var root = Path.GetDirectoryName(dir)!;
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }
}
