using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace Knossos.NET.Views;
public partial class ReportModView : KnossosWindow
{
    public ReportModView()
    {
        //InitializeComponent();
        AvaloniaXamlLoader.Load(this);
    }
}