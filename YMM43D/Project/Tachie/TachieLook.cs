using System.Numerics;
using YMM43D.Commons;
using YMM43D.Graphics;
using YMM43D.Graphics.Models;
using YMM43D.Project.Model;
using YukkuriMovieMaker.Commons;

namespace YMM43D.Project.Tachie
{
    internal sealed class TachieLook(
        ModelData model,
        Model3DCharacterParameter character,
        Model3DFaceParameter? face,
        TimeSpan time,
        double kuchipaku) : IModelLook
    {
        public ModelData? Model => model;

        public Vector4 Tone => character.Tint.ToVector4();

        public bool IsUnlit => character.IsUnlit;

        public SurfaceGloss GetGloss(in FrameContext time) => SurfaceGloss.None;

        public ModelPaint GetPaint(in FrameContext time)
        {
            var paint = ModelPaint.Default with { Glow = (float)(character.Glow / 100.0) };

            if (character.IsUnlit)
                return paint;

            return paint with
            {
                ToonSteps = character.ToonSteps,
                ToonShift = (float)(character.ToonShift / 100.0),
                ToonFeather = (float)(character.ToonFeather / Model3DCharacterParameter.FeatherSpan),
                Shade = character.ShadeColor.ToVector3(),
                Bump = (float)(character.Bump / 100.0),
                Metallic = (float)(character.Metallic / 100.0),
                Smoothness = (float)(character.Smoothness / 100.0),
                Mirror = (float)(character.Mirror / 100.0),
            };
        }

        public Matrix4x4 GetLocalMatrix(ModelData model, in FrameContext time)
        {
            var bounds = model.Bounds;
            var extent = bounds.Max - bounds.Min;
            var longest = MathF.Max(extent.X, MathF.Max(extent.Y, extent.Z));

            var scale = longest > 1e-9f ? WorldScale.ToWorld((float)character.Size) / longest : 1f;

            return Matrix4x4.CreateTranslation(-bounds.Center)
                 * Matrix4x4.CreateScale(scale)
                 * Rotation3D.ForObject((float)character.RotationX, (float)character.RotationY, (float)character.RotationZ)
                 * Matrix4x4.CreateTranslation(0f, -WorldScale.ToWorld((float)character.OffsetY), 0f);
        }

        public IReadOnlyList<ModelImage?> GetReplacements(ModelData model) => [];

        public IReadOnlyList<(ModelBlend Blend, float Cutoff)?> GetBlends(ModelData model)
            => new (ModelBlend, float)?[model.Materials.Length];

        public IReadOnlyList<Matrix4x4>? GetPose(
            ModelData model, in FrameContext time, in Matrix4x4 world, bool advance)
            => null;

        public IReadOnlyList<float>? GetMorphWeights(ModelData model, in FrameContext time)
            => ModelExpressionWeights.Of(model, Wanted());

        private IEnumerable<(string Name, float Strength)> Wanted()
        {
            yield return (character.MouthExpression, (float)(kuchipaku * character.MouthOpen / 100.0));

            yield return (character.BlinkExpression, Blink());

            if (face is { Expression.Length: > 0 })
                yield return (face.Expression, (float)(face.Strength / 100.0));
        }

        private float Blink()
        {
            if (character.BlinkExpression.Length == 0 || character.BlinkInterval <= 0)
                return 0f;

            var interval = character.BlinkInterval;
            var length = Math.Min(character.BlinkLength, interval);
            var at = time.TotalSeconds % interval;

            if (at >= length)
                return 0f;

            return (float)Math.Sin(at / length * Math.PI);
        }
    }
}
