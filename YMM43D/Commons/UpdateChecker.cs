using System.Diagnostics;
using System.Net.Http;
using System.Text.Json;
using System.Windows.Input;
using YukkuriMovieMaker.Commons;

namespace YMM43D.Commons
{
    public sealed class UpdateChecker : Bindable
    {
        private const string PostId = "";

        private static readonly HttpClient httpClient = new() { Timeout = TimeSpan.FromSeconds(10) };

        public static UpdateChecker Instance { get; } = new();

        private Task? checkTask;
        private string downloadUrl = string.Empty;

        public string CurrentVersion => BuildInfo.Version;

        public string LatestVersion { get => latestVersion; private set => Set(ref latestVersion, value); }
        private string latestVersion = string.Empty;

        public bool HasUpdate
        {
            get => hasUpdate;
            private set
            {
                if (Set(ref hasUpdate, value))
                    OnPropertyChanged(nameof(ShowsNotice));
            }
        }
        private bool hasUpdate;

        public bool ShowsNotice => HasUpdate && !isDismissed;
        private bool isDismissed;

        public ICommand OpenDownloadPageCommand { get; }

        public ICommand DismissCommand { get; }

        private UpdateChecker()
        {
            OpenDownloadPageCommand = new ActionCommand(_ => HasUpdate, _ => OpenDownloadPage());
            DismissCommand = new ActionCommand(_ => true, _ =>
            {
                isDismissed = true;
                OnPropertyChanged(nameof(ShowsNotice));
            });
        }

        public void EnsureChecked()
        {
            checkTask ??= CheckAsync();
        }

        private async Task CheckAsync()
        {
            if (string.IsNullOrEmpty(PostId))
                return;

            try
            {
                using var request = new HttpRequestMessage(
                    HttpMethod.Get, $"https://ymm4-info.net/api/plugin/{PostId}/version");
                request.Headers.Add("x-ymm4-plugin-check", "true");

                using var response = await httpClient.SendAsync(request);

                if (!response.IsSuccessStatusCode)
                    return;

                using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
                var root = document.RootElement;

                if (!root.TryGetProperty("success", out var success)
                    || success.ValueKind != JsonValueKind.True
                    || !root.TryGetProperty("version", out var version)
                    || version.GetString() is not { Length: > 0 } latest)
                {
                    return;
                }

                if (root.TryGetProperty("downloadURL", out var url) && url.ValueKind == JsonValueKind.String)
                    downloadUrl = url.GetString() ?? string.Empty;

                LatestVersion = latest;
                HasUpdate = IsNewer(latest, CurrentVersion);
            }
            catch (Exception error)
            {
                Trace.TraceInformation($"[YMM43D] 新しいバージョンを確かめられませんでした。{error.Message}");
            }
        }

        private void OpenDownloadPage()
        {
            var target = Uri.TryCreate(downloadUrl, UriKind.Absolute, out var uri)
                && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp)
                    ? uri.AbsoluteUri
                    : "https://ymm4-info.net/";

            Process.Start(new ProcessStartInfo { FileName = target, UseShellExecute = true });
        }

        public static bool IsNewer(string candidate, string current)
            => TryParse(candidate, out var next) && TryParse(current, out var now) && Compare(next, now) > 0;

        private static bool TryParse(string text, out (Version Core, string[] PreRelease) parsed)
        {
            parsed = default;

            var trimmed = text.Trim().TrimStart('v', 'V');
            var plus = trimmed.IndexOf('+');

            if (plus >= 0)
                trimmed = trimmed[..plus];

            var dash = trimmed.IndexOf('-');
            var core = dash >= 0 ? trimmed[..dash] : trimmed;

            if (!Version.TryParse(core.Contains('.') ? core : core + ".0", out var version))
                return false;

            parsed = (Normalize(version), dash >= 0 ? trimmed[(dash + 1)..].Split('.') : []);
            return true;
        }

        private static Version Normalize(Version version)
            => new(version.Major, version.Minor, Math.Max(0, version.Build), Math.Max(0, version.Revision));

        private static int Compare((Version Core, string[] PreRelease) left, (Version Core, string[] PreRelease) right)
        {
            var core = left.Core.CompareTo(right.Core);

            if (core != 0)
                return core;

            if (left.PreRelease.Length == 0 || right.PreRelease.Length == 0)
                return (left.PreRelease.Length == 0).CompareTo(right.PreRelease.Length == 0);

            for (var i = 0; i < Math.Min(left.PreRelease.Length, right.PreRelease.Length); i++)
            {
                var a = left.PreRelease[i];
                var b = right.PreRelease[i];

                var aIsNumber = int.TryParse(a, out var aNumber);
                var bIsNumber = int.TryParse(b, out var bNumber);

                var order = (aIsNumber, bIsNumber) switch
                {
                    (true, true) => aNumber.CompareTo(bNumber),
                    (true, false) => -1,
                    (false, true) => 1,
                    _ => string.CompareOrdinal(a, b),
                };

                if (order != 0)
                    return order;
            }

            return left.PreRelease.Length.CompareTo(right.PreRelease.Length);
        }
    }
}
