using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Knossos.NET.ViewModels;
using System;

namespace Knossos.NET.Views
{
    public partial class DebugFiltersView : KnossosWindow
    {
        public DebugFiltersView()
        {
            //InitializeComponent();
            AvaloniaXamlLoader.Load(this);
        }
    }
}