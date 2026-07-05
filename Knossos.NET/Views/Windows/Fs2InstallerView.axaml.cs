using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace Knossos.NET.Views
{
    public partial class Fs2InstallerView : KnossosWindow
    {
        public Fs2InstallerView()
        {
            //InitializeComponent();
            AvaloniaXamlLoader.Load(this);
        }
    }
}
