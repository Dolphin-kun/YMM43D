using System.Windows;
using System.Windows.Input;
using YukkuriMovieMaker.Settings;

namespace YMM43D.PreviewTool
{
    internal static class HostCommands
    {
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

        public static bool Execute(CommandType type, IInputElement? target)
        {
            if (Find(type) is not { } command || !command.CanExecute(null, target))
                return false;

            command.Execute(null, target);

            return true;
        }

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
