using Avalonia.Controls;
using CommunityToolkit.Mvvm.ComponentModel;
using Knossos.NET.Models;
using Knossos.NET.Views;

namespace Knossos.NET.ViewModels
{
    /// <summary>
    /// Report Mod View Model
    /// This dialog allow a user to report a mod
    /// User must be logged into to create a mod report in nebula
    /// </summary>
    public partial class ReportModViewModel : ViewModelBase
    {
        [ObservableProperty]
        internal string modName = string.Empty;

        [ObservableProperty]
        internal string modVersion = string.Empty;

        [ObservableProperty]
        internal string reasonString = string.Empty;

        private Mod? mod;
        private KnossosWindow? dialog;

        public ReportModViewModel()
        {
        }

        public ReportModViewModel(Mod mod, KnossosWindow dialog)
        {
            this.dialog = dialog;
            this.mod = mod;
            modName = mod.title;
            modVersion = mod.version;
        }

        /// <summary>
        /// Upload mod report to Nebula
        /// User must be logged in
        /// </summary>
        internal async void Submit()
        {
            if(ReasonString.Trim() != string.Empty && mod != null)
            {
                var reply = await Nebula.ReportMod(mod, ReasonString);
                if(reply)
                {
                    await MessageBox.Show(MainWindow.instance!, "Your report has been submitted to fsnebula, we will act as soon as possible.", "Mod Report OK", MessageBox.MessageBoxButtons.OK);
                    if(dialog != null)
                    {
                        dialog.Close();
                    }
                }
                else
                {
                    await MessageBox.Show(MainWindow.instance!, "An error has ocurred while reporting a mod.", "Mod Report Error", MessageBox.MessageBoxButtons.OK);
                }
            }
            else
            {
                await MessageBox.Show(MainWindow.instance!, "Please provide a reason for your report.", "Reason is empty", MessageBox.MessageBoxButtons.OK);
            }
        }

        internal void Cancel()
        {
            if (dialog != null)
            {
                dialog.Close();
            }
        }
    }
}
