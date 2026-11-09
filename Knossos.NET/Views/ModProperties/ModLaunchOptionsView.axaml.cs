using Avalonia;
using Avalonia.Controls;
using System;

namespace Knossos.NET.Views
{
    public partial class ModLaunchOptionsView : UserControl
    {
        public static readonly StyledProperty<double> FlagGroupWidthProperty = AvaloniaProperty.Register<ModLaunchOptionsView, double>(nameof(FlagGroupWidth), 300);
        public double FlagGroupWidth { get => GetValue(FlagGroupWidthProperty); set => SetValue(FlagGroupWidthProperty, value); }
        public ModLaunchOptionsView()
        {
            InitializeComponent();
            SizeChanged += (_, _) =>
            {
                var padding = Bounds.Width < 480 ? 12 : 24;
                LaunchLayout.Margin = new Thickness(padding, 20);
            };
            FlagList.SizeChanged += (_, _) =>
            {
                var available = Math.Max(90, FlagList.Bounds.Width);
                var columns = available >= 1150 ? 3 : available >= 740 ? 2 : 1;
                FlagGroupWidth = Math.Max(80, Math.Floor(available / columns) - 10);
            };
        }
    }
}
