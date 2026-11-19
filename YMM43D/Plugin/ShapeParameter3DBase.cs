using System.ComponentModel.DataAnnotations;
using System.Numerics;
using YMM43D.Commons;
using YukkuriMovieMaker.Commons;
using YukkuriMovieMaker.Controls;
using YukkuriMovieMaker.Player.Video;
using YukkuriMovieMaker.Plugin.Shape;
using YukkuriMovieMaker.Project;

namespace YMM43D.Plugin
{
    public abstract class ShapeParameter3DBase : ShapeParameterBase, ICameraSync
    {
        private const string Rotation = "3D回転";

        private readonly CameraSync cameraSync = new();

        protected ShapeParameter3DBase(SharedDataStore? sharedData) : base(sharedData)
        {
            cameraSync.Changed += () => OnPropertyChanged(nameof(CameraSyncAnimation));
        }

        [Display(GroupName = Rotation, Name = "中心X", Description = "回すときの中心を横にずらす量")]
        [AnimationSlider("F1", "px", -500, 500)]
        public Animation PivotX { get; } = new(0, -1000000, 1000000);

        [Display(GroupName = Rotation, Name = "中心Y", Description = "回すときの中心を縦にずらす量で、下が正です")]
        [AnimationSlider("F1", "px", -500, 500)]
        public Animation PivotY { get; } = new(0, -1000000, 1000000);

        [Display(GroupName = Rotation, Name = "中心Z", Description = "回すときの中心を奥行き方向にずらす量で、手前が正です")]
        [AnimationSlider("F1", "px", -500, 500)]
        public Animation PivotZ { get; } = new(0, -1000000, 1000000);

        public Animation CameraSyncAnimation => cameraSync.Value;

        protected IEnumerable<IAnimatable> PivotAnimatables => [PivotX, PivotY, PivotZ];

        public void TouchCameraSync() => cameraSync.TouchCameraSync();

        public Vector3 GetPivot(in FrameContext itemTime)
            => WorldScale.ToWorldPosition(PivotX.GetFloat(itemTime), PivotY.GetFloat(itemTime), PivotZ.GetFloat(itemTime));

        public Matrix4x4 GetTurn(in FrameContext itemTime)
        {
            if (this is not ISceneRotation3D rotation)
                return Pivot3D.Around(Matrix4x4.Identity, GetPivot(itemTime));

            var turn = rotation.ToOrientation(rotation.RotationX.GetFloat(itemTime), rotation.RotationY.GetFloat(itemTime), rotation.RotationZ.GetFloat(itemTime));

            return Pivot3D.Around(turn, GetPivot(itemTime));
        }

        public Matrix4x4 GetTurn(in FrameContext itemTime, in Matrix4x4 orientation) => Pivot3D.Around(orientation, GetPivot(itemTime));

        protected abstract Shape3DSourceBase Create3DSource(IGraphicsDevicesAndContext devices);

        public sealed override IShapeSource CreateShapeSource(IGraphicsDevicesAndContext devices)
        {
            var source = Create3DSource(devices);
            Provider3DRegistry.Register(this, source, devices);
            source.Disposed = () => Provider3DRegistry.Unregister(this, source);
            return source;
        }
    }
}
