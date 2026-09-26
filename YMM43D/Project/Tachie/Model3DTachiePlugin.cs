using YukkuriMovieMaker.Commons;
using YukkuriMovieMaker.Exo;
using YukkuriMovieMaker.Plugin.Tachie;

namespace YMM43D.Project.Tachie
{
    // 3Dモデルを立ち絵として使うためのプラグイン。
    // セリフに合わせた口パクと、表情アイテムでの表情の切り替えが効く。
    public class Model3DTachiePlugin : ITachiePlugin
    {
        public string Name => "3Dモデル";

        public bool HasScriptFile => false;

        public ITachieCharacterParameter CreateCharacterParameter() => new Model3DCharacterParameter();

        public ITachieItemParameter CreateItemParameter() => new Model3DItemParameter();

        public ITachieFaceParameter CreateFaceParameter() => new Model3DFaceParameter();

        public ITachieSource CreateTachieSource(IGraphicsDevicesAndContext devices)
            => new Model3DTachieSource(devices);

        public IEnumerable<ExoItem> CreateExoItems(
            int FPS,
            IEnumerable<TachieItemExoDescription> tachieItemDescriptions,
            IEnumerable<TachieFaceItemExoDescription> faceItemDescriptions,
            IEnumerable<TachieVoiceItemExoDescription> voiceDescriptions) => [];

        public void CreateScriptFile(string scriptDirectoryPath)
        {
        }
    }
}
