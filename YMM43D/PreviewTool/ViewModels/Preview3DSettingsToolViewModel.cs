using System.ComponentModel;
using System.Windows.Input;
using System.Windows.Threading;
using YMM43D.Integration;
using YMM43D.Scene3D;
using YukkuriMovieMaker.Commons;
using YukkuriMovieMaker.Plugin;
using YukkuriMovieMaker.Project;

namespace YMM43D.PreviewTool.ViewModels
{
    public class Preview3DSettingsToolViewModel : Bindable, ITimelineToolViewModel, IDisposable
    {
        private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(50);

        private TimelineRefresher? refresher;
        private Timeline? timeline;
        private DispatcherTimer? pollTimer;
        private SceneCamera camera = new();
        private bool isDisposed;

        public SceneCamera Camera
        {
            get => camera;
            private set => Set(ref camera, value, nameof(Camera));
        }

        public ICommand ResetCameraCommand { get; }

        public Preview3DSettingsToolViewModel()
        {
            ResetCameraCommand = new ActionCommand(_ => true, _ => Camera.Reset());
        }

        public void SetTimelineToolInfo(TimelineToolInfo info)
        {
            Detach();

            timeline = info.Timeline;
            if (timeline is null)
                return;

            Camera = SceneCameraRegistry.Get(timeline.ID);
            refresher = TimelineRefresher.For(timeline);

            // タイムラインの変更通知は購読しない。描き直しを促す手が現在フレームを
            // 揺らすので、購読すると自分が起こした通知で呼び戻される。カメラの
            // 変化はこの間隔で拾えば足りる。
            pollTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = PollInterval };
            pollTimer.Tick += OnPollTick;
            pollTimer.Start();
        }

        private void OnPollTick(object? sender, EventArgs e)
        {
            if (timeline is not null)
                refresher?.RefreshIfCameraChanged(timeline, Camera);
        }

        private void Detach()
        {
            if (pollTimer is not null)
            {
                pollTimer.Stop();
                pollTimer.Tick -= OnPollTick;
                pollTimer = null;
            }

            timeline = null;
            refresher = null;
        }

        public void Dispose()
        {
            if (isDisposed)
                return;

            isDisposed = true;
            Detach();
            GC.SuppressFinalize(this);
        }
    }
}
