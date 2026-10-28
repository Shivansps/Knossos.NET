using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using Knossos.NET.ViewModels;
using System;
using System.ComponentModel;
using System.Threading.Tasks;

namespace Knossos.NET.Views
{
    public partial class ModPropertiesView : KnossosWindow
    {
        private ModPropertiesViewModel? model;
        private IInputElement? previousFocus;

        public ModPropertiesView()
        {
            InitializeComponent();
            DataContextChanged += (_, _) =>
            {
                if (model != null) model.PropertyChanged -= OnModelChanged;
                model = DataContext as ModPropertiesViewModel;
                if (model != null)
                {
                    model.AttachView(this);
                    model.PropertyChanged += OnModelChanged;
                }
            };
            SizeChanged += (_, _) => UpdateLayoutWidth();
            KeyDown += OnKeyDown;
            Closed += (_, _) =>
            {
                if (model != null) { model.PropertyChanged -= OnModelChanged; model.Dispose(); }
            };
        }

        private void UpdateLayoutWidth()
        {
            // Avalonia uses logical pixels, including on high-density Android screens.
            var compact = Bounds.Width < 760;
            PropertiesLayout.ColumnDefinitions[0].Width = new GridLength(compact ? 116 : 224);
            PropertiesLayout.Classes.Set("compact", compact);
        }

        private void OnModelChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName != nameof(ModPropertiesViewModel.IsModalOpen)) return;
            if (model?.IsModalOpen == true)
            {
                previousFocus = TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement();
                Dispatcher.UIThread.Post(() => ModalClose.Focus());
            }
            else previousFocus?.Focus();
        }

        private void OnKeyDown(object? sender, KeyEventArgs e)
        {
            if (model?.IsModalOpen != true) return;
            if (e.Key == Key.Escape) { model.CloseModalCommand.Execute(null); e.Handled = true; }
            else if (model.IsMediaModal && !model.IsVideoModal && (e.Key == Key.Left || e.Key == Key.Right))
            {
                model.ChangeMediaCommand.Execute(e.Key == Key.Left ? "-1" : "1");
                e.Handled = true;
            }
        }

        protected override Task<bool> OnClosingAsync()
        {
            if (model?.IsSubmitting == true) return Task.FromResult(false);
            if (model?.IsModalOpen == true) { model.CloseModalCommand.Execute(null); return Task.FromResult(false); }
            model?.SaveOptions();
            return Task.FromResult(true);
        }
    }
}
