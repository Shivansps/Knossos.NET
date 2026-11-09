using Avalonia;
using Avalonia.Controls;

namespace Knossos.NET.Views
{
    public partial class ModLaunchDependencyView : UserControl
    {
        public ModLaunchDependencyView()
        {
            InitializeComponent();
            SizeChanged += (_, _) =>
            {
                var narrow = Bounds.Width < 560;
                RowLayout.ColumnDefinitions[2].Width = narrow ? new GridLength(0) : new GridLength(180);
                Grid.SetColumn(VersionPicker, narrow ? 1 : 2);
                Grid.SetRow(VersionPicker, narrow ? 1 : 0);
                VersionPicker.Margin = narrow ? new Thickness(0, 6, 0, 0) : new Thickness(0);
            };
        }
    }
}
