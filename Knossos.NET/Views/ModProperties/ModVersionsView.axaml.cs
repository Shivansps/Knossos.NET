using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using System;
using System.Linq;

namespace Knossos.NET.Views
{
    public partial class ModVersionsView : UserControl
    {
        public static readonly StyledProperty<double> TreeTextWidthProperty = AvaloniaProperty.Register<ModVersionsView, double>(nameof(TreeTextWidth), 540);
        public double TreeTextWidth { get => GetValue(TreeTextWidthProperty); set => SetValue(TreeTextWidthProperty, value); }

        public ModVersionsView()
        {
            InitializeComponent();
            SizeChanged += (_, _) => UpdateResponsiveLayout();
            AttachedToVisualTree += (_, _) => UpdateResponsiveLayout();
        }
        private void UpdateResponsiveLayout()
        {
            var narrow = Bounds.Width < 900;
            var margin = Bounds.Width < 420 ? 10 : 24;
            PageLayout.Margin = new Thickness(margin, 16);
            var available = Math.Max(100, Bounds.Width - margin * 2);
            VersionPicker.Width = Math.Min(230, available);
            TreeTextWidth = Math.Max(80, Math.Min(540, (narrow ? available : available / 2) - 110));
            foreach (var label in OptionsPanel.Children.OfType<CheckBox>().Select(c => c.Content).OfType<TextBlock>()) label.MaxWidth = Math.Max(60, available - 44);
            PageScroll.VerticalScrollBarVisibility = narrow ? ScrollBarVisibility.Auto : ScrollBarVisibility.Disabled;
            PackagePanels.ColumnDefinitions = new ColumnDefinitions(narrow ? "*" : "*,*");
            PackagePanels.RowDefinitions = new RowDefinitions(narrow ? "*,*" : "*");
            PackagePanels.Height = narrow ? 900 : double.NaN;
            PackagePanels.RowSpacing = narrow ? 12 : 0;
            Grid.SetColumn(DependencyPanel, narrow ? 0 : 1);
            Grid.SetRow(DependencyPanel, narrow ? 1 : 0);
            PageLayout.RowDefinitions[1].Height = narrow ? GridLength.Auto : new GridLength(1, GridUnitType.Star);
        }
    }
}
