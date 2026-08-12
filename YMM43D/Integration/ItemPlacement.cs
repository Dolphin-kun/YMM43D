using System.Numerics;
using YMM43D.Scene3D;
using YukkuriMovieMaker.Project.Items;

namespace YMM43D.Integration
{
    /// <summary>
    /// YMM4 のアイテム設定から、3D 空間での配置を組み立てます。
    /// </summary>
    /// <remarks>
    /// 3Dプレビューと、アイテムをまたいだ前後関係を出す処理の両方で使います。
    /// 別々に書くと片方だけ直したときにずれるため、1箇所にまとめています。
    /// </remarks>
    public static class ItemPlacement
    {
        /// <summary>
        /// アイテムの拡大率・回転角・位置を反映した行列を返します。
        /// </summary>
        /// <param name="cameraMatrix">
        /// エフェクトが <c>DrawDescription.Camera</c> に書き込んだ変換。
        /// 無ければ単位行列を渡してください。
        /// </param>
        /// <remarks>
        /// 拡大率も含めます。ここで外すと深度判定だけが拡大前の空間で行われ、
        /// アイテムをまたいだ前後関係が狂います。二重に掛かる分は描画側が相殺します。
        /// </remarks>
        public static Matrix4x4 GetWorldMatrix(
            IVideoItem item,
            in FrameContext time,
            Matrix4x4 cameraMatrix)
        {
            var zoom = Matrix4x4.CreateScale(item.Zoom.GetFloat(time) / 100f);

            // YMM4 の回転は時計回り、3D空間は反時計回りなので符号を反転する。
            var rotation = Matrix4x4.CreateRotationZ(-Rotation3D.ToRadians(item.Rotation.GetFloat(time)));

            // YMM4 の Y 軸は下向き、3D空間は上向き。
            var translation = Matrix4x4.CreateTranslation(
                WorldScale.ToWorld(item.X.GetFloat(time)),
                -WorldScale.ToWorld(item.Y.GetFloat(time)),
                WorldScale.ToWorld(item.Z.GetFloat(time)));

            if (cameraMatrix == Matrix4x4.Identity)
                return zoom * rotation * translation;

            return zoom * rotation * WorldScale.ToYUpMatrix(cameraMatrix) * translation;
        }

        /// <summary>
        /// 同じ配置を、YMM4 が画像に掛ける 2D の形で返します。
        /// </summary>
        /// <remarks>
        /// <see cref="GetWorldMatrix"/> と同じ値から作ります。片方だけを直すと
        /// 3D 側の配置と打ち消しがずれるので、必ず対で使ってください。
        /// </remarks>
        public static ScreenPlacement GetScreenPlacement(IVideoItem item, in FrameContext time)
        {
            var zoom = item.Zoom.GetFloat(time) / 100f;

            return new ScreenPlacement(
                new Vector2(item.X.GetFloat(time), item.Y.GetFloat(time)),
                float.IsFinite(zoom) && zoom > 0f ? zoom : 1f,
                item.Rotation.GetFloat(time),
                item.Z.GetFloat(time));
        }

        /// <summary>
        /// 不透明度に、フェードイン・フェードアウトの効果を掛け合わせます。
        /// </summary>
        public static float GetOpacity(IVideoItem item, in FrameContext time)
        {
            var opacity = item.Opacity.GetFloat(time) / 100f;

            var fadeInFrames = item.FadeIn * time.Fps;
            if (fadeInFrames > 0 && time.Frame < fadeInFrames)
                opacity *= (float)(time.Frame / fadeInFrames);

            var fadeOutFrames = item.FadeOut * time.Fps;
            if (fadeOutFrames > 0 && time.Frame > time.Length - fadeOutFrames)
                opacity *= (float)((time.Length - time.Frame) / fadeOutFrames);

            return opacity;
        }

        /// <summary>
        /// アイテムが指定フレームに存在するかどうかを返します。
        /// </summary>
        public static bool IsAliveAt(IVideoItem item, int frame)
            => frame >= item.Frame && frame < item.Frame + item.Length;
    }
}
