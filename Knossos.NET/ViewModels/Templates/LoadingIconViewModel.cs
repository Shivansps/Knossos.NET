using CommunityToolkit.Mvvm.ComponentModel;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Knossos.NET.ViewModels
{
    public partial class LoadingIconViewModel : ViewModelBase
    {
        [ObservableProperty]
        public bool animate  = false;
    }
}
