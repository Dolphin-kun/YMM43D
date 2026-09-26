using System.Numerics;
using Vortice.Direct3D11;
using YMM43D.Commons;
using YMM43D.Graphics;
using YMM43D.Graphics.Materials;
using YMM43D.Graphics.Models;
using YMM43D.Plugin;
using YukkuriMovieMaker.Commons;

namespace YMM43D.Project.Model
{
    internal sealed class Model3DSource(IGraphicsDevicesAndContext devices, Model3DParameter parameter)
        : Shape3DSourceBase(devices)
    {
        private readonly Model3DParameter parameter = parameter;
        private readonly ModelPainter painter = new();

        public override void Draw(in Render3DContext render, DrawContext3D item)
            => painter.Draw(render, item, parameter);

        protected override WorldBounds GetWorldBounds(in FrameContext itemTime)
        {
            if (parameter.Model is not { } model)
                return WorldBounds.Empty;

            // 骨を動かすと手足が基本の姿勢からはみ出すので、そのぶんまで含めて測る。
            var bounds = model.HasBones && parameter.GetPose(model, itemTime) is { } pose
                ? model.PosedBounds(ModelPose.Skin(model, pose))
                : model.Bounds;

            return bounds.Transform(parameter.GetLocalMatrix(model, itemTime));
        }

        public override void Dispose()
        {
            painter.Dispose();
            base.Dispose();
        }

    }
}
