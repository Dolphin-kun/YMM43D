using System.Numerics;
using Vortice.Direct3D11;
using YMM43D.Graphics;
using YMM43D.Graphics.Meshes;
using YMM43D.Integration;
using YMM43D.Plugin;
using YMM43D.Scene3D;
using YukkuriMovieMaker.Commons;
using YukkuriMovieMaker.Plugin;

namespace Shape3D
{
    internal sealed class Shape3DSource : Shape3DSourceBase
    {
        private readonly Shape3DParameter parameter;
        private readonly DeviceResourceCache<RenderPipeline<CubeConstants>> pipelines;
        private readonly D2DTextureBridge textureBridge = new();
        private readonly Dictionary<string, IImageFileSource> images = [];

        private DeviceLease? lease;
        private FaceTextures textures = FaceTextures.None;

        public Shape3DSource(IGraphicsDevicesAndContext devices, Shape3DParameter parameter) : base(devices)
        {
            this.parameter = parameter;

            // 立方体は 1×1 の板1枚を6回描いて作る。面ごとに色も画像も変えられて、
            // 画像を貼るときに UV が面ごとに揃うのが利点。
            pipelines = new DeviceResourceCache<RenderPipeline<CubeConstants>>(
                device => new RenderPipeline<CubeConstants>(
                    device,
                    new PlaneMesh(device),
                    new CubeMaterial(device)));
        }

        public override void Draw(in Render3DContext render, DrawContext3D item)
        {
            var local = GetLocalMatrix(item.Time) * item.World;

            // 半透明でも面の前後関係が正しく見えるよう、内側を向いた面をすべて
            // 描いてから、外側を向いた面を重ねる。
            DrawFaces(render, item, local, FaceCulling.Front);
            DrawFaces(render, item, local, FaceCulling.Back);
        }

        private void DrawFaces(
            in Render3DContext render, DrawContext3D item, in Matrix4x4 local, FaceCulling culling)
        {
            var pipeline = pipelines.Get(render.Device);
            var colors = parameter.FaceColors;
            var baked = textures;

            for (var face = 0; face < CubeFaces.Count; face++)
            {
                var world = CubeFaces.GetTransform((CubeFace)face) * local;
                var texture = baked.Get(render.Device, face);

                var constants = CubeConstants.Create(
                    render.GetWorldViewProjection(world), colors[face], item.Opacity, texture is not null);

                pipeline.Draw(render.Context, constants, item.ToDrawSettings(culling, texture));
            }
        }

        protected override void PrepareResources(in FrameContext itemTime)
        {
            var paths = parameter.FaceImages;

            // 指定を外した画像は閉じる。開いたままだと、ファイルを掴んだままになる。
            foreach (var path in images.Keys.Where(k => !paths.Contains(k)).ToArray())
            {
                if (images.Remove(path, out var stale))
                    stale.Dispose();
            }

            var live = paths.Where(p => !string.IsNullOrEmpty(p)).Cast<object>().ToHashSet();
            textureBridge.RetainOnly(live);

            if (live.Count == 0)
            {
                textures = FaceTextures.None;
                return;
            }

            var device = (lease ??= GraphicsDevicePool.Acquire()).Device;
            var views = new ID3D11ShaderResourceView?[CubeFaces.Count];

            for (var face = 0; face < views.Length; face++)
                views[face] = Bake(device, paths[face]);

            textures = new FaceTextures(device.NativePointer, views);
        }

        private ID3D11ShaderResourceView? Bake(ID3D11Device device, string path)
        {
            if (string.IsNullOrEmpty(path))
                return null;

            if (!images.TryGetValue(path, out var image))
            {
                try
                {
                    image = ImageFileSourceFactory.Create(Devices, path);
                }
                catch
                {
                    image = null;
                }

                // 消された・壊れているなど、開けない画像がある。色だけで描く。
                if (image is null)
                    return null;

                images[path] = image;
            }

            return textureBridge.GetTexture(device, Devices, image.Output, path, out _);
        }

        private float GetEdgeLength(in FrameContext itemTime)
            => WorldScale.ToWorld(parameter.Size.GetFloat(itemTime));

        private Matrix4x4 GetLocalMatrix(in FrameContext itemTime)
            => Matrix4x4.CreateScale(GetEdgeLength(itemTime))
             * Rotation3D.ForObject(
                   parameter.RotationX.GetFloat(itemTime),
                   parameter.RotationY.GetFloat(itemTime),
                   parameter.RotationZ.GetFloat(itemTime));

        // 回転角は分かっているので、実際に回した範囲を返す。どの向きにも対応できる
        // 外接立方体を返すと、辺の長さが最大で √3 ≒ 1.73 倍になり、そのぶん
        // 出力画像が無駄に大きくなる。
        protected override WorldBounds GetWorldBounds(in FrameContext itemTime)
            => WorldBounds.FromCube(1f).Transform(GetLocalMatrix(itemTime));

        public override void Dispose()
        {
            textures = FaceTextures.None;

            foreach (var image in images.Values)
                image.Dispose();
            images.Clear();

            textureBridge.Dispose();
            pipelines.Dispose();

            lease?.Dispose();
            lease = null;

            base.Dispose();
        }

        // 作るのは YMM4 の描画スレッド、読むのは 3Dプレビューのスレッドからもある。
        // まるごと差し替える形にしてあるので、読む側が途中の状態を見ることはない。
        private sealed record FaceTextures(nint DeviceKey, ID3D11ShaderResourceView?[] Views)
        {
            public static FaceTextures None { get; } = new(nint.Zero, []);

            public ID3D11ShaderResourceView? Get(ID3D11Device device, int face)
                => DeviceKey == device.NativePointer && face < Views.Length ? Views[face] : null;
        }
    }
}
