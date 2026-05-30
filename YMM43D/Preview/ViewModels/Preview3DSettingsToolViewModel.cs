using System.ComponentModel;
using System.Windows.Input;
using System.Windows.Threading;
using YukkuriMovieMaker.Commons;
using YukkuriMovieMaker.Plugin;
using YukkuriMovieMaker.Project;

namespace YMM43D.Preview.ViewModels
{
    public class Preview3DSettingsToolViewModel : Bindable, ITimelineToolViewModel
    {
        private Timeline? timeline;
        private DispatcherTimer? pollTimer;
        private readonly CameraChangeTracker sceneCameraTracker = new();

        public SceneCamera Camera => SceneCamera.Instance;

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

            ForceTimelineRefresh();
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
