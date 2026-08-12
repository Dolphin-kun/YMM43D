using System.ComponentModel.DataAnnotations;
using System.Numerics;

namespace Shape3D
{
    internal enum CubeFace
    {
        Front,
        Back,
        Left,
        Right,
        Top,
        Bottom,
    }

    internal static class CubeFaces
    {
        public const int Count = 6;

        // 立方体は 1×1 の板を6枚置いて作る。板1枚ぶんの頂点しか要らず、面ごとに
        // 別の色と画像を渡せる。板は +Z を向いているので、それぞれの向きへ回して
        // 半単位ぶん押し出す。回転は裏表を入れ替えないため、6枚とも外を向く。
        public static Matrix4x4 GetTransform(CubeFace face) => face switch
        {
            CubeFace.Front => Matrix4x4.CreateTranslation(0f, 0f, 0.5f),
            CubeFace.Back => Matrix4x4.CreateRotationY(MathF.PI)
                           * Matrix4x4.CreateTranslation(0f, 0f, -0.5f),
            CubeFace.Left => Matrix4x4.CreateRotationY(-MathF.PI / 2f)
                           * Matrix4x4.CreateTranslation(-0.5f, 0f, 0f),
            CubeFace.Right => Matrix4x4.CreateRotationY(MathF.PI / 2f)
                            * Matrix4x4.CreateTranslation(0.5f, 0f, 0f),
            CubeFace.Top => Matrix4x4.CreateRotationX(-MathF.PI / 2f)
                          * Matrix4x4.CreateTranslation(0f, 0.5f, 0f),
            _ => Matrix4x4.CreateRotationX(MathF.PI / 2f)
               * Matrix4x4.CreateTranslation(0f, -0.5f, 0f),
        };
    }

    public enum CubeFill
    {
        [Display(Name = "単色", Description = "6面すべてを同じ色と画像で塗ります")]
        Solid,

        [Display(Name = "面ごと", Description = "6面それぞれに色と画像を指定します")]
        PerFace,
    }
}
