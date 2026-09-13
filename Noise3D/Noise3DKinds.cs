using System.ComponentModel.DataAnnotations;

namespace Noise3D
{
    public enum Noise3DType
    {
        [Display(Name = "バリュー", Description = "格子点の値をなめらかにつないだノイズ")]
        Value,

        [Display(Name = "ランダム", Description = "細かい粒がばらばらに並ぶノイズ。フラクタルは使いません")]
        Random,

        [Display(Name = "ブロック", Description = "立方体のかたまりごとに濃さが変わるノイズ")]
        Block,

        [Display(Name = "パーリン", Description = "なめらかにうねるノイズ")]
        Perlin,

        [Display(Name = "セルラー", Description = "泡のような、点からの距離で決まるノイズ")]
        Cellular,

        [Display(Name = "ボロノイ", Description = "区画ごとに同じ濃さになるノイズ")]
        Voronoi,

        [Display(Name = "シンプレックス", Description = "パーリンより方向のくせが少ないノイズ")]
        Simplex,

        [Display(Name = "マーブル", Description = "しま模様をノイズで歪ませたマーブル模様")]
        Marble,
    }

    public enum Noise3DFractalMode
    {
        [Display(Name = "通常")]
        Normal,

        [Display(Name = "乱流")]
        Turbulence,

        [Display(Name = "リッジ")]
        Ridged,
    }
}
