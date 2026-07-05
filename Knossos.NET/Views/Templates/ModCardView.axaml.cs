using Avalonia.Controls;
using Knossos.NET.ViewModels;

namespace Knossos.NET.Views
{
    public partial class ModCardView : UserControl
    {
        public ModCardView()
        {
            InitializeComponent();
        }

        private void StackPanel_PointerPressed(object? sender, Avalonia.Input.PointerPressedEventArgs e)
        {
            if (!KnUtils.IsAndroid)
                return;
            var dt = this.DataContext as ModCardViewModel;
            var overlayPanel = this.FindControl<StackPanel>("Overlay");
            if (overlayPanel != null)
            {
                if (overlayPanel.Opacity == 0)
                {
                    overlayPanel.Opacity = 0.8;
                    if (dt != null)
                    {
                        dt.androidButtonsEnabled = true;
                    }
                }
                else
                {
                    overlayPanel.Opacity = 0;
                    if (dt != null)
                    {
                        dt.androidButtonsEnabled = false;
                    }
                }
            }
        }
    }
}
