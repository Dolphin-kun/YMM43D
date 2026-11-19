using System.Numerics;
using YMM43D.Commons;
using YMM43D.Graphics.Models;
using YMM43D.Project.Model;
using YukkuriMovieMaker.Commons;
using YukkuriMovieMaker.Plugin.Voice;

namespace YMM43D.Project.Tachie
{
    internal sealed class TachieLook(
        ModelData model,
        Model3DCharacterParameter character,
        IReadOnlyList<Model3DFaceParameter> faces,
        Model3DItemParameter? item,
        ModelSwing? swing,
        TimeSpan time,
        IReadOnlyList<float> mouth) : IModelLook
    {
        private static readonly string[] OpenNames = ["A", "aa", "あ", "jawOpen"];
        private static readonly string[] BlinkNames = ["Blink", "まばたき"];
        private static readonly string[] BlinkSides = ["eyeBlinkLeft", "eyeBlinkRight"];

        private Matrix4x4[]? baked;
        private int bakedFrame = int.MinValue;

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
            var pose = TachiePose.Of(item, time);
            var bounds = model.Bounds;
            var extent = bounds.Max - bounds.Min;
            var longest = MathF.Max(extent.X, MathF.Max(extent.Y, extent.Z));

            var scale = longest > 1e-9f ? WorldScale.ToWorld((float)character.Size) / longest : 1f;
            var turn = Rotation3D.ForObject((float)character.RotationX + pose.RotationX, (float)character.RotationY + pose.RotationY, (float)character.RotationZ + pose.RotationZ);

            return Matrix4x4.CreateTranslation(-bounds.Center)
                 * Matrix4x4.CreateScale(scale)
                 * turn
                 * Matrix4x4.CreateTranslation(0f, -WorldScale.ToWorld((float)character.OffsetY), 0f);
        }

        public IReadOnlyList<ModelImage?> GetReplacements(ModelData model) => [];

        public IReadOnlyList<(ModelBlend Blend, float Cutoff)?> GetBlends(ModelData model) => new (ModelBlend, float)?[model.Materials.Length];

        public IReadOnlyList<Matrix4x4>? GetPose(ModelData model, in FrameContext time, in Matrix4x4 world, bool advance)
            => Posed(model, time, advance ? world : null);

        public IReadOnlyList<Matrix4x4>? GetPose(ModelData model, in FrameContext time) => Posed(model, time, null);

        private Matrix4x4[]? Posed(ModelData model, in FrameContext time, Matrix4x4? world)
        {
            if (!model.HasBones)
                return null;

            if (bakedFrame != time.Frame)
            {
                bakedFrame = time.Frame;
                baked = item is null
                    ? null
                    : ModelMmd.Settle(model, BonePoses.Apply(model, item.Bones, time, item.Sample(model, time)));
            }

            return character.Swings && swing is not null
                ? swing.Apply(model, item?.Bones ?? [], baked, time, world, (float)(character.Softness / 100.0), (float)(character.Sway / 100.0))
                : baked;
        }

        public IReadOnlyList<float>? GetMorphWeights(ModelData model, in FrameContext time) => ModelExpressionWeights.Of(model, Wanted());

        private IEnumerable<(string Name, float Strength)> Wanted()
        {
            foreach (var one in Mouth())
                yield return one;

            var blink = Blink();

            foreach (var name in BlinkTargets())
                yield return (name, blink);

            foreach (var one in faces)
            {
                if (one.Expression.Length > 0)
                    yield return (one.Expression, (float)(one.Strength / 100.0));
            }
        }

        private IEnumerable<(string Name, float Strength)> Mouth()
        {
            for (var slot = 0; slot < mouth.Count; slot++)
            {
                if (mouth[slot] <= 0f)
                    continue;

                var name = Vowel(MouthMotion.Of(slot));

                if (name.Length == 0 || Shares(name, slot))
                    continue;

                yield return (name, Strongest(name, slot));
            }
        }

        private bool Shares(string name, int slot)
        {
            for (var other = 0; other < slot; other++)
            {
                if (mouth[other] > 0f && Vowel(MouthMotion.Of(other)) == name)
                    return true;
            }

            return false;
        }

        private float Strongest(string name, int slot)
        {
            var best = mouth[slot];

            for (var other = slot + 1; other < mouth.Count; other++)
            {
                if (mouth[other] > best && Vowel(MouthMotion.Of(other)) == name)
                    best = mouth[other];
            }

            return best;
        }

        private string Vowel(MouthShape shape)
        {
            var wanted = shape switch
            {
                MouthShape.I => (string[])["I", "ih", "い"],
                MouthShape.U => ["U", "ou", "う"],
                MouthShape.E => ["E", "ee", "え"],
                MouthShape.O => ["O", "oh", "お"],
                _ => [],
            };

            foreach (var name in wanted)
            {
                if (ModelExpressionWeights.Has(model, name))
                    return name;
            }

            return Open();
        }

        private string Open()
        {
            if (ModelExpressionWeights.Has(model, character.MouthExpression))
                return character.MouthExpression;

            return OpenNames.FirstOrDefault(name => ModelExpressionWeights.Has(model, name)) ?? character.MouthExpression;
        }

        private IEnumerable<string> BlinkTargets()
        {
            if (character.BlinkExpression.Length == 0)
                return [];

            if (ModelExpressionWeights.Has(model, character.BlinkExpression))
                return [character.BlinkExpression];

            if (BlinkNames.FirstOrDefault(name => ModelExpressionWeights.Has(model, name)) is { } found)
                return [found];

            return BlinkSides.Where(name => ModelExpressionWeights.Has(model, name));
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
