using System.Windows.Controls;

namespace YMM43D.PreviewTool.Views
{
    public partial class Preview3DView : UserControl
    {
        public Preview3DView()
        {
            InitializeComponent();
            
            // マウスクリック時にフォーカスを取得する
            MouseDown += (s, e) => Focus();
        }

        private void UserControl_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key == System.Windows.Input.Key.R && (System.Windows.Input.Keyboard.Modifiers & System.Windows.Input.ModifierKeys.Control) == System.Windows.Input.ModifierKeys.Control)
            {
                if (DataContext is ViewModels.Preview3DViewModel vm)
                {
                    vm.ResetToSceneCamera();
                    e.Handled = true;
                }
            }
        }
    }
}
