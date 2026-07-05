using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Knossos.NET.ViewModels;

namespace Knossos.NET.Views
{
    public partial class MainWindow : Window
    {
        public static MainWindow? instance;
        private static bool canClose = false;

        public MainWindow()
        {
            instance = this;
            InitializeComponent();
        }

        /// <summary>
        /// Change size of the main window
        /// </summary>
        /// <param name="width"></param>
        /// <param name="height"></param>
        public void SetSize(double? width, double? height)
        {
            if(width.HasValue)
                this.Width = width.Value;
            if(height.HasValue)
                this.Height = height.Value;
        }

        protected override async void OnClosing(WindowClosingEventArgs e)
        {
            //Intercept closing, do stuff, then re-call close
            if (!canClose)
            {
                e.Cancel = true;

                await Dispatcher.UIThread.InvokeAsync(() => {
                    Knossos.Tts(string.Empty);
                    MainViewModel.Instance?.GlobalSettingsView?.CommitPendingChanges();
                    Knossos.globalSettings.SaveSettingsOnAppClose();
                    canClose = true;
                });

                if (canClose) 
                    this.Close(); 
            }
            base.OnClosing(e);
        }
    }
}
