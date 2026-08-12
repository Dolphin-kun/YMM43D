using System.Windows.Controls;
using System.Windows.Input;
using YMM43D.PreviewTool.ViewModels;

namespace YMM43D.PreviewTool.Views
{
    public partial class Preview3DView : UserControl
    {
        public Preview3DView()
        {
            InitializeComponent();

            MouseDown += (s, e) => Focus();
        }

        private void UserControl_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.R || (Keyboard.Modifiers & ModifierKeys.Control) != ModifierKeys.Control)
                return;

            if (DataContext is Preview3DViewModel viewModel)
            {
                viewModel.ResetToSceneCamera();
                e.Handled = true;
            }
        }
    }
}
