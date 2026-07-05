using Avalonia;
using Avalonia.Platform;
using System;

namespace Knossos.NET
{
    internal class Program
    {
        // Initialization code. Don't use any Avalonia, third-party APIs or any
        // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
        // yet and stuff might break.
        [STAThread]
        public static void Main(string[] args)
        {
            bool softwareRendering = true;
            bool isQuickLaunch = false;

            //Check app args
            foreach (var arg in args)
            {
                if (arg.ToLower() == "-hardware")
                {
                    softwareRendering = false;
                }
                if (arg.ToLower() == "-playmod" || arg.ToLower() ==  "-tool")
                {
                    isQuickLaunch = true;
                }
            }


            if (isQuickLaunch)
            {
                Knossos.StartUp(true, false);
            }
            else
            {
                //Check enviroment variables
                var renderMode = KnUtils.GetEnvironmentVariable("KNET_RENDER_MODE");

                if (renderMode != null && renderMode.ToLower() == "hardware")
                {
                    softwareRendering = false;
                }

                //Start App
                if (softwareRendering)
                {
                    BuildAvaloniaAppSoftware().StartWithClassicDesktopLifetime(args);
                }
                else
                {
                    BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
                }
            }
        }

        // Avalonia configuration, don't remove; also used by visual designer.
        public static AppBuilder BuildAvaloniaApp()
        {
            //Default
            return AppBuilder.Configure<App>()
                   .UsePlatformDetect()
                   .LogToTrace();
        }

        public static AppBuilder BuildAvaloniaAppSoftware()
        {
            //Disable Hardware Renderer
            return AppBuilder.Configure<App>()
                    .UsePlatformDetect()
                    .LogToTrace()
                    .With(new Win32PlatformOptions
                    {   //Windows
                        RenderingMode = new[]
                        {
                            Win32RenderingMode.Software
                        }
                    })
                    .With(new X11PlatformOptions
                    {   //Linux
                        RenderingMode = new[]
                        {
                            X11RenderingMode.Software
                        }
                    })
                    .With(new AvaloniaNativePlatformOptions
                    {   //MacOS
                        RenderingMode = new[]
                        {
                            AvaloniaNativeRenderingMode.Software
                        }
                    });
        }
    }
}
