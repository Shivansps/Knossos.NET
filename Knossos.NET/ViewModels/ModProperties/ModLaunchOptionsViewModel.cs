using Knossos.NET.Models;

namespace Knossos.NET.ViewModels
{
    /// <summary>Reuses the existing settings behavior inside the properties navigation.</summary>
    public class ModLaunchOptionsViewModel : ModSettingsViewModel
    {
        public ModLaunchOptionsViewModel() { }
        public ModLaunchOptionsViewModel(Mod mod, ModCardViewModel? card = null) : base(mod, card) { }
    }
}
