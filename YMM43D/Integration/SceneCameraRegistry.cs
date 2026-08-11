using System.Collections.Concurrent;
using YMM43D.Scene3D;
using YukkuriMovieMaker.Player.Video;

namespace YMM43D.Integration
{
    /// <summary>
    /// シーンごとの <see cref="SceneCamera"/> を保持します。
    /// </summary>
    /// <remarks>
    /// 3Dプレビュー・図形アイテム・映像エフェクトは、それぞれ別の経路で呼ばれながら
    /// 同じシーンのカメラを共有する必要があります。YMM4 がシーンに割り当てる
    /// <see cref="Guid"/> を鍵にすることで、どの経路からでも同じカメラに辿り着けます。
    /// <para>
    /// 以前はグラフィックスデバイスのオブジェクト同一性を鍵にしていましたが、
    /// これはデバイスがシーンと1対1である保証がなく、デバイスを保持するグローバル変数を
    /// 4箇所から書き換える必要もありました。
    /// </para>
    /// </remarks>
    public static class SceneCameraRegistry
    {
        private static readonly ConcurrentDictionary<Guid, SceneCamera> cameras = new();

        /// <summary>
        /// シーンのカメラを取得します。まだ無ければ作成します。
        /// </summary>
        public static SceneCamera Get(Guid sceneId) => cameras.GetOrAdd(sceneId, _ => new SceneCamera());

        /// <summary>
        /// 描画要求に対応するシーンのカメラを取得します。
        /// </summary>
        public static SceneCamera Get(TimelineItemSourceDescription description) => Get(description.SceneId);
    }
}
