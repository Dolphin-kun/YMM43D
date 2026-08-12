using YukkuriMovieMaker.Plugin.Shape;
using YukkuriMovieMaker.Project;

namespace Camera3D
{
    /// <summary>
    /// シーンを撮る3Dカメラをタイムラインに置けるようにします。
    /// </summary>
    /// <remarks>
    /// YMM4 にはアイテムの種類を増やす拡張点がないため、図形アイテムとして作ります。
    /// 図形として何も描かないので、映像には現れません。カメラをアイテムにすると、
    /// キーフレーム・プロジェクトへの保存・元に戻すが YMM4 のしくみでそのまま動きます。
    /// </remarks>
    public class Camera3DPlugin : IShapePlugin
    {
        public string Name => "3Dカメラ";
        public bool IsExoShapeSupported => false;
        public bool IsExoMaskSupported => false;

        public IShapeParameter CreateShapeParameter(SharedDataStore? sharedData)
            => new Camera3DParameter(sharedData);
    }
}
