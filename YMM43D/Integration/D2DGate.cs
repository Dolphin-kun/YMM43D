namespace YMM43D.Integration
{
    /// <summary>
    /// Direct2D の操作を、このプラグイン全体で1本に直列化するための鍵。
    /// </summary>
    /// <remarks>
    /// <c>ID2D1DeviceContext</c> はスレッド安全ではありません。描画先・変換・描画中状態を
    /// 内部に持つため、2つのスレッドから同時に呼ぶと状態が壊れ、悪ければ
    /// <c>ExecutionEngineException</c> としてプロセスごと落ちます。資源はコンテキストでは
    /// なくデバイスに属するので、鍵は全体で1つにしています。
    /// <para>
    /// <b>守る範囲</b>：このプラグインが直接呼ぶ Direct2D だけでなく、3Dプレビューが
    /// アイテムの絵を得るために回す YMM4 本体の描画（<c>ISource.Update</c> や
    /// エフェクト連鎖）も含めます。
    /// </para>
    /// <para>
    /// <b>入れ子の順序</b>：必ず「この鍵 → 3D描画用デバイス」の順で取ってください。
    /// 逆順に取る箇所を1つでも作ると詰まります。
    /// </para>
    /// <para>
    /// この鍵は YMM4 側の破棄までは止められません。あちらが所有する画像に触れてよい
    /// 場所については <c>VideoEffect3DProcessorBase</c> を参照してください。
    /// </para>
    /// </remarks>
    public static class D2DGate
    {
        /// <summary>Direct2D の操作を囲む鍵。</summary>
        public static readonly Lock Sync = new();
    }
}
