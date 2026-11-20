using System.Windows.Controls;
using YukkuriMovieMaker.Commons;

namespace YMM43D.Controls.ParentItem
{
    public partial class ParentItemSelector : UserControl, IPropertyEditorControl
    {
        public event EventHandler? BeginEdit;

        public event EventHandler? EndEdit;

        public ParentItemSelector()
        {
            InitializeComponent();

            DataContextChanged += (_, e) =>
            {
                if (e.OldValue is ParentItemSelectorViewModel before)
                {
                    before.BeginEdit -= OnBeginEdit;
                    before.EndEdit -= OnEndEdit;
                }

                if (e.NewValue is ParentItemSelectorViewModel after)
                {
                    after.BeginEdit += OnBeginEdit;
                    after.EndEdit += OnEndEdit;
                }
            };
        }

        public void SetFocus() => Choices.Focus();

        private void OnBeginEdit(object? sender, EventArgs e) => BeginEdit?.Invoke(this, e);

        private void OnEndEdit(object? sender, EventArgs e) => EndEdit?.Invoke(this, e);

        private void Choices_DropDownOpened(object? sender, EventArgs e) => (DataContext as ParentItemSelectorViewModel)?.Refresh();
    }
}
