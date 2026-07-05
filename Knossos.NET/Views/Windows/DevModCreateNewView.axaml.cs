using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace Knossos.NET.Views;

public partial class DevModCreateNewView : KnossosWindow
{
    public DevModCreateNewView()
    {
        //InitializeComponent();
        AvaloniaXamlLoader.Load(this);
    }
}