using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Media;
using System;
using System.ComponentModel;
using System.Threading.Tasks;

namespace Knossos.NET.Views
{
    /// <summary>
    /// Window wrapper system.
    /// Use this instead of Avalonia Window to support both desktop (real Window)
    /// and single-view platforms like Android (overlay on MainView).
    ///
    /// On desktop: creates a native Window at runtime and sets this UserControl as its Content.
    /// On Android/Browser: wraps this UserControl in a chrome overlay displayed via DialogHost.
    ///
    /// Sizing on desktop:
    ///   Set MaxWidth/MaxHeight/MinWidth/MinHeight on the KnossosWindow in AXAML as usual.
    ///   Use WindowWidth/WindowHeight instead of Width/Height on the KnossosWindow AXAML.
    ///   The host Window picks them up automatically. SizeToContent works too.
    ///
    /// Sizing on Android overlay:
    ///   The overlay always fills MainView minus a fixed margin (20 px each side).
    ///   MaxWidth/MaxHeight are NOT set on the body — the chrome card handles sizing
    ///   and re-adjusts automatically if MainView is resized.
    ///   
    /// Note: For dialogs that are only intended to be used on desktop OS it is 
    /// recommended to just use a Window
    /// Note2: Avoid using Width/Height to set a fixed size.
    /// </summary>
    public partial class KnossosWindow : UserControl
    {
        public KnossosWindow()
        {
        }

        // ── Styled properties mirroring the Window API ──────────────────────

        public static readonly StyledProperty<string?> TitleProperty =
            AvaloniaProperty.Register<KnossosWindow, string?>(nameof(Title));
        public string? Title
        {
            get => GetValue(TitleProperty);
            set => SetValue(TitleProperty, value);
        }

        public static readonly StyledProperty<bool> CanCloseProperty =
            AvaloniaProperty.Register<KnossosWindow, bool>(nameof(CanClose), defaultValue: true);
        public bool CanClose
        {
            get => GetValue(CanCloseProperty);
            set => SetValue(CanCloseProperty, value);
        }

        public static readonly StyledProperty<bool> CanResizeProperty =
            AvaloniaProperty.Register<KnossosWindow, bool>(nameof(CanResize), defaultValue: true);
        public bool CanResize
        {
            get => GetValue(CanResizeProperty);
            set => SetValue(CanResizeProperty, value);
        }

        public static readonly StyledProperty<WindowIcon?> IconProperty =
            AvaloniaProperty.Register<KnossosWindow, WindowIcon?>(nameof(Icon));
        public WindowIcon? Icon
        {
            get => GetValue(IconProperty);
            set => SetValue(IconProperty, value);
        }

        public static readonly StyledProperty<WindowStartupLocation> WindowStartupLocationProperty =
            AvaloniaProperty.Register<KnossosWindow, WindowStartupLocation>(
                nameof(WindowStartupLocation), WindowStartupLocation.CenterOwner);
        public WindowStartupLocation WindowStartupLocation
        {
            get => GetValue(WindowStartupLocationProperty);
            set => SetValue(WindowStartupLocationProperty, value);
        }

        public static readonly StyledProperty<int> WindowWidthProperty =
            AvaloniaProperty.Register<KnossosWindow, int>(nameof(WindowWidth), 0);
        public int WindowWidth
        {
            get => GetValue(WindowWidthProperty);
            set => SetValue(WindowWidthProperty, value);
        }

        public static readonly StyledProperty<int> WindowHeightProperty =
            AvaloniaProperty.Register<KnossosWindow, int>(nameof(WindowHeight), 0);
        public int WindowHeight
        {
            get => GetValue(WindowHeightProperty);
            set => SetValue(WindowHeightProperty, value);
        }

        public static readonly StyledProperty<SizeToContent> SizeToContentProperty =
            AvaloniaProperty.Register<KnossosWindow, SizeToContent>(
                nameof(SizeToContent), SizeToContent.Manual);
        public SizeToContent SizeToContent
        {
            get => GetValue(SizeToContentProperty);
            set => SetValue(SizeToContentProperty, value);
        }

        public static readonly StyledProperty<bool> TopmostProperty =
            AvaloniaProperty.Register<KnossosWindow, bool>(nameof(Topmost), defaultValue: false);
        public bool Topmost
        {
            get => GetValue(TopmostProperty);
            set => SetValue(TopmostProperty, value);
        }

        // ── Dialog result & events ───────────────────────────────────────────

        public object? DialogResult { get; set; }

        public event EventHandler? Opened;
        public event EventHandler<CancelEventArgs>? Closing;
        public event EventHandler? Closed;

        // ── Public API ───────────────────────────────────────────────────────

        public void Show() => _ = ShowInternalAsync(KnUtils.GetTopLevel(), isDialog: false);
        public void Show(Window owner) => _ = ShowInternalAsync(owner, isDialog: false);
        public Task ShowDialog() => ShowInternalAsync(KnUtils.GetTopLevel(), isDialog: true);
        public Task ShowDialog<T>(Window? owner) => ShowInternalAsync(owner, isDialog: true);

        /// <summary>
        /// Override in subclasses to run async logic before closing (return false to cancel).
        /// </summary>
        protected virtual Task<bool> OnClosingAsync() => Task.FromResult(true);

        // ── Internal state ───────────────────────────────────────────────────

        private Window? _hostWindow;
        private Control? _overlayWrapper;
        private TaskCompletionSource<object?>? _tcs;
        private EventHandler<AvaloniaPropertyChangedEventArgs>? _mainViewBoundsHandler;

        // ── Close ────────────────────────────────────────────────────────────

        public async void Close()
        {
            var ce = new CancelEventArgs();
            if (!CanClose) ce.Cancel = true;
            Closing?.Invoke(this, ce);
            if (ce.Cancel) return;

            var ok = await OnClosingAsync().ConfigureAwait(true);
            if (!ok) return;

            if (_hostWindow != null)
            {
                _hostWindow.Close();
                return;
            }

			if (_overlayWrapper != null)
			{
				if (_mainViewBoundsHandler != null && MainView.instance != null)
				{
					MainView.instance.PropertyChanged -= _mainViewBoundsHandler;
					_mainViewBoundsHandler = null;
				}

				DialogHost.Hide(_overlayWrapper);
				_overlayWrapper = null;
				_tcs?.TrySetResult(DialogResult);
				_tcs = null;
				Closed?.Invoke(this, EventArgs.Empty);
			}
        }

        // ── Overlay chrome (Android / browser) ───────────────────────────────

        /// <summary>
        /// Builds the title-bar + close-button chrome around <paramref name="body"/>
        /// for the overlay mode.
        ///
        /// Key points:
        ///   - The outer card uses HorizontalAlignment/VerticalAlignment = Stretch so it
        ///     fills the semi-transparent backdrop. The 20 px Margin is the only visual gap.
        ///   - body also gets Stretch alignment so it fills the card.
        ///   - We do NOT set MaxWidth/MaxHeight on body. The Margin on the card is enough
        ///     to keep content away from screen edges, and it automatically adapts when
        ///     the host window is resized.
        /// </summary>
        private Control BuildOverlayChrome(Control body)
        {
            var titleText = new TextBlock
            {
                Margin = new Thickness(8, 0, 0, 0),
                FontWeight = FontWeight.SemiBold,
                Foreground = SolidColorBrush.Parse("Black"),
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
                Text = Title
            };

            var closeBtn = new Button
            {
                Content = "✕",
                Width = 60,
                Height = 30,
                Background = SolidColorBrush.Parse("Red"),
                Margin = new Thickness(0, 0, 4, 0),
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center
            };
            closeBtn.Click += (_, _) => Close();

            var titleBar = new Grid
            {
                Background = new SolidColorBrush(Color.FromRgb(240, 240, 240)),
                Height = 35,
                MinHeight = 35,
                MaxHeight = 35,
                ColumnDefinitions = new ColumnDefinitions("*, Auto")
            };
            titleBar.Children.Add(titleText);
            Grid.SetColumn(closeBtn, 1);
            titleBar.Children.Add(closeBtn);

            // Allow body to stretch to fill whatever space the card gives it
            body.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch;
            body.VerticalAlignment   = Avalonia.Layout.VerticalAlignment.Stretch;

            var layout = new Grid
            {
                RowDefinitions = new RowDefinitions("Auto, *"),
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch,
                VerticalAlignment   = Avalonia.Layout.VerticalAlignment.Stretch
            };
            Grid.SetRow(titleBar, 0);
            layout.Children.Add(titleBar);
            Grid.SetRow(body, 1);
            layout.Children.Add(body);
            body.MinHeight = 0;

            var card = new Border
            {
                Background  = new SolidColorBrush(Colors.Black),
                CornerRadius = new CornerRadius(10),
                Padding     = new Thickness(0),
                Margin      = new Thickness(10),
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch,
                VerticalAlignment   = Avalonia.Layout.VerticalAlignment.Stretch,
                Child = layout
            };

            return card;
        }

        // ── ShowInternalAsync ────────────────────────────────────────────────

        private async Task ShowInternalAsync(TopLevel? owner, bool isDialog)
        {
            if (Knossos.globalSettings.singleViewMode || KnUtils.IsAndroid || KnUtils.IsBrowser)
            {
                _tcs = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);

				var chrome = BuildOverlayChrome(this);
				_overlayWrapper = DialogHost.Show(chrome, DialogHost.Layer.Window, onDismiss: () => Close());

                // Subscribe to MainView resize so the chrome triggers a re-measure.
                // Avalonia layout propagates automatically via Stretch alignment, but this
                // ensures correctness without requiring System.Reactive.
                if (MainView.instance != null)
                {
                    _mainViewBoundsHandler = (_, e) =>
                    {
                        if (e.Property == BoundsProperty)
                            chrome.InvalidateMeasure();
                    };
                    MainView.instance.PropertyChanged += _mainViewBoundsHandler;
                }

                Opened?.Invoke(this, EventArgs.Empty);

                if (isDialog)
                    await _tcs.Task.ConfigureAwait(false);

                return;
            }

            // ── Desktop: create a real Window ────────────────────────────────

            var w = new Window
            {
                Content               = this,
                Title                 = Title ?? string.Empty,
                SizeToContent         = SizeToContent,
                Topmost               = Topmost,
                WindowStartupLocation = WindowStartupLocation,
                CanResize             = CanResize,
            };

            if (Icon != null) w.Icon = Icon;

            // Transfer sizing constraints from the KnossosWindow AXAML to the host Window.
            // Use explicit checks so we only override defaults that were intentionally set.
            if (!double.IsNaN(WindowHeight) && WindowHeight > 0) w.Height = WindowHeight;
            if (!double.IsNaN(WindowWidth) && WindowWidth > 0) w.Width = WindowWidth;
            if (MinWidth  > 0) w.MinWidth  = MinWidth;
            if (MinHeight > 0) w.MinHeight = MinHeight;
            if (!double.IsPositiveInfinity(MaxWidth)  && MaxWidth  > 0) w.MaxWidth  = MaxWidth;
            if (!double.IsPositiveInfinity(MaxHeight) && MaxHeight > 0) w.MaxHeight = MaxHeight;

            w.Closing += async (_, e) =>
            {
                var ce = new CancelEventArgs();
                if (!CanClose) ce.Cancel = true;
                Closing?.Invoke(this, ce);
                if (ce.Cancel) { e.Cancel = true; return; }

                var ok = await OnClosingAsync().ConfigureAwait(true);
                if (!ok) { e.Cancel = true; return; }
            };

            w.Opened += (_, _) => Opened?.Invoke(this, EventArgs.Empty);
            w.Closed += (_, _) => { Closed?.Invoke(this, EventArgs.Empty); _hostWindow = null; };

            _hostWindow = w;

            if (isDialog && owner is Window ownerWin)
                await w.ShowDialog(ownerWin);
            else if (isDialog && MainWindow.instance != null)
                await w.ShowDialog(MainWindow.instance);
            else
                w.Show();
        }
    }
}
