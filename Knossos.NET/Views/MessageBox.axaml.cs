using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia;
using Avalonia.Layout;
using System;
using System.Threading.Tasks;

namespace Knossos.NET.Views
{
    /// <summary>
    /// Knossos Messagebox. Se muestra como tarjeta sobre el overlay (DialogHost),
    /// en la capa MessageBox para que quede por encima de cualquier ventana abierta.
    /// No es thread-safe: llamar desde el hilo de UI.
    /// </summary>
    public partial class MessageBox : UserControl
    {
        private readonly Border _card;

        public enum MessageBoxButtons
        {
            OK,
            OKCancel,
            YesNo,
            YesNoCancel,
            Continue,
            ContinueCancel,
            Details,
            DetailsOKCancel,
            DetailsContinueCancel,
            DontWarnAgainOK,
            ContinueCancelSkipVersion,
        }

        public enum MessageBoxResult
        {
            OK,
            Cancel,
            Yes,
            No,
            Continue,
            Details,
            DontWarnAgain,
            SkipVersion
        }

        private readonly TextBlock _title;
        private readonly TextBlock _body;
        private readonly StackPanel _buttonsHostPanel;

        public string Title { get => _title.Text!; set => _title.Text = value; }
        public string Text { get => _body.Text!; set => _body.Text = value; }

        public MessageBox()
        {
            AvaloniaXamlLoader.Load(this);
            _title = this.FindControl<TextBlock>("TitleText")!;
            _body = this.FindControl<TextBlock>("BodyText")!;
            _buttonsHostPanel = this.FindControl<StackPanel>("ButtonsHostPanel")!;
            _card = this.FindControl<Border>("CardBorder")!;
        }

        public void UseWindowChrome()
        {
            _card.Margin = new Thickness(0);
            _card.CornerRadius = new CornerRadius(0);
            _card.BorderThickness = new Thickness(0);
            _card.BoxShadow = default;
            _card.HorizontalAlignment = HorizontalAlignment.Stretch;
            _card.VerticalAlignment = VerticalAlignment.Stretch;
            _title.IsVisible = false;
        }

        public static Task<MessageBoxResult> Show(Window? parent, string text, string title, MessageBoxButtons buttons)
        {
            var tcs = new TaskCompletionSource<MessageBoxResult>();
            var view = new MessageBox { Title = title, Text = text };

            bool overlay = Knossos.globalSettings.singleViewMode || KnUtils.IsAndroid || KnUtils.IsBrowser;

            Window? window = overlay ? null : new Window
            {
                Title = title,
                Content = view,
                SizeToContent = SizeToContent.WidthAndHeight,
                CanResize = false,
                ShowInTaskbar = false,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
            };

            Action dismiss = overlay
                ? () => DialogHost.Hide(view)
                : () => window!.Close();

            void AddButton(string caption, MessageBoxResult r, bool isDefault = false, string? classes = null, double? width = null)
            {
                var b = new Button { Content = caption, MinWidth = width ?? 100 };
                if (!string.IsNullOrEmpty(classes)) b.Classes.Add(classes!);
                b.Click += (_, __) => { tcs.TrySetResult(r); dismiss(); };
                view._buttonsHostPanel.Children.Add(b);
                if (isDefault) view.AttachedToVisualTree += (_, __) => b.Focus();
            }

            ButtonCreation(AddButton, buttons);

            if (overlay)
            {
                DialogHost.Show(view, DialogHost.Layer.MessageBox,
                    onDismiss: () => { if (tcs.TrySetResult(MessageBoxResult.Cancel)) DialogHost.Hide(view); });
            }
            else
            {
                view.UseWindowChrome();
                window!.Closed += (_, __) => tcs.TrySetResult(MessageBoxResult.Cancel);

                if (parent != null && parent.IsVisible)
                    window.ShowDialog(parent);
                else
                    window.Show();
            }

            return tcs.Task;
        }

        /// <summary>
        /// Do not use externally
        /// </summary>
        internal static void ButtonCreation(Action<string, MessageBoxResult, bool, string?, double?> addButtonMethod, MessageBoxButtons buttons)
        {
            if (buttons == MessageBoxButtons.OK || buttons == MessageBoxButtons.OKCancel || buttons == MessageBoxButtons.DetailsOKCancel)
                addButtonMethod("OK", MessageBoxResult.OK, true, "Accept", null);

            if (buttons == MessageBoxButtons.YesNo || buttons == MessageBoxButtons.YesNoCancel)
            {
                addButtonMethod("Yes", MessageBoxResult.Yes, false, "Accept", null);
                addButtonMethod("No", MessageBoxResult.No, true, "Cancel", null);
            }

            if (buttons == MessageBoxButtons.Continue || buttons == MessageBoxButtons.ContinueCancel || buttons == MessageBoxButtons.DetailsContinueCancel || buttons == MessageBoxButtons.ContinueCancelSkipVersion)
                addButtonMethod("Continue", MessageBoxResult.Continue, false, "Accept", null);

            if (buttons == MessageBoxButtons.OKCancel || buttons == MessageBoxButtons.YesNoCancel || buttons == MessageBoxButtons.ContinueCancel || buttons == MessageBoxButtons.DetailsOKCancel || buttons == MessageBoxButtons.DetailsContinueCancel || buttons == MessageBoxButtons.ContinueCancelSkipVersion)
                addButtonMethod("Cancel", MessageBoxResult.Cancel, true, "Cancel", null);

            if (buttons == MessageBoxButtons.Details || buttons == MessageBoxButtons.DetailsOKCancel || buttons == MessageBoxButtons.DetailsContinueCancel)
                addButtonMethod("Details", MessageBoxResult.Details, false, "Option", null);

            if (buttons == MessageBoxButtons.DontWarnAgainOK)
            {
                addButtonMethod("OK", MessageBoxResult.OK, true, "Accept", null);
                addButtonMethod("Don't warn again", MessageBoxResult.DontWarnAgain, false, "Option", 150);
            }

            if (buttons == MessageBoxButtons.ContinueCancelSkipVersion)
                addButtonMethod("Skip this version", MessageBoxResult.SkipVersion, false, "Option", 150);
        }
    }
}