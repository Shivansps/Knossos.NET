using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace Knossos.NET.Views.Windows;

public partial class ServerCreatorView : KnossosWindow
{
    public ServerCreatorView()
    {
        //InitializeComponent();
        AvaloniaXamlLoader.Load(this);
    }
}