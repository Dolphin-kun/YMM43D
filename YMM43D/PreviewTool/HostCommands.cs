using System.Windows;
using System.Windows.Input;
using YukkuriMovieMaker.Settings;

namespace YMM43D.PreviewTool
{
    /// <summary>
    /// YMM4 本体のコマンドを、3Dプレビューから使うための橋渡し。
    /// </summary>
    /// <remarks>
    /// ショートカットを自分で決め打ちにすると、本体側で割り当てを変えている人と
    /// 食い違います。本体に登録されている操作は、割り当ても実行も本体に聞きます。
    /// </remarks>
    internal static class HostCommands
    {
        /// <summary>
        /// そのキーが、本体で <paramref name="type"/> に割り当てられているかどうか。
        /// </summary>
        public static bool Matches(CommandType type, Key key, ModifierKeys modifiers)
        {
            if (Find(type) is not { } command)
                return false;

            foreach (var gesture in command.InputGestures)
            {
                if (gesture is KeyGesture keys && keys.Key == key && keys.Modifiers == modifiers)
                    return true;
            }

            return false;
        }

        /// <summary>
        /// 本体のコマンドを実行します。
        /// </summary>
        /// <param name="target">
        /// 実行の起点にする要素。本体のウィンドウまで遡って処理されるので、
        /// プレビューの中の要素を渡してください。
        /// </param>
        /// <returns>実行できたら <c>true</c>。</returns>
        public static bool Execute(CommandType type, IInputElement? target)
        {
            if (Find(type) is not { } command || !command.CanExecute(null, target))
                return false;

            command.Execute(null, target);

            return true;
        }

        /// <remarks>
        /// 設定はアプリ全体で1つ。まだ用意されていない場面で触ると落ちるので、
        /// 取れなければ「その操作は無い」ものとして扱います。
        /// </remarks>
        private static RoutedUICommandEx? Find(CommandType type)
        {
            try
            {
                return CommandSettings.Default?.GetCommand(type);
            }
            catch
            {
                return null;
            }
        }
    }
}
