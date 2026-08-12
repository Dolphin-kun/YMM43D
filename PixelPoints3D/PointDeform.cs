using System.Numerics;
using YMM43D.Scene3D;
using YukkuriMovieMaker.Commons;

namespace PixelPoints3D
{
    /// <summary>
    /// 点の並びを三次元的に歪ませる設定を、シェーダーが使える形に直したもの。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 「強さ」は種類ごとに意味が違います（波なら振幅、ねじれなら端での角度、
    /// 球に巻くなら移し具合）。換算はここで済ませてしまい、シェーダー側は
    /// ワールド単位・ラジアン・比率という素直な値だけを見ます。
    /// </para>
    /// <para>
    /// 変形は格子の位置にだけ掛かります。ばらつきはそのあとに足すので、
    /// 曲げても散らばり方は変わりません。
    /// </para>
    /// </remarks>
    internal readonly record struct PointDeform(
        DeformKind Kind, Vector3 Axis, float Amount, float Period, float Phase)
    {
        public static PointDeform None => new(DeformKind.None, Vector3.UnitY, 0f, 1f, 0f);

        /// <param name="extent">格子が占める大きさ（ワールド単位）。</param>
        public static PointDeform Create(
            PixelPoints3DEffect effect, in FrameContext time, Vector3 extent)
        {
            var kind = effect.DeformKind;
            if (kind == DeformKind.None)
                return None;

            var axis = effect.DeformAxis switch
            {
                DeformAxis.X => Vector3.UnitX,
                DeformAxis.Z => Vector3.UnitZ,
                _ => Vector3.UnitY,
            };

            var strength = effect.DeformAmount.GetFloat(time) / 100f;

            var amount = kind switch
            {
                // 端まで進むと 180 度まわる。
                DeformKind.Twist => strength * MathF.PI,

                // 完全に球へ移しきるのが 100%。
                DeformKind.Sphere => Math.Clamp(strength, 0f, 1f),

                // 短いほうの辺の半分を 100% とする。絵の大きさに応じて効き方が
                // 変わらないので、画像を差し替えても見た目が保たれる。
                _ => strength * MathF.Min(extent.X, extent.Y) / 2f,
            };

            return new PointDeform(
                kind,
                axis,
                amount,
                MathF.Max(WorldScale.ToWorld(effect.DeformPeriod.GetFloat(time)), 1e-4f),
                Rotation3D.ToRadians(effect.DeformPhase.GetFloat(time)));
        }

        /// <summary>
        /// 変形で点がはみ出す分を、格子の半分の大きさに足します。
        /// </summary>
        /// <remarks>
        /// 出力画像の大きさはここから決まります。足りないと絵の端が切れ、
        /// 多すぎると無駄に大きな画像を確保します。切れるほうが困るので、
        /// 迷ったら大きめに見積もっています。
        /// </remarks>
        public Vector3 Expand(Vector3 half) => Kind switch
        {
            DeformKind.None => half,

            // 押し引きする向きは1つだけだが、余裕を見て全方向に足す。
            DeformKind.Wave => half + new Vector3(MathF.Abs(Amount)),

            // 軸のまわりに回るので、軸に垂直な向きは角まで届く。
            DeformKind.Twist => Along(half) + Across(Vector3.One) * Across(half).Length(),

            DeformKind.Bulge => half + Axis * MathF.Abs(Amount),

            // 球に移ると、元の並びの外へ出ることがある。半径は
            // 「軸に垂直な半分の大きさ」＋「奥行きの層のぶん」まで。
            _ => Vector3.Max(half, new Vector3(SumOf(Across(half)))),
        };

        /// <summary>軸に沿った成分。</summary>
        private Vector3 Along(Vector3 v) => Axis * Vector3.Dot(Axis, v);

        /// <summary>軸に垂直な成分。</summary>
        private Vector3 Across(Vector3 v) => v - Along(v);

        private static float SumOf(Vector3 v) => v.X + v.Y + v.Z;
    }
}
