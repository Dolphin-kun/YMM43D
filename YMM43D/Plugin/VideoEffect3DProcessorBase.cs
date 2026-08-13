using System.Numerics;
using Vortice.Direct2D1;
using Vortice.Direct3D11;
using YMM43D.Commons;
using YMM43D.Graphics;
using YMM43D.Scene3D;
using YukkuriMovieMaker.Commons;
using YukkuriMovieMaker.Player.Video;

namespace YMM43D.Plugin
{
    public abstract class VideoEffect3DProcessorBase(VideoEffect3DBase? owner, IGraphicsDevicesAndContext devices)
                : IVideoEffectProcessor, I3DVideoEffect, I3DSizeProvider, I3DLocalTransform, I3DBounds
    {
        private readonly VideoEffect3DBase? owner = owner;
        private readonly D2DTextureBridge textureBridge = new();
        private readonly Output3DRenderer renderer = new();

        private ID2D1Image? output;
        private Vector2 inputSize;
        private Vector2 inputOffset;
        private Matrix4x4? localMatrix;
        private DeviceLease? lease;
        private ID3D11ShaderResourceView? bakedTexture;
        private nint bakedDeviceKey;
        private bool isDisposed;

        protected IGraphicsDevicesAndContext Devices { get; } = devices;

        protected ID2D1Image? Input { get; private set; }

        public EffectDescription? EffectDescription { get; private set; }

        public ID2D1Image Output => output ?? throw new InvalidOperationException(
            "まだ画像が生成されていません。Update を先に呼んでください。");

        public virtual bool RequiresMappedTexture => false;

        public virtual bool ScalesToInputSize => true;

        public abstract void Draw(in Render3DContext render, DrawContext3D item);

        protected abstract WorldBounds GetLocalBounds(in FrameContext itemTime);

        WorldBounds I3DBounds.GetLocalBounds(in FrameContext itemTime) => GetLocalBounds(itemTime);

        public DrawDescription Update(EffectDescription description)
        {
            EffectDescription = description;

            BakeInput();

            var itemTime = FrameContext.FromItem(description);

            var world = ScalesToInputSize && TryGetSize(out var size, out var offset)
                ? WorldScale.CreateSizeMatrix(size, offset + size / 2f)
                : Matrix4x4.Identity;

            ConsumeCamera(description.DrawDescription, ref world);

            localMatrix = world;

            output = renderer.Render(
                Devices, description, GetLocalBounds(itemTime), world, Draw,
                self: (I3DProvider?)owner ?? this,
                placement: ToWorldPlacement(description.DrawDescription));

            return Neutralize(description.DrawDescription);
        }

        private static Matrix4x4 ToWorldPlacement(DrawDescription draw)
        {
            var zoom = draw.Zoom;

            var scale = new Vector3(
                (float)zoom.X,
                (float)zoom.Y,
                (float)(zoom.X + zoom.Y) / 2f);

            var rotation = Rotation3D.ForObject(
                -(float)draw.Rotation.X, -(float)draw.Rotation.Y, -(float)draw.Rotation.Z);

            var translation = Matrix4x4.CreateTranslation(
                WorldScale.ToWorld((float)draw.Draw.X),
                -WorldScale.ToWorld((float)draw.Draw.Y),
                WorldScale.ToWorld((float)draw.Draw.Z));

            return Matrix4x4.CreateScale(scale) * rotation * translation;
        }

        private static DrawDescription Neutralize(DrawDescription draw) => draw with
        {
            Draw = Vector3.Zero,
            CenterPoint = Vector2.Zero,
            Zoom = Vector2.One,
            Rotation = Vector3.Zero,
            Camera = Matrix4x4.Identity,
        };

        private static void ConsumeCamera(DrawDescription draw, ref Matrix4x4 world)
        {
            if (draw.Camera == Matrix4x4.Identity)
                return;

            world *= WorldScale.ToYUpMatrix(draw.Camera);
        }

        public void SetInput(ID2D1Image? input)
        {
            lock (D2DGate.Sync)
                Input = input;
        }

        public void ClearInput()
        {
            lock (D2DGate.Sync)
                Input = null;
        }

        private void BakeInput()
        {
            if (Input is not { } input)
                return;

            var device = (lease ??= GraphicsDevicePool.Acquire()).Device;

            var texture = textureBridge.GetTexture(device, Devices, input, this, out var bounds);

            inputSize = new Vector2(bounds.Right - bounds.Left, bounds.Bottom - bounds.Top);
            inputOffset = new Vector2(bounds.Left, bounds.Top);

            bakedTexture = texture;
            bakedDeviceKey = texture is null ? nint.Zero : device.NativePointer;
        }

        public ID3D11ShaderResourceView? GetTexture(ID3D11Device device)
        {
            lock (D2DGate.Sync)
            {
                if (isDisposed || bakedDeviceKey == nint.Zero)
                    return null;

                return bakedDeviceKey == device.NativePointer ? bakedTexture : null;
            }
        }

        public bool TryGetLocalMatrix(out Matrix4x4 matrix)
        {
            matrix = localMatrix ?? Matrix4x4.Identity;
            return localMatrix.HasValue;
        }

        public bool TryGetSize(out Vector2 size, out Vector2 offset)
        {
            size = inputSize;
            offset = inputOffset;
            return size.X > 0 && size.Y > 0;
        }

        public virtual void Dispose()
        {
            lock (D2DGate.Sync)
            {
                isDisposed = true;
                bakedTexture = null;
                bakedDeviceKey = nint.Zero;
                Input = null;
                output = null;
            }

            owner?.DetachProcessor(this);
            renderer.Dispose();
            textureBridge.Dispose();

            lease?.Dispose();
            lease = null;

            GC.SuppressFinalize(this);
        }
    }
}
