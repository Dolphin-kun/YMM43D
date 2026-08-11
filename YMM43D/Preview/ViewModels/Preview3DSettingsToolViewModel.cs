using System.ComponentModel;
using System.Linq;
using System.Windows.Input;
using System.Windows.Threading;
using YMM43D.Rendering;
using YukkuriMovieMaker.Commons;
using YukkuriMovieMaker.Plugin;
using YukkuriMovieMaker.Project;
using YukkuriMovieMaker.Project.Items;

namespace YMM43D.Preview.ViewModels
{
    public class Preview3DSettingsToolViewModel : Bindable, ITimelineToolViewModel
    {
        private Timeline? timeline;
        private DispatcherTimer? pollTimer;
        private readonly CameraChangeTracker sceneCameraTracker = new();
        private SceneCamera camera = new();

        public SceneCamera Camera
        {
            get => camera;
            private set => Set(ref camera, value, nameof(Camera));
        }

        public ICommand ResetCameraCommand { get; }

        public Preview3DSettingsToolViewModel()
        {
            ResetCameraCommand = new ActionCommand(
                _ => true,
                _ => Camera.Reset()
            );
        }

        public void SetTimelineToolInfo(TimelineToolInfo info)
        {
            timeline?.PropertyChanged -= OnTimelinePropertyChanged;

            timeline = info.Timeline;
            if (timeline != null)
            {
                Camera = SceneCamera.GetCamera(SharedGraphics.Devices);
            }

            if (timeline != null)
            {
                timeline.PropertyChanged += OnTimelinePropertyChanged;
                EnsurePollTimer();
            }
        }

        private void EnsurePollTimer()
        {
            if (pollTimer != null)
                return;

            pollTimer = new DispatcherTimer(DispatcherPriority.Background)
            {
                Interval = TimeSpan.FromMilliseconds(50)
            };
            pollTimer.Tick += (_, _) => RefreshOutputPreviewIfCameraChanged();
            pollTimer.Start();
        }

        private void OnTimelinePropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (string.IsNullOrEmpty(e.PropertyName) ||
                e.PropertyName == nameof(Timeline.CurrentFrame) ||
                e.PropertyName == nameof(Timeline.Length) ||
                e.PropertyName == nameof(Timeline.VideoInfo))
            {
                RefreshOutputPreviewIfCameraChanged();
            }
        }

        private void RefreshOutputPreviewIfCameraChanged()
        {
            if (timeline == null)
                return;

            int frame = timeline.CurrentFrame;
            int length = timeline.Length;
            int fps = timeline.VideoInfo.FPS;

            Camera.UpdateTimelineContext(frame, length, fps);

            if (!sceneCameraTracker.HasChanged(Camera, frame, length, fps))
                return;

            TouchShape3DParameters();
            ForceTimelineRefresh();
        }

        private void TouchShape3DParameters()
        {
            if (timeline?.Items == null)
                return;

            foreach (var item in timeline.Items)
            {
                if (item is ShapeItem shapeItem)
                {
                    if (shapeItem.ShapeParameter is ICameraSync param)
                    {
                        param.TouchCameraSync();
                    }
                }

                if (item is IVideoItem videoItem && videoItem.VideoEffects != null)
                {
                    foreach (var effect in videoItem.VideoEffects)
                    {
                        if (effect is ICameraSync param)
                        {
                            param.TouchCameraSync();
                        }
                    }
                }
            }
        }

        private void ForceTimelineRefresh()
        {
            if (timeline == null)
                return;

            int currentFrame = timeline.CurrentFrame;
            int alternateFrame = currentFrame < timeline.Length - 1
                ? currentFrame + 1
                : (currentFrame > 0 ? currentFrame - 1 : 0);

            if (alternateFrame == currentFrame)
                return;

            timeline.CurrentFrame = alternateFrame;
            timeline.CurrentFrame = currentFrame;
        }
    }
}
