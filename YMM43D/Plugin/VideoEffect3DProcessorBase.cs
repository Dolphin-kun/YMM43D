using System.Numerics;
using Vortice.Direct2D1;
using Vortice.Direct3D11;
using YMM43D.Player;
using YMM43D.Graphics;
using YMM43D.Commons;
using YukkuriMovieMaker.Commons;
using YukkuriMovieMaker.Player.Video;

namespace YMM43D.Plugin
{
    public abstract class VideoEffect3DProcessorBase(VideoEffect3DBase? owner, IGraphicsDevicesAndContext devices)
                : IVideoEffectProcessor, I3DVideoEffect, I3DSizeProvider, I3DLocalTransform, I3DBounds, I3DPlacedInstance
    {
        private readonly VideoEffect3DBase? owner = owner;
        private readonly D2DTextureBridge textureBridge = new();
        private readonly Output3DRenderer renderer = new();

        private ID2D1Image? output;
        private Vector2 inputSize;
        private Vector2 inputOffset;
        private Matrix4x4? localMatrix;
        private Matrix4x4? placement;
        private DeviceLease? lease;
        private ID3D11ShaderResourceView? bakedTexture;
        private nint bakedDeviceKey;
        private bool isDisposed;

        private VideoEffect3DProcessorBase? groupMemberProcessor;

        protected IGraphicsDevicesAndContext Devices { get; } = devices;

        // グループ制御に付けたエフェクトは、中のアイテム 1 つずつに掛かる。
        // そのとき中のアイテムが 3D を持っているなら、絵を板にせずその 3D をそのまま使う。
        protected virtual bool KeepsGroupMemberSolid => false;

        // 中に 3D が無いときも、グループ制御の中心を軸にした親として掛けるか。
        protected virtual bool ParentsGroupMembers => false;

        // グループ制御の中心を軸にして掛ける親の行列。
        protected virtual Matrix4x4 GetGroupParentMatrix(in FrameContext time) => Matrix4x4.Identity;

        // グループ制御に付けたエフェクトか。
        protected bool IsGroupEffect { get; private set; }

        // グループ制御の中のアイテムが持っている 3D の中身。
        protected I3DProvider? GroupMemberSolid { get; private set; }

        // その中身がこのプラグインのエフェクトなら、その処理側。配置を引き継ぐのに使う。
        protected VideoEffect3DProcessorBase? GroupMemberProcessor => groupMemberProcessor;

        internal IGraphicsDevicesAndContext SourceDevices => Devices;

        protected ID2D1Image? Input { get; private set; }

        public EffectDescription? EffectDescription { get; private set; }

        public ID2D1Image Output => output ?? throw new InvalidOperationException(
            "まだ画像が生成されていません。Update を先に呼んでください。");

        public virtual bool RequiresMappedTexture => false;

        public virtual bool ScalesToInputSize => true;

        public abstract void Draw(in Render3DContext render, DrawContext3D item);

        protected abstract WorldBounds GetLocalBounds(in FrameContext itemTime);

        WorldBounds I3DBounds.GetLocalBounds(in FrameContext itemTime) => Bounds(itemTime);

        // 中の 3D をそのまま描くときは、占める範囲もその中身に合わせる。
        // 複製してばらまくように自分で範囲を決めるものは、false にして GetLocalBounds を使う。
        protected virtual bool UsesGroupMemberBounds => true;

        private WorldBounds Bounds(in FrameContext itemTime)
            => UsesGroupMemberBounds && GroupMemberSolid is I3DBounds bounded
                ? bounded.GetLocalBounds(itemTime)
                : GetLocalBounds(itemTime);

        public DrawDescription Update(EffectDescription effectDescription)
        {
            EffectDescription = effectDescription;
            owner?.ReportInput(this, effectDescription.Layer, effectDescription.InputIndex, effectDescription.InputCount);

            BakeInput();
            FindGroupMemberSolid(effectDescription);
            OnUpdating(effectDescription);

            var itemTime = FrameContext.FromItem(effectDescription);

            Matrix4x4 world;

            if (TryInheritTransform(out var inheritedLocal, out var inheritedPlacement))
            {
                world = inheritedLocal;
                placement = inheritedPlacement;
            }
            else
            {
                world = ScalesToInputSize && TryGetSize(out var size, out var offset)
                    ? WorldScale.CreateSizeMatrix(size, offset + size / 2f)
                    : Matrix4x4.Identity;

                ConsumeCamera(effectDescription.DrawDescription, ref world);

                placement = ItemPlacement.GetWorldMatrix(effectDescription.DrawDescription);
            }

            localMatrix = world;

            output = renderer.Render(
                Devices, effectDescription, Bounds(itemTime), world, Draw,
                out var imageReach,
                self: (I3DProvider?)owner ?? this,
                placement: placement);

            SceneRevision.Advance();

            return Neutralize(effectDescription.DrawDescription, imageReach);
        }

        protected virtual void OnUpdating(EffectDescription effectDescription)
        {
        }

        protected virtual bool TryInheritTransform(out Matrix4x4 local, out Matrix4x4 world)
        {
            if (IsGroupEffect
                && (GroupMemberSolid is not null || ParentsGroupMembers)
                && EffectDescription is { } description)
            {
                var parent = GetGroupParentMatrix(FrameContext.FromItem(description));

                if (groupMemberProcessor is { } source
                    && source.TryGetLocalMatrix(out local)
                    && source.TryGetPlacement(out var sourcePlacement))
                {
                    world = sourcePlacement * parent;
                    return true;
                }

                local = Matrix4x4.Identity;
                world = ItemPlacement.GetWorldMatrix(description.DrawDescription) * parent;
                return true;
            }

            local = Matrix4x4.Identity;
            world = Matrix4x4.Identity;
            return false;
        }

        private void FindGroupMemberSolid(EffectDescription description)
        {
            IsGroupEffect = false;
            GroupMemberSolid = null;
            groupMemberProcessor = null;

            if (!KeepsGroupMemberSolid
                || owner is null
                || SceneDepthCollector.FindOwner(description) is not { } item
                || !GroupEffectSource.IsOnGroup(item, owner))
            {
                return;
            }

            IsGroupEffect = true;
            GroupMemberSolid = GroupEffectSource.Find(item, description, owner, Devices, this);
            groupMemberProcessor = GroupMemberSolid as VideoEffect3DProcessorBase;
        }

        // グループ制御の中のアイテムが持つ 3D を、そのまま描く。
        protected bool TryDrawGroupMemberSolid(in Render3DContext render, in DrawContext3D item)
        {
            if (GroupMemberSolid is not { } source)
                return false;

            source.Draw(render, new DrawContext3D
            {
                World = item.World,
                Opacity = item.Opacity,
                Blend = item.Blend,
                IsAlwaysOnTop = item.IsAlwaysOnTop,
                DepthOnly = item.DepthOnly,
                Time = item.Time,
            });

            return true;
        }

        private static DrawDescription Neutralize(DrawDescription draw, float imageReach) => draw with
        {
            Draw = Vector3.Zero,
            CenterPoint = Vector2.Zero,
            Zoom = Vector2.One,
            Rotation = Vector3.Zero,
            Camera = Matrix4x4.Identity,
            PerspectiveDistance =
                ScreenPlacement.GetFlatPerspectiveDistance(draw.PerspectiveDistance, imageReach),
        };

        private static void ConsumeCamera(DrawDescription draw, ref Matrix4x4 world)
            => world = ItemPlacement.WithCamera(world, draw.Camera);

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

        public bool TryGetPlacement(out Matrix4x4 world)
        {
            world = placement ?? Matrix4x4.Identity;
            return placement.HasValue;
        }

        public virtual bool TryGetSize(out Vector2 size, out Vector2 offset)
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
