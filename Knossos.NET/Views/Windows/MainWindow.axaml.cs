using Avalonia.Controls;
using Knossos.NET.ViewModels;

namespace Knossos.NET.Views
{
    public partial class MainWindow : Window
    {
        public static MainWindow? instance;

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

        protected override void OnClosing(WindowClosingEventArgs e)
        {
            // Everything that has to happen before the window goes away is synchronous and
            // already runs on the UI thread, so do it here and let the close proceed.
            // Do NOT cancel the close and re-call Close() later: on Linux the X11 session
            // manager asks the app to close during logout/reboot, and a cancelled first
            // close is reported back as "the app refused", which cancels the whole logout.
            Knossos.Tts(string.Empty);
            MainViewModel.Instance?.GlobalSettingsView?.CommitPendingChanges();
            Knossos.globalSettings.SaveSettingsOnAppClose();
            base.OnClosing(e);
        }
    }
}
