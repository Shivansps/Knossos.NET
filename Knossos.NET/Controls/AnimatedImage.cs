using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using Knossos.NET.Classes;
using System;
using System.Collections.Generic;
using System.IO;
using SystemPath = System.IO.Path;

namespace Knossos.NET.Controls
{
    /// <summary>
    /// Image control that auto-detects what its been given and animates accordingly:
    ///
    ///   - Path is an APNG (.png/.apng)       -> APNG animation
    ///   - Path is any other supported image  -> static image (jpg, bmp, webp…)
    ///   - Stream is set                      -> Loads any static image or animation from a stream, 
    ///                                           the Extension property must also be set
    ///   - Frames is set                      -> manual slideshow, bitmap list (overrides Path and Stream) 
    ///   - FrameDelays                        -> list of delay for each frame for the slideshow
    ///
    /// "Path" accepts either a regular file path or an "avares://" URI.
    /// Animation is driven by a single DispatcherTimer — when Playing=false the timer
    /// is stopped and no CPU is used until Playing flips back to true.
    ///
    /// For APNG/ANI a single WriteableBitmap is allocated once and re-written every frame.
    /// 
    /// Usage in XAML:
    ///   xmlns:c="using:VP.NET.GUI.Controls"
    ///   <c:AnimatedImage Path="avares://MyApp/Assets/anim.ani" Playing="True" />
    ///   <c:AnimatedImage Stream="{Binding MyStream}" Extension="{Binding ImageExt}" Playing="True" />
    ///   <c:AnimatedImage Path="C:/foo/cool.apng" />
    ///   <c:AnimatedImage Path="C:/foo/photo.jpg" />            <!-- static -->
    ///   <c:AnimatedImage Frames="{Binding MyBitmaps}"
    ///                    FrameDelays="{Binding MyDelays}"
    ///                    DefaultFrameDelay="120" />
    /// </summary>
    public class AnimatedImage : Image
    {
        // Styled properties

        public static readonly StyledProperty<string?> PathProperty =
            AvaloniaProperty.Register<AnimatedImage, string?>(nameof(Path));

        public static readonly StyledProperty<Stream?> StreamProperty =
            AvaloniaProperty.Register<AnimatedImage, Stream?>(nameof(Stream));

        public static readonly StyledProperty<string?> ExtensionProperty =
            AvaloniaProperty.Register<AnimatedImage, string?>(nameof(Extension));

        public static readonly StyledProperty<bool> PlayingProperty =
            AvaloniaProperty.Register<AnimatedImage, bool>(nameof(Playing), defaultValue: true);

        public static readonly StyledProperty<IList<Bitmap>?> FramesProperty =
            AvaloniaProperty.Register<AnimatedImage, IList<Bitmap>?>(nameof(Frames));

        public static readonly StyledProperty<IList<int>?> FrameDelaysProperty =
            AvaloniaProperty.Register<AnimatedImage, IList<int>?>(nameof(FrameDelays));

        public static readonly StyledProperty<int> DefaultFrameDelayProperty =
            AvaloniaProperty.Register<AnimatedImage, int>(nameof(DefaultFrameDelay), defaultValue: 100);

        /// <summary>File path or avares:// URI of the image to display.</summary>
        public string? Path
        {
            get => GetValue(PathProperty);
            set => SetValue(PathProperty, value);
        }

        /// <summary>Any stream, an extension must be provided in the Extension property. The stream will not be disposed.</summary>
        public Stream? Stream
        {
            get => GetValue(StreamProperty);
            set => SetValue(StreamProperty, value);
        }

        /// <summary>File extension (only used with stream).</summary>
        public string? Extension
        {
            get => GetValue(ExtensionProperty);
            set => SetValue(ExtensionProperty, value);
        }

        /// <summary>True to play the animation, false to pause it. Has no effect on static images.</summary>
        public bool Playing
        {
            get => GetValue(PlayingProperty);
            set => SetValue(PlayingProperty, value);
        }

        /// <summary>Optional manual frame collection. Takes priority over Path or Stream when set.</summary>
        public IList<Bitmap>? Frames
        {
            get => GetValue(FramesProperty);
            set => SetValue(FramesProperty, value);
        }

        /// <summary>Optional per-frame delays in milliseconds for manual mode. If null or shorter than Frames, DefaultFrameDelay is used.</summary>
        public IList<int>? FrameDelays
        {
            get => GetValue(FrameDelaysProperty);
            set => SetValue(FrameDelaysProperty, value);
        }

        /// <summary>Fallback delay in milliseconds for manual mode when FrameDelays is null or short.</summary>
        public int DefaultFrameDelay
        {
            get => GetValue(DefaultFrameDelayProperty);
            set => SetValue(DefaultFrameDelayProperty, value);
        }

        // ─────────── Internal state ───────────

        private enum Mode { None, Static, Apng, Ani, Manual }

        private Mode _mode = Mode.None;
        private DispatcherTimer? _timer;

        private APNGHelper.ApngFile? _apngFile;
        private APNGHelper.ApngComposer? _apngComposer;
        private int _manualIndex;
        private int _lastDelayMs = 100;
        private WriteableBitmap? _reusableBitmap;

        // Lifecycle

        protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
        {
            base.OnPropertyChanged(change);

            if (change.Property == PathProperty)
            {
                ReloadFromPath();
            }
            else if (change.Property == StreamProperty)
            {
                ReloadFromStream();
            }
            else if (change.Property == FramesProperty)
            {
                ReloadManual();
            }
            else if (change.Property == PlayingProperty)
            {
                if (Playing) StartTimer();
                else StopTimer();
            }
        }

        protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
        {
            base.OnAttachedToVisualTree(e);
            // If we already have content and we are meant to play, (re)start the timer.
            if (Playing) StartTimer();
        }

        protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
        {
            // Stop the timer so we dont fire ticks while invisible.
            // We deliberately keep the decoded state around so reattaching is cheap;
            // set Path = null (or Frames = null) to release the bitmap buffers.
            StopTimer();
            base.OnDetachedFromVisualTree(e);
        }

        // Loading

        private void DisposeAnimatedState()
        {
            StopTimer();
            _apngComposer?.Dispose();
            _apngComposer = null;
            _apngFile     = null;
            _reusableBitmap?.Dispose();
            _reusableBitmap = null;
            _manualIndex  = 0;
            _lastDelayMs  = 100;
            _mode         = Mode.None;
        }

        private void ReloadManual()
        {
            var frames = Frames;
            if (frames != null && frames.Count > 0)
            {
                DisposeAnimatedState();
                _mode         = Mode.Manual;
                _manualIndex  = 0;
                _lastDelayMs  = GetManualDelay(0);
                Source        = frames[0];

                if (frames.Count > 1 && Playing) StartTimer();
                return;
            }

            // Frames became null/empty: fall back to whatever Path says (if anything).
            DisposeAnimatedState();
            Source = null;
            ReloadFromPath();
        }

        private void ReloadFromStream()
        {
            if (Frames != null && Frames.Count > 0) return;

            DisposeAnimatedState();
            Source = null;

            string? ext = Extension;
            Stream? stream = Stream;
            if (stream == null || string.IsNullOrWhiteSpace(ext)) return;
            
            try
            {
                switch (ext)
                {
                    case "png":
                    case "apng":
                        if (TryIsApng(stream))
                            LoadApng(stream);
                        else
                            LoadStatic(stream);
                        break;

                    default:
                        LoadStatic(stream);
                        break;
                }
            }
            catch (Exception ex)
            {
                Log.Add(Log.LogSeverity.Error, "AnimatedImage.ReloadFromStream", ex);
            }
        }

        private void ReloadFromPath()
        {
            if (Frames != null && Frames.Count > 0) return;

            DisposeAnimatedState();
            Source = null;

            string? path = Path;
            if (string.IsNullOrWhiteSpace(path)) return;

            try
            {
                string ext = SystemPath.GetExtension(path).ToLowerInvariant();
                using Stream stream = OpenStream(path);

                switch (ext)
                {
                    case ".png":
                    case ".apng":
                        if (TryIsApng(stream))
                            LoadApng(stream);
                        else
                            LoadStatic(stream);
                        break;

                    default:
                        LoadStatic(stream);
                        break;
                }
            }
            catch (Exception ex)
            {
                Log.Add(Log.LogSeverity.Error, "AnimatedImage.ReloadFromPath", ex);
            }
        }

        private static Stream OpenStream(string path)
        {
            Stream raw;
            if (path.StartsWith("avares://", StringComparison.OrdinalIgnoreCase) ||
                path.StartsWith("resm:",    StringComparison.OrdinalIgnoreCase))
            {
                raw = AssetLoader.Open(new Uri(path));
            }
            else
            {
                raw = File.OpenRead(path);
            }

            // The helpers all do stream.Seek(0, ...), so make sure we hand them
            // a seekable stream regardless of where it came from.
            if (raw.CanSeek) return raw;

            var ms = new MemoryStream();
            using (raw) raw.CopyTo(ms);
            ms.Position = 0;
            return ms;
        }

        private static bool TryIsApng(Stream stream)
        {
            try { return APNGHelper.IsApng(stream); }
            catch { return false; }
        }

        private void LoadStatic(Stream stream)
        {
            stream.Seek(0, SeekOrigin.Begin);
            Source = new Bitmap(stream);
            _mode  = Mode.Static;
        }

        private void LoadApng(Stream stream)
        {
            stream.Seek(0, SeekOrigin.Begin);
            var apng = APNGHelper.ReadApng(stream);
            if (apng == null || apng.Frames.Count == 0)
            {
                Log.Add(Log.LogSeverity.Error, "AnimatedImage.LoadApng", "Failed to parse APNG.");
                return;
            }

            _apngFile       = apng;
            _apngComposer   = apng.CreateComposer();
            _reusableBitmap = _apngComposer.CreateMatchingBitmap();
            Source          = _reusableBitmap;
            _mode           = Mode.Apng;

            // Render the first frame immediately so we always show something,
            // even when Playing=false from the start.
            _apngComposer.RenderNextFrameTo(_reusableBitmap, out _lastDelayMs);
            InvalidateVisual();

            if (apng.Frames.Count > 1 && Playing) StartTimer();
        }

        // Animation loop

        private void StartTimer()
        {
            if (_mode != Mode.Apng && _mode != Mode.Ani && _mode != Mode.Manual) return;

            _timer ??= new DispatcherTimer();
            _timer.Tick -= OnTick;
            _timer.Tick += OnTick;
            _timer.Interval = TimeSpan.FromMilliseconds(Math.Max(1, _lastDelayMs));
            _timer.Start();
        }

        private void StopTimer()
        {
            if (_timer == null) return;
            _timer.Stop();
            _timer.Tick -= OnTick;
        }

        private void OnTick(object? sender, EventArgs e)
        {
            try
            {
                switch (_mode)
                {
                    case Mode.Apng:
                        if (_apngComposer != null && _reusableBitmap != null)
                        {
                            _apngComposer.RenderNextFrameTo(_reusableBitmap, out _lastDelayMs);
                            InvalidateVisual();
                        }
                        break;

                    case Mode.Manual:
                        var frames = Frames;
                        if (frames != null && frames.Count > 0)
                        {
                            _manualIndex = (_manualIndex + 1) % frames.Count;
                            Source = frames[_manualIndex];
                            _lastDelayMs = GetManualDelay(_manualIndex);
                        }
                        break;
                }
            }
            catch (Exception ex)
            {
                Log.Add(Log.LogSeverity.Error, "AnimatedImage.OnTick", ex);
                StopTimer();
                return;
            }

            if (_timer != null)
                _timer.Interval = TimeSpan.FromMilliseconds(Math.Max(1, _lastDelayMs));
        }

        private int GetManualDelay(int index)
        {
            var delays = FrameDelays;
            if (delays != null && index >= 0 && index < delays.Count)
                return Math.Max(1, delays[index]);
            return Math.Max(1, DefaultFrameDelay);
        }
    }
}
