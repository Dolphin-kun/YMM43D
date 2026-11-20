using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using YukkuriMovieMaker.Commons;

namespace YMM43D.Controls.ControlPoints
{
    public partial class ControlPointsEditor : UserControl, IPropertyEditorControl
    {
        private const double LeastHeight = 60;

        private const double MostHeight = 600;

        public event EventHandler? BeginEdit;

        public event EventHandler? EndEdit;

        public ControlPointsEditor()
        {
            InitializeComponent();

            DataContextChanged += OnDataContextChanged;
        }

        public void SetFocus() => PointList.Focus();

        private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (e.OldValue is ControlPointsEditorViewModel before)
            {
                before.BeginEdit -= Forward_BeginEdit;
                before.EndEdit -= Forward_EndEdit;
            }

            if (e.NewValue is ControlPointsEditorViewModel after)
            {
                after.BeginEdit += Forward_BeginEdit;
                after.EndEdit += Forward_EndEdit;
            }
        }

        private void Forward_BeginEdit(object? sender, EventArgs e) => BeginEdit?.Invoke(this, e);

        private void Forward_EndEdit(object? sender, EventArgs e) => EndEdit?.Invoke(this, e);

        private void Slider_BeginEdit(object? sender, EventArgs e) => BeginEdit?.Invoke(this, e);

        private void Slider_EndEdit(object? sender, EventArgs e) => EndEdit?.Invoke(this, e);

        private void Grip_DragDelta(object sender, DragDeltaEventArgs e)
            => PointList.Height = Math.Clamp(PointList.Height + e.VerticalChange, LeastHeight, MostHeight);

        private void PointList_PreviewMouseWheel(object sender, MouseWheelEventArgs e) => ListWheel.PassOn(PointList, e);
    }
}
