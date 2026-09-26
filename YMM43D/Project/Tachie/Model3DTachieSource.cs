using System.Numerics;
using Vortice.Direct2D1;
using Vortice.Mathematics;
using YMM43D.Commons;
using YMM43D.Graphics.Models;
using YMM43D.Player;
using YMM43D.Project.Model;
using YukkuriMovieMaker.Commons;
using YukkuriMovieMaker.Plugin.Tachie;

namespace YMM43D.Project.Tachie
{
    // 3Dモデルを立ち絵として描く。口パクと表情は YMM4 から渡ってくる。
    internal sealed class Model3DTachieSource(IGraphicsDevicesAndContext devices) : ITachieSource
    {
        // 立ち絵の絵の余白。腕を広げたり口を開けたりしてもはみ出さないだけ広げておく。
        private const float Margin = 1.4f;

        // 見る位置。平行に見るので、遠さは見た目に影響しない。
        private const float Distance = 100f;

        private readonly Renderer3DTo2D renderer = new();
        private readonly ModelPainter painter = new();

        private ID2D1Image? output;

        public ID2D1Image Output => output ??= renderer.RenderEmpty(devices);

        public void Update(
            TimeSpan tachieTime,
            TimeSpan tachieLength,
            TimeSpan faceTime,
            TimeSpan faceLength,
            ITachieCharacterParameter characterParameter,
            ITachieItemParameter itemParameter,
            ITachieFaceParameter faceParameter,
            double kuchipaku)
        {
            if (characterParameter is not Model3DCharacterParameter character
                || ModelLibrary.Find(character.File) is not { } model)
            {
                output = renderer.RenderEmpty(devices);

                return;
            }

            var look = new TachieLook(model, character, faceParameter as Model3DFaceParameter, tachieTime, kuchipaku);

            var side = Math.Max(16, (int)(character.Size * Margin));
            var half = WorldScale.ToWorld(side) / 2f;

            var view = Matrix4x4.CreateLookAt(new Vector3(0f, 0f, Distance), Vector3.Zero, Vector3.UnitY);
            var projection = Matrix4x4.CreateOrthographic(half * 2f, half * 2f, 0.01f, Distance * 2f);

            var context = new DrawContext3D
            {
                World = Matrix4x4.Identity,
                Opacity = 1f,
                Time = new FrameContext(0, 1, 60),
            };

            output = renderer.Render(
                devices,
                side,
                side,
                view,
                projection,
                Vector2.Zero,
                null,
                [],
                new Color4(0f, 0f, 0f, 0f),
                render => painter.Draw(render, context, look));
        }

        public void Dispose()
        {
            painter.Dispose();
            renderer.Dispose();
        }
    }
}
