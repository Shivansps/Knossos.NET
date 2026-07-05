using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace Knossos.NET.Views.Windows
{
    public partial class AddUserBuildView : KnossosWindow
    {
        public AddUserBuildView()
        {
            //InitializeComponent();
            AvaloniaXamlLoader.Load(this);
        }
    }
}
