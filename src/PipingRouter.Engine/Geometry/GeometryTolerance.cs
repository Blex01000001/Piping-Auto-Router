namespace PipingRouter.Engine.Geometry;

/// <summary>
/// 幾何運算的預設容差（mm）。相交判斷採「閉集合」語意：
/// 距離 ≤ 容差即視為相交（含剛好相切／接觸）。
/// 規則層的「剛好等於最小淨距算通過」另以距離 ≥ 限值 − epsilon 判斷，epsilon 來自設定檔。
/// </summary>
public static class GeometryTolerance
{
    public const double Default = 1e-9;
}
