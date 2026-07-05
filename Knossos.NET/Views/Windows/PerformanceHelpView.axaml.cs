using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace Knossos.NET.Views
{
    public partial class PerformanceHelpView : KnossosWindow
    {
        public PerformanceHelpView()
        {
            //InitializeComponent();
            AvaloniaXamlLoader.Load(this);
        }
    }
}
