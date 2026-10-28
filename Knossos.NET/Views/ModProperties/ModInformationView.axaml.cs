using Avalonia;
using Avalonia.Controls;
using System;

namespace Knossos.NET.Views
{
    public partial class ModInformationView : UserControl
    {
        public static readonly StyledProperty<double> ThumbnailWidthProperty =
            AvaloniaProperty.Register<ModInformationView, double>(nameof(ThumbnailWidth), 150);
        public static readonly StyledProperty<double> ThumbnailHeightProperty =
            AvaloniaProperty.Register<ModInformationView, double>(nameof(ThumbnailHeight), 95);
        public double ThumbnailWidth { get => GetValue(ThumbnailWidthProperty); set => SetValue(ThumbnailWidthProperty, value); }
        public double ThumbnailHeight { get => GetValue(ThumbnailHeightProperty); set => SetValue(ThumbnailHeightProperty, value); }

        public ModInformationView()
        {
            InitializeComponent();
            SizeChanged += (_, _) => UpdateResponsiveLayout();
            GalleryPanel.SizeChanged += (_, _) => UpdateThumbnails();
        }

        private void UpdateResponsiveLayout()
        {
            var narrow = Bounds.Width < 940;
            var padding = Bounds.Width < 480 ? 12 : 24;
            InformationLayout.Classes.Set("narrow", Bounds.Width < 300);
            InformationLayout.Margin = new Thickness(padding, 20);
            BannerFrame.Height = Math.Clamp((Bounds.Width - padding * 2) / 4.8, 100, 270);
            DetailsLayout.ColumnDefinitions[1].Width = new GridLength(narrow ? 0 : 360);
            Grid.SetColumn(GalleryPanel, narrow ? 0 : 1);
            Grid.SetRow(GalleryPanel, narrow ? 1 : 0);
            DescriptionPanel.Margin = new Thickness(0, 0, narrow ? 0 : 24, narrow ? 20 : 0);
            MetadataPanel.ItemWidth = Bounds.Width < 520 ? Math.Max(60, Bounds.Width - padding * 2 - 26) : 215;
            UpdateThumbnails();
        }

        private void UpdateThumbnails()
        {
            var width = Math.Max(100, GalleryPanel.Bounds.Width - 26);
            var columns = width < 280 ? 1 : width > 640 ? 3 : 2;
            ThumbnailWidth = Math.Max(80, Math.Floor(width / columns) - 8);
            ThumbnailHeight = ThumbnailWidth * 9 / 16;
        }
    }
}
