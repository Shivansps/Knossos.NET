using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace Knossos.NET.Views
{
    public partial class QuickSetupView : KnossosWindow
    {
        public QuickSetupView()
        {
            //InitializeComponent();
            AvaloniaXamlLoader.Load(this);
        }
    }
}
