using System.IO;
using System.Text;
using YukkuriMovieMaker.Player.Video;
using YukkuriMovieMaker.Project;

namespace YMM43D.Integration
{
    /// <summary>
    /// 描画要求から、シーン内の全アイテムに届くかどうかを調べます。
    /// </summary>
    /// <remarks>
    /// <para>
    /// アイテムをまたいだ 3D の前後関係を出すには、1つのアイテムを描く時点で
    /// シーン内の他のアイテムを知る必要があります。YMM4 が渡してくる
    /// <see cref="TimelineSourceDescription.Scenes"/> は <c>ISceneInfo</c> の列で、
    /// この型自体はアイテム一覧を持ちません。実体が
    /// <see cref="Scene"/> であればタイムラインに届きますが、それは実行時にしか
    /// 分かりません。
    /// </para>
    /// <para>
    /// 届かない場合、アイテム一覧はツールウィンドウ経由でしか得られず、
    /// 「3Dプレビューのパネルを閉じると書き出し結果が変わる」ことになります。
    /// 設計を決める前にここを確かめる必要があるため、一度だけ結果を書き出します。
    /// </para>
    /// </remarks>
    public static class SceneAccessProbe
    {
        private static readonly Lock gate = new();
        private static readonly HashSet<TimelineSourceUsage> reported = [];

        /// <summary>調査結果の書き出し先。</summary>
        public static string ReportPath { get; }
            = Path.Combine(Path.GetTempPath(), "YMM43D_scene_probe.txt");

        /// <summary>
        /// 描画要求を調べ、結果を <see cref="ReportPath"/> に追記します。
        /// </summary>
        /// <remarks>
        /// 再生・一時停止・書き出しのそれぞれで一度ずつだけ記録します。
        /// 経路によって結果が変わりうるため、用途ごとに分けています。
        /// </remarks>
        public static void ReportOnce(TimelineSourceDescription description)
        {
            lock (gate)
            {
                if (!reported.Add(description.Usage))
                    return;
            }

            try
            {
                File.AppendAllText(ReportPath, Describe(description), Encoding.UTF8);
            }
            catch
            {
                // 調査用の書き出しなので、失敗しても描画は続ける。
            }
        }

        private static string Describe(TimelineSourceDescription description)
        {
            var report = new StringBuilder();
            report.AppendLine($"===== {DateTime.Now:yyyy-MM-dd HH:mm:ss}  Usage={description.Usage}");
            report.AppendLine($"  SceneId    : {description.SceneId}");
            report.AppendLine($"  ScreenSize : {description.ScreenSize.Width}x{description.ScreenSize.Height}");

            if (description.Scenes is null)
            {
                report.AppendLine("  Scenes     : null");
                return report.ToString();
            }

            var scenes = description.Scenes.ToArray();
            report.AppendLine($"  Scenes     : {scenes.Length} 件");

            foreach (var info in scenes)
            {
                if (info is null)
                {
                    report.AppendLine("    - null");
                    continue;
                }

                var isCurrent = info.ID == description.SceneId ? " ★このシーン" : "";
                report.AppendLine($"    - {info.Name} [{info.ID}]{isCurrent}");
                report.AppendLine($"        実体の型 : {info.GetType().FullName}");

                if (info is not Scene scene)
                {
                    report.AppendLine("        Scene へのキャスト : 失敗");
                    continue;
                }

                report.AppendLine("        Scene へのキャスト : 成功");

                var timeline = scene.Timeline;
                if (timeline is null)
                {
                    report.AppendLine("        Timeline : null");
                    continue;
                }

                var items = timeline.Items;
                report.AppendLine($"        Timeline.Items : {(items is null ? "null" : $"{items.Count} 件")}");

                foreach (var item in items ?? [])
                    report.AppendLine($"          {item.GetType().Name} (Layer {item.Layer}, Frame {item.Frame})");
            }

            return report.ToString();
        }
    }
}
