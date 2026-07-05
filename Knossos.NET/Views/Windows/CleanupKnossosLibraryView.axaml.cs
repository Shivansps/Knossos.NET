using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Knossos.NET.ViewModels;
using System;

namespace Knossos.NET.Views
{
    public partial class CleanupKnossosLibraryView : KnossosWindow
    {
        public CleanupKnossosLibraryView()
        {
            //InitializeComponent();
            AvaloniaXamlLoader.Load(this);
            DataContext = new CleanupKnossosLibraryViewModel();

            ((CleanupKnossosLibraryViewModel)DataContext).OnRequestClose += (s, ev) => Close();
            ((CleanupKnossosLibraryViewModel)DataContext).LoadRemovableMods();
        }
    }
}
