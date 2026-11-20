using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using YukkuriMovieMaker.Commons;

namespace YMM43D.Controls.ModifierStack
{
    public partial class ModifierStackEditor : UserControl, IPropertyEditorControl
    {
        public event EventHandler? BeginEdit;

        public event EventHandler? EndEdit;

        public ModifierStackEditor()
        {
            InitializeComponent();

            DataContextChanged += OnDataContextChanged;
        }

        public void SetFocus() => ModifierList.Focus();

        private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (e.OldValue is ModifierStackEditorViewModel before)
            {
                before.BeginEdit -= Slider_BeginEdit;
                before.EndEdit -= Slider_EndEdit;
            }

            if (e.NewValue is ModifierStackEditorViewModel after)
            {
                after.BeginEdit += Slider_BeginEdit;
                after.EndEdit += Slider_EndEdit;
            }
        }

        private void Slider_BeginEdit(object? sender, EventArgs e) => BeginEdit?.Invoke(this, e);

        private void Slider_EndEdit(object? sender, EventArgs e) => EndEdit?.Invoke(this, e);

        private void ModifierList_PreviewMouseWheel(object sender, MouseWheelEventArgs e) => ListWheel.PassOn(ModifierList, e);

        private Window? host;

        private void AddButton_Checked(object sender, RoutedEventArgs e) => (DataContext as ModifierStackEditorViewModel)?.OpenPicker();

        private void Picker_Opened(object? sender, EventArgs e)
        {
            host = Window.GetWindow(this);

            if (host is not null)
            {
                host.PreviewMouseDown += Host_PreviewMouseDown;
                host.Deactivated += Host_Deactivated;
            }

            SearchBox.Focus();
        }

        private void Picker_Closed(object? sender, EventArgs e)
        {
            if (host is not null)
            {
                host.PreviewMouseDown -= Host_PreviewMouseDown;
                host.Deactivated -= Host_Deactivated;
                host = null;
            }

            AddButton.IsChecked = false;
            (DataContext as ModifierStackEditorViewModel)?.ClosePicker();
        }

        private void Host_PreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            if (InsidePicker(e.OriginalSource))
                return;

            e.Handled = true;
            Close();
        }

        private void Host_Deactivated(object? sender, EventArgs e) => Close();

        private bool InsidePicker(object source)
        {
            for (var at = source as DependencyObject; at is not null; at = at is Visual ? VisualTreeHelper.GetParent(at) : LogicalTreeHelper.GetParent(at))
            {
                if (ReferenceEquals(at, Picker.Child))
                    return true;
            }

            return false;
        }

        private void Close() => AddButton.IsChecked = false;

        private void SearchBox_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if ((e.Key == Key.Return || (e.Key == Key.Right && SearchBox.CaretIndex == SearchBox.Text.Length)) && ChoiceList.Items.Count > 0)
                FocusChoices();
            else if (e.Key == Key.Down)
                FocusItem(CategoryList);
            else if (e.Key == Key.Escape)
                Close();
            else
                return;

            e.Handled = true;
        }

        private void CategoryList_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key is Key.Tab or Key.Return or Key.Right)
                FocusChoices();
            else if (e.Key is Key.Back or Key.Escape || (e.Key == Key.Up && CategoryList.SelectedIndex == 0))
                SearchBox.Focus();
            else
                return;

            e.Handled = true;
        }

        private void ChoiceList_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Return)
            {
                Close();
            }
            else if (e.Key is Key.Back or Key.Escape or Key.Left)
            {
                SearchBox.Focus();
            }
            else if (e.Key == Key.Delete)
            {
                ChoiceList.SelectedIndex = -1;
                Close();
            }
            else
            {
                return;
            }

            e.Handled = true;
        }

        private void ChoiceList_MouseUp(object sender, MouseButtonEventArgs e)
        {
            if (e.OriginalSource is not ScrollViewer && ChoiceList.SelectedItem is not null)
                Close();
        }

        private void FocusChoices()
        {
            if (ChoiceList.SelectedIndex < 0)
                ChoiceList.SelectedIndex = 0;

            FocusItem(ChoiceList);
        }

        private static void FocusItem(ListBox list)
        {
            list.ScrollIntoView(list.SelectedItem);

            if (list.ItemContainerGenerator.ContainerFromIndex(Math.Max(list.SelectedIndex, 0)) is FrameworkElement item)
                item.Focus();
        }
    }
}
