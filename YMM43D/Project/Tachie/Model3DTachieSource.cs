using System.Numerics;
using Vortice.Direct2D1;
using Vortice.Direct2D1.Effects;
using Vortice.Mathematics;
using YMM43D.Commons;
using YMM43D.Graphics.Models;
using YMM43D.Player;
using YMM43D.Plugin;
using YMM43D.Project.Model;
using YukkuriMovieMaker.Commons;
using YukkuriMovieMaker.Player.Video;
using YukkuriMovieMaker.Plugin.Tachie;
using YukkuriMovieMaker.Plugin.Voice;

namespace YMM43D.Project.Tachie
{
    internal sealed class Model3DTachieSource(IGraphicsDevicesAndContext devices)
        : ITachieSource2, I3DProvider, I3DBounds, I3DAlive
    {
        private const float Margin = 1.4f;

        private const float Distance = 100f;

        private const int MaxSide = 4096;

        private const int NominalFps = 60;

        private readonly Renderer3DTo2D renderer = new();
        private readonly Output3DRenderer placed = new();
        private readonly ModelPainter painter = new();
        private readonly AffineTransform2D holder = new(devices.DeviceContext);
        private readonly MouthMotion mouth = new();
        private readonly ModelSwing swing = new();

        private ID2D1Image? face;
        private ID2D1Image? shown;
        private TachieLook? look;
        private object? registered;
        private bool isDisposed;

        public bool IsAlive => !isDisposed;

        public bool RequiresMappedTexture => false;

        public ID2D1Image Output
        {
            get
            {
                if (shown is null)
                    Show(renderer.RenderEmpty(devices));

                return face ??= holder.Output;
            }
        }

        public void Update(TachieSourceDescription description)
        {
            var tachie = description.Tachie;

            Build(
                description,
                description.ItemPosition.Frame,
                description.FPS,
                description.ItemPosition.Time,
                tachie.ItemParameter as Model3DItemParameter,
                tachie.CharacterParameter,
                [.. tachie.Faces
                    .OrderBy(one => one.Layer)
                    .Select(one => one.FaceParameter)
                    .OfType<Model3DFaceParameter>()],
                description.MouthShape,
                description.VoiceVolume);
        }

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
            Build(
                null,
                (int)(tachieTime.TotalSeconds * NominalFps),
                NominalFps,
                tachieTime,
                itemParameter as Model3DItemParameter,
                characterParameter,
                faceParameter is Model3DFaceParameter one ? [one] : [],
                kuchipaku > 0.0 ? MouthShape.A : MouthShape.Silent,
                kuchipaku);
        }

        private void Build(
            TachieSourceDescription? description,
            int frame,
            int fps,
            TimeSpan tachieTime,
            Model3DItemParameter? item,
            ITachieCharacterParameter characterParameter,
            IReadOnlyList<Model3DFaceParameter> faces,
            MouthShape shape,
            double voice)
        {
            if (characterParameter is not Model3DCharacterParameter character
                || ModelLibrary.Find(character.File) is not { } model)
            {
                look = null;
                Show(renderer.RenderEmpty(devices));

                return;
            }

            mouth.Advance(
                shape, voice, character.MouthOpen / 100.0, character.MouthSmooth / 1000.0, frame, fps);

            look = new TachieLook(model, character, faces, item, swing, tachieTime, mouth.Weights);

            if (item is not null)
                item.ModelFile = character.File;

            Remember(character);

            Show(description is not null ? InScene(description) : Alone(character));
        }

        private ID2D1Image InScene(TachieSourceDescription description)
        {
            var image = placed.Render(
                devices,
                description,
                Bounds(FrameContext.FromItem(description)),
                Matrix4x4.Identity,
                Draw,
                out _,
                self: this,
                hostAppliesPlacement: true);

            SceneRevision.Advance();

            return image;
        }

        private ID2D1Image Alone(Model3DCharacterParameter character)
        {
            var side = Math.Clamp((int)(character.Size * Margin), 16, MaxSide);
            var half = WorldScale.ToWorld(side) / 2f;

            var view = Matrix4x4.CreateLookAt(new Vector3(0f, 0f, Distance), Vector3.Zero, Vector3.UnitY);
            var projection = Matrix4x4.CreateOrthographic(half * 2f, half * 2f, 0.01f, Distance * 2f);

            var item = new DrawContext3D
            {
                World = Matrix4x4.Identity,
                Opacity = 1f,
                Time = new FrameContext(0, 1, 60),
            };

            try
            {
                return renderer.Render(
                    devices,
                    side,
                    side,
                    view,
                    projection,
                    new Vector2(-side / 2f, -side / 2f),
                    null,
                    [],
                    new Color4(0f, 0f, 0f, 0f),
                    render => Draw(render, item));
            }
            catch (Exception)
            {
                return renderer.RenderEmpty(devices);
            }
        }

        public void Draw(in Render3DContext render, DrawContext3D item)
        {
            if (look is not null)
                painter.Draw(render, item, look);
        }

        WorldBounds I3DBounds.GetLocalBounds(in FrameContext itemTime) => Bounds(itemTime);

        private WorldBounds Bounds(in FrameContext itemTime)
        {
            if (look is not { Model: { } model })
                return WorldBounds.Empty;

            var bounds = model.HasBones && look.GetPose(model, itemTime) is { } pose
                ? model.PosedBounds(ModelPose.Skin(model, pose))
                : model.Bounds;

            return bounds.Transform(look.GetLocalMatrix(model, itemTime));
        }

        private void Remember(Model3DCharacterParameter character)
        {
            if (ReferenceEquals(registered, character))
                return;

            if (registered is not null)
                Provider3DRegistry.Unregister(registered, this);

            Provider3DRegistry.Register(character, this, devices);
            registered = character;
        }

        private void Show(ID2D1Image image)
        {
            shown = image;
            holder.SetInput(0, image, true);
        }

        public void Dispose()
        {
            isDisposed = true;

            if (registered is not null)
            {
                Provider3DRegistry.Unregister(registered, this);
                registered = null;
            }

            face?.Dispose();
            face = null;
            shown = null;
            look = null;
            holder.Dispose();
            painter.Dispose();
            placed.Dispose();
            renderer.Dispose();
        }
    }
}
