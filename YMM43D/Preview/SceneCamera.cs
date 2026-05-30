using System.Numerics;
using YukkuriMovieMaker.Commons;
using YukkuriMovieMaker.Player.Video;

namespace YMM43D.Preview
{
    /// <summary>
    /// シーン内の「カメラオブジェクト」の設定。
    /// </summary>
    [Serializable]
    public class SceneCamera : Bindable
    {
        public static SceneCamera Instance { get; } = new();
        private Vector3 cameraTarget = Vector3.Zero;
        private bool hasTimelineContext;
        private int timelineFrame;
        private int timelineLength = 1;
        private int timelineFps = 60;

        public Animation CameraYaw { get; } = new Animation(0, -3600, 3600);
        public Animation CameraPitch { get; } = new Animation(0, -90, 90);
        public Animation CameraRoll { get; } = new Animation(0, -3600, 3600);
        public Animation CameraDistance { get; } = new Animation(10, 0.1, 1000);
        public Vector3 CameraTarget { get => cameraTarget; set => Set(ref cameraTarget, value, nameof(CameraTarget)); }

        public void Reset()
        {
            // TODO: DefaultValue is read-only. Need to find the correct way to reset Animation values.
            // For now, we use CopyFrom which is confirmed to work in the project.
            CameraYaw.CopyFrom(new Animation(0, -3600, 3600));
            CameraPitch.CopyFrom(new Animation(0, -90, 90));
            CameraRoll.CopyFrom(new Animation(0, -3600, 3600));
            CameraDistance.CopyFrom(new Animation(10, 0.1, 1000));
            CameraTarget = Vector3.Zero;
        }

        public void UpdateTimelineContext(int frame, int length, int fps)
        {
            timelineFrame = frame;
            timelineLength = Math.Max(1, length);
            timelineFps = Math.Max(1, fps);
            hasTimelineContext = true;
        }

        public bool TryGetTimelineContext(out int frame, out int length, out int fps)
        {
            if (!hasTimelineContext)
            {
                frame = 0;
                length = 0;
                fps = 0;
                return false;
            }

            frame = timelineFrame;
            length = timelineLength;
            fps = timelineFps;
            return true;
        }

        public void ResolveTimelineContext(TimelineItemSourceDescription sourceDescription, out int frame, out int length, out int fps)
        {
            frame = sourceDescription.TimelinePosition.Frame;
            length = sourceDescription.TimelineDuration.Frame;
            fps = sourceDescription.FPS;

            if (length > 0 && fps > 0)
                return;

            if (TryGetTimelineContext(out int timelineFrame, out int timelineLength, out int timelineFps))
            {
                frame = timelineFrame;
                length = timelineLength;
                fps = timelineFps;
                return;
            }

            frame = sourceDescription.ItemPosition.Frame;
            length = Math.Max(1, sourceDescription.ItemDuration.Frame);
            fps = Math.Max(1, sourceDescription.FPS);
        }

        public Matrix4x4 GetViewMatrix(TimelineItemSourceDescription sourceDescription)
        {
            ResolveTimelineContext(sourceDescription, out int frame, out int length, out int fps);
            return GetViewMatrix(frame, length, fps);
        }

        public Vector3 GetPosition(TimelineItemSourceDescription sourceDescription)
        {
            ResolveTimelineContext(sourceDescription, out int frame, out int length, out int fps);
            return GetPosition(frame, length, fps);
        }

        public Matrix4x4 GetViewMatrix(int frame, int length, int fps)
        {
            var yaw = (float)CameraYaw.GetValue(frame, length, fps);
            var pitch = (float)CameraPitch.GetValue(frame, length, fps);
            var roll = (float)CameraRoll.GetValue(frame, length, fps);
            var dist = (float)CameraDistance.GetValue(frame, length, fps);

            var rotation = Commons.Math.CreateCameraRotation(yaw, pitch, roll);
            var lookDir = Vector3.Transform(new Vector3(0, 0, -1), rotation);
            var cameraPos = CameraTarget - lookDir * dist;
            return Matrix4x4.CreateLookAt(cameraPos, CameraTarget, Vector3.Transform(Vector3.UnitY, rotation));
        }

        public Vector3 GetPosition(int frame, int length, int fps)
        {
            var yaw = (float)CameraYaw.GetValue(frame, length, fps);
            var pitch = (float)CameraPitch.GetValue(frame, length, fps);
            var roll = (float)CameraRoll.GetValue(frame, length, fps);
            var dist = (float)CameraDistance.GetValue(frame, length, fps);

            var rotation = Commons.Math.CreateCameraRotation(yaw, pitch, roll);
            var lookDir = Vector3.Transform(new Vector3(0, 0, -1), rotation);
            return CameraTarget - lookDir * dist;
        }

        public static Matrix4x4 GetProjectionMatrix(float aspectRatio)
        {
            return Matrix4x4.CreatePerspectiveFieldOfView((float)Math.PI / 4, aspectRatio, 0.1f, 1000f);
        }
    }
}
