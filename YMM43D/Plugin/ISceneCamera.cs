using YMM43D.Scene3D;

namespace YMM43D.Plugin
{
    /// <summary>
    /// シーンを撮るカメラとして振る舞うアイテム。
    /// </summary>
    /// <remarks>
    /// タイムラインに置いたアイテムがこれを実装していると、その区間の 3D 描画は
    /// このカメラで行われます。カメラをアイテムにするのは、キーフレームと
    /// プロジェクトへの保存を YMM4 に任せるためです。アイテムに属さない値を
    /// 保存する口が YMM4 側に無いため、これ以外に永続化する方法がありません。
    /// <para>
    /// 同じ時刻に複数あるときは、レイヤー番号がいちばん大きいものが使われます。
    /// 時間で並べればカメラを切り替えるカット割りになります。
    /// </para>
    /// </remarks>
    public interface ISceneCamera
    {
        /// <summary>
        /// アイテム内の時間位置における、カメラの設定値を返します。
        /// </summary>
        /// <param name="itemTime">
        /// このアイテムの中での位置。キーフレームはアイテムの長さに対して打たれるので、
        /// タイムライン全体ではなくこちらで評価してください。
        /// </param>
        CameraState GetState(in FrameContext itemTime);

        /// <summary>
        /// プレビュー上のドラッグに合わせてカメラを動かします。
        /// </summary>
        /// <remarks>
        /// 差分で受け取るのは、キーフレームを打ったカメラを壊さないためです。
        /// 全部のキーフレームを同じだけずらせば、打った動きは残ります。
        /// </remarks>
        void Move(in CameraMove move);
    }
}
