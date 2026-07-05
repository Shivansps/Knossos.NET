using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Markup.Xaml;
using System;
using System.ComponentModel;

namespace Knossos.NET.Views
{
    public partial class ModDetailsView : KnossosWindow
    {
        private Carousel _carousel;
        private Button _left;
        private Button _right;

        public ModDetailsView()
        {
            //InitializeComponent();
            AvaloniaXamlLoader.Load(this);
            this.Closing += ModDetailsView_StopTTS;

            _carousel = this.Get<Carousel>("carousel");
            _left = this.Get<Button>("left");
            _right = this.Get<Button>("right");
            _left.Click += (s, e) =>
            {
                if (_carousel.ItemCount > 0 && _carousel.SelectedIndex == 0)
                    _carousel.SelectedIndex = _carousel.ItemCount - 1;
                else
                    _carousel.Previous();
            };
            _right.Click += (s, e) =>
            {
                if (_carousel.ItemCount > 0 && _carousel.SelectedIndex == _carousel.ItemCount - 1)
                    _carousel.SelectedIndex = 0;
                else
                    _carousel.Next();
            };
        }

        private void ModDetailsView_StopTTS(object? sender, CancelEventArgs e)
        {
            Knossos.Tts(string.Empty);
        }
    }
}
