using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;
using System;
using System.Linq;

namespace Knossos.NET.Views
{
    public static class DialogHost
    {
        public enum Layer { Window, MessageBox }

        public static Border? Show(Control dialog, Layer layer = Layer.Window, Action? onDismiss = null)
        {
            var host = GetHost(layer);
            if (host == null)
            {
                Log.Add(Log.LogSeverity.Error, "DialogHost.Show()", "Unable to find the dialog overlay panel.");
                return null;
            }

            var wrapper = Wrap(dialog, onDismiss);
            host.Children.Add(wrapper);
            host.IsHitTestVisible = true;
            return wrapper;
        }

        public static void Hide(Control dialogOrWrapper)
        {
            foreach (var host in AllHosts())
            {
                if (host == null) continue;

                var entry = host.Children.FirstOrDefault(c =>
                    c == dialogOrWrapper ||
                    (c is Border b && b.Child == dialogOrWrapper));

                if (entry != null)
                {
                    host.Children.Remove(entry);
                    host.IsHitTestVisible = host.Children.Count > 0;
                    return;
                }
            }
        }

        private static Panel? GetHost(Layer layer)
        {
            var name = layer == Layer.MessageBox ? "MessageBoxOverlay" : "WindowOverlay";
            return MainView.instance?
                .GetVisualDescendants()
                .OfType<Panel>()
                .FirstOrDefault(x => x.Name == name);
        }

        private static Panel?[] AllHosts() =>
            new[] { GetHost(Layer.MessageBox), GetHost(Layer.Window) };

        private static Border Wrap(Control content, Action? onDismiss)
        {
            var root = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(0x80, 0x00, 0x00, 0x00)),
                Child = content,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch
            };
            //root.PointerPressed += (_, __) => onDismiss?.Invoke();
            return root;
        }
    }
}