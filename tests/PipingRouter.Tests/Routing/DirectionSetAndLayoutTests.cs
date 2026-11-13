using PipingRouter.Engine.Geometry;
using PipingRouter.Engine.Routing;
using Xunit;

namespace PipingRouter.Tests.Routing;

public class DirectionSetAndLayoutTests
{
    // ------------------------------------------------------------ 方向表
    [Fact]
    public void Axis6_MatchesAxisDirectionOrderAndVectors()
    {
        var set = DirectionSet.Axis6;
        Assert.Equal(6, set.Count);
        Assert.Same(set, DirectionSet.For(DirectionSetKind.Axis6));
        for (int i = 0; i < 6; i++)
        {
            var v = AxisDirections.All[i].ToVector();
            Assert.Equal(v, new Vec3(set.StepX(i), set.StepY(i), set.StepZ(i)));
            Assert.Equal((int)AxisDirections.All[i].Opposite(), set.Opposite(i));
            Assert.Equal(i, set.Opposite(set.Opposite(i)));              // 反方向是對合
            Assert.Equal(0, set.StepX(i) * set.StepX(set.Opposite(i)) + set.StepY(i) * set.StepY(set.Opposite(i)) + set.StepZ(i) * set.StepZ(set.Opposite(i)) + 1);   // 內積 = −1
        }
    }

    [Fact]
    public void Axis6_BendClass_StraightIsZero_EverythingElseIsAQuarterTurn()
    {
        var set = DirectionSet.Axis6;
        for (int a = 0; a < 6; a++)
            for (int b = 0; b < 6; b++)
            {
                if (b == set.Opposite(a)) continue;                      // 回頭不是合法的轉彎，由搜尋另外禁止
                Assert.Equal(a == b ? 0 : 1, set.BendClass(a, b));
                if (a != b) Assert.Equal(0, set.StepX(a) * set.StepX(b) + set.StepY(a) * set.StepY(b) + set.StepZ(a) * set.StepZ(b));   // 確實是 90 度
            }
    }

    // ------------------------------------------------------------ 狀態編碼（含預留的元件序列索引）
    [Theory]
    [InlineData(6, 1, 1)]
    [InlineData(6, 3, 1)]
    [InlineData(6, 4, 3)]       // 日後加入元件序列時 comp 維度 > 1
    [InlineData(18, 2, 2)]      // 日後 45 度方向集合
    public void Encode_IsABijection_OnAllCombinations(int dirs, int minRun, int comps)
    {
        var layout = new StateLayout(dirs, minRun, comps);
        const int cells = 5;
        Assert.Equal((long)dirs * minRun * comps, layout.StatesPerCell);

        var seen = new HashSet<int>();
        for (int linear = 0; linear < cells; linear++)
            for (int d = 0; d < dirs; d++)
                for (int run = 1; run <= minRun; run++)
                    for (int comp = 0; comp < comps; comp++)
                    {
                        int s = layout.Encode(linear, d, run, comp);
                        Assert.True(s >= 0 && s < cells * layout.StatesPerCell);
                        Assert.True(seen.Add(s), "編碼重複");
                        layout.Decode(s, out int l2, out int d2, out int r2, out int c2);
                        Assert.Equal((linear, d, run, comp), (l2, d2, r2, c2));
                    }
        Assert.Equal(cells * layout.StatesPerCell, seen.Count);          // 剛好填滿 [0, cells×每格狀態數)
    }

    [Fact]
    public void FirstPhase_ComponentIndexIsAlwaysZero()
    {
        var layout = new StateLayout(6, 3);
        Assert.Equal(1, layout.ComponentCount);
        Assert.Equal(18, layout.StatesPerCell);
        Assert.Equal(layout.Encode(2, 4, 3, 0), layout.Encode(2, 4, 3));
        layout.Decode(layout.Encode(7, 5, 2), out _, out _, out _, out int comp);
        Assert.Equal(0, comp);
    }

    [Theory]
    [InlineData(0, 1, 1)]
    [InlineData(6, 0, 1)]
    [InlineData(6, 1, 0)]
    public void InvalidLayout_Throws(int dirs, int minRun, int comps)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new StateLayout(dirs, minRun, comps));
    }
}
