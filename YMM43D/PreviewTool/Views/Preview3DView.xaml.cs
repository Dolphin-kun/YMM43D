using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media.Animation;
using YMM43D.PreviewTool.ViewModels;

namespace YMM43D.PreviewTool.Views
{
    public partial class Preview3DView : UserControl
    {
        private static readonly Duration Slide = new(TimeSpan.FromSeconds(0.22));
        public Preview3DView()
        {
            InitializeComponent();

            MouseDown += (s, e) => Focus();
            DataContextChanged += OnDataContextChanged;
        }

        private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (e.OldValue is Preview3DViewModel before)
            {
                before.MenuRequested -= OnMenuRequested;
                before.PropertyChanged -= OnViewModelChanged;
            }

            if (e.NewValue is Preview3DViewModel after)
            {
                after.MenuRequested += OnMenuRequested;
                after.PropertyChanged += OnViewModelChanged;
                SidebarFrame.BeginAnimation(WidthProperty, null);
                SidebarFrame.Width = after.IsSidebarOpen ? after.SidebarWidth : 0;
            }
        }

        private void OnViewModelChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName != nameof(Preview3DViewModel.IsSidebarOpen) || sender is not Preview3DViewModel viewModel)
                return;

            var to = viewModel.IsSidebarOpen ? viewModel.SidebarWidth : 0;

            SidebarFrame.BeginAnimation(WidthProperty, new DoubleAnimation(to, Slide) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
        }

        private void SidebarEdge_DragDelta(object sender, DragDeltaEventArgs e)
        {
            if (DataContext is not Preview3DViewModel { IsSidebarOpen: true } viewModel)
                return;

            viewModel.SidebarWidth -= e.HorizontalChange;
            SidebarFrame.BeginAnimation(WidthProperty, null);
            SidebarFrame.Width = viewModel.SidebarWidth;
        }

        private void OnMenuRequested(IReadOnlyList<MeshMenuEntry> entries)
        {
            if (DataContext is not Preview3DViewModel viewModel || Resources["MeshPopup"] is not ContextMenu popup)
                return;

            popup.DataContext = viewModel;
            popup.ItemsSource = entries;
            popup.PlacementTarget = this;
            popup.Placement = PlacementMode.MousePoint;
            popup.IsOpen = true;
        }

        private void Sidebar_Edit(object? sender, EventArgs e)
        {
            if (DataContext is Preview3DViewModel viewModel)
                viewModel.SeparateSidebarEdit();
        }

        private void UserControl_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (DataContext is not Preview3DViewModel viewModel || e.OriginalSource is TextBox)
                return;

            var key = e.Key == Key.ImeProcessed ? e.ImeProcessedKey : e.Key;

            e.Handled = viewModel.HandleKey(key, Keyboard.Modifiers);
        }
    }
}
