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
        /// 拡大率も含めます。出力経路では YMM4 が出来上がった画像にも拡大を掛けますが、
        /// 二重にならないよう描画側が縮尺で相殺します。ここで外してしまうと、
        /// 深度判定だけが拡大前の空間で行われ、アイテムをまたいだ前後関係が狂います。
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
