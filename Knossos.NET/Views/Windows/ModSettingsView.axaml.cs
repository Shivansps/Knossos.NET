using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Knossos.NET.ViewModels;
using System.Threading.Tasks;

namespace Knossos.NET.Views
{
    public partial class ModSettingsView : KnossosWindow
    {
        private bool canClose = false;

        public ModSettingsView()
        {
            //InitializeComponent();
            AvaloniaXamlLoader.Load(this);
        }

        protected override async Task<bool> OnClosingAsync()
        {
            if (DataContext is ModSettingsViewModel vm)
            {
                if (!canClose)
                {
                    await Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        vm.SaveSettingsCommand();
                        canClose = true;
                    });
                }
            }
            return canClose;
        }
    }
}
