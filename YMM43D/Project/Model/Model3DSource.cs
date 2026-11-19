using YMM43D.Commons;
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
