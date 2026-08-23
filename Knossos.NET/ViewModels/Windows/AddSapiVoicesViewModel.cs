using Knossos.NET.Views;
using System;
using System.Diagnostics;
using System.IO;

namespace Knossos.NET.ViewModels
{
    /// <summary>
    /// Add SapiVoices Windows View Model
    /// </summary>
    public partial class AddSapiVoicesViewModel : ViewModelBase
    {

        internal void OpenSpeechSettings()
        {
            using (var speech = new Process())
            {
                speech.StartInfo.FileName = "ms-settings:speech";
                speech.StartInfo.UseShellExecute = true;
                speech.Start();
            }
        }

        internal void CopyKeys()
        {
            try
            {
                if (!File.Exists(KnUtils.GetKnossosDataFolderPath() + @"\sapi_tokens_backup.reg"))
                {
                    using (var back = new Process())
                    {
                        back.StartInfo.FileName = "reg";
                        back.StartInfo.Arguments = @"export HKLM\SOFTWARE\Microsoft\Speech\Voices\Tokens " + KnUtils.GetKnossosDataFolderPath() + @"\sapi_tokens_backup.reg";
                        back.StartInfo.UseShellExecute = true;
                        back.Start();
                        back.WaitForExit();
                    }
                }

                //Export OneCore Keys
                using (var export = new Process())
                {
                    export.StartInfo.FileName = "reg";
                    export.StartInfo.Arguments = @"export HKLM\SOFTWARE\Microsoft\Speech_OneCore\Voices\Tokens " + KnUtils.GetKnossosDataFolderPath() + @"\one_core.reg";
                    export.StartInfo.UseShellExecute = true;
                    export.Start();
                    export.WaitForExit();
                }

                //Change the reg key
                if (File.Exists(KnUtils.GetKnossosDataFolderPath() + @"\one_core.reg"))
                {
                    var keys = File.ReadAllText(KnUtils.GetKnossosDataFolderPath() + @"\one_core.reg");
                    keys = keys.Replace(@"\Speech_OneCore\", @"\Speech\");
                    keys = keys.Replace(@"SayAsSupport", @"unsupportedentry");
                    File.WriteAllText(KnUtils.GetKnossosDataFolderPath() + @"\one_core_modified.reg", keys);
                }

                //Import New Keys
                using (var import = new Process())
                {
                    import.StartInfo.FileName = "reg";
                    import.StartInfo.Arguments = @"import " + KnUtils.GetKnossosDataFolderPath() + @"\one_core_modified.reg";
                    import.StartInfo.UseShellExecute = true;
                    import.StartInfo.Verb = "runas";
                    import.Start();
                    import.WaitForExit();
                }

                File.Delete(KnUtils.GetKnossosDataFolderPath() + @"\one_core.reg");
                File.Delete(KnUtils.GetKnossosDataFolderPath() + @"\one_core_modified.reg");

                MessageBox.Show(MainWindow.instance, "Process completed. A backup of the original SAPI keys is saved here: "+KnUtils.GetKnossosDataFolderPath() + @"\sapi_tokens_backup.reg", "Registry keys copy", MessageBox.MessageBoxButtons.OK);
            }
            catch (Exception ex)
            {
                MessageBox.Show(MainWindow.instance,ex.Message.ToString(),"An error has ocurred",MessageBox.MessageBoxButtons.OK);
            }
        }
    }
}
