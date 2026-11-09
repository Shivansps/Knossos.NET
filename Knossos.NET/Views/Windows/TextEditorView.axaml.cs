using Knossos.NET.ViewModels;
using System.Threading.Tasks;

namespace Knossos.NET.Views
{
    public partial class TextEditorView : KnossosWindow
    {
        public TextEditorView() { InitializeComponent(); }
        protected override Task<bool> OnClosingAsync() => Task.FromResult((DataContext as TextEditorViewModel)?.RequestClose() ?? true);
    }
}
