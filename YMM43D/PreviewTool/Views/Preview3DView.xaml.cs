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
            if (DataContext is not Preview3DViewModel viewModel)
                return;

            var key = e.Key == Key.ImeProcessed ? e.ImeProcessedKey : e.Key;

            e.Handled = viewModel.HandleKey(key, Keyboard.Modifiers);
        }
    }
}
