using Android.Content;
using Android.Content.PM;
using Android.Views;
using AndroidX.Core.Content;
using AndroidX.Core.View;
using Avalonia;
using Avalonia.Android;
using Avalonia.Threading;
using Knossos.NET.Classes;
using AndroidNet = global::Android.Net;

namespace Knossos.NET.Android;

[Activity(
    Label = "Knossos.NET",
    Theme = "@style/MyTheme.NoActionBar",
    Icon = "@drawable/icon",
    MainLauncher = true,
    ConfigurationChanges = ConfigChanges.Orientation
        | ConfigChanges.ScreenSize
        | ConfigChanges.UiMode,
    ScreenOrientation = ScreenOrientation.SensorLandscape)]
public class MainActivity : AvaloniaMainActivity<App>
{
    private const string ApkMimeType = "application/vnd.android.package-archive";
    private static string? pendingApkInstallPath;

    public static MainActivity? Instance { get; private set; }

    protected override AppBuilder CustomizeAppBuilder(AppBuilder builder)
    {
        return base.CustomizeAppBuilder(builder)
            .WithInterFont();
    }

    private void hideSystemUI()
    {
        WindowCompat.SetDecorFitsSystemWindows(Window, false);

        var windowInsetsController = WindowCompat.GetInsetsController(Window, Window?.DecorView);

        if (windowInsetsController != null)
        {
            // Hide both navigation and status bars
            windowInsetsController.Hide(WindowInsetsCompat.Type.SystemBars());

            // Or only navigation bars:
            //windowInsetsController.Hide(WindowInsetsCompat.Type.NavigationBars());

            // Set behavior to show bars temporarily on swipe
            windowInsetsController.SystemBarsBehavior = WindowInsetsControllerCompat.BehaviorShowTransientBarsBySwipe;
        }
    }

    public override void OnWindowFocusChanged(bool hasFocus)
    {
        base.OnWindowFocusChanged(hasFocus);
        if(hasFocus)
            hideSystemUI();
    }

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        Window?.AddFlags(WindowManagerFlags.KeepScreenOn);
        //Window?.AddFlags(WindowManagerFlags.Fullscreen);

        base.OnCreate(savedInstanceState);
        Instance = this;
        AndroidHelper.ShareTextAsyncFunc = ShareTextFileAndroidAsync;
        AndroidHelper.ShareFileAsyncFunc = OpenFileExternalApp;
        AndroidHelper.OpenUrlAsyncFunc = OpenExternalURLAsync;
        AndroidHelper.InstallApkAsyncFunc = InstallApkAndroidAsync;
        hideSystemUI();
    }

    protected override void OnResume()
    {
        base.OnResume();
        hideSystemUI();

        if (pendingApkInstallPath != null && PackageManager?.CanRequestPackageInstalls() == true)
        {
            var apkPath = pendingApkInstallPath;
            pendingApkInstallPath = null;
            OpenPackageInstaller(apkPath);
        }
    }

    private static async Task InstallApkAndroidAsync(string fullPath)
    {
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            if (!System.IO.File.Exists(fullPath) || Instance == null)
                return;

            try
            {
                if (Instance.PackageManager?.CanRequestPackageInstalls() != true)
                {
                    pendingApkInstallPath = fullPath;
                    var packageUri = AndroidNet.Uri.Parse($"package:{Instance.PackageName}");
                    var permissionIntent = new Intent(global::Android.Provider.Settings.ActionManageUnknownAppSources, packageUri);
                    Instance.StartActivity(permissionIntent);
                    return;
                }

                OpenPackageInstaller(fullPath);
            }
            catch (Exception ex)
            {
                pendingApkInstallPath = null;
                Log.Add(Log.LogSeverity.Error, "MainActivity.InstallApkAndroidAsync", ex);
            }
        });
    }

    private static void OpenPackageInstaller(string fullPath)
    {
        if (!System.IO.File.Exists(fullPath) || Instance == null)
            return;

        try
        {
            var javaFile = new Java.IO.File(fullPath);
            var authority = $"{Instance.PackageName}.fileprovider";
            var uri = FileProvider.GetUriForFile(Instance, authority, javaFile);

            var intent = new Intent(Intent.ActionView);
            intent.SetDataAndType(uri, ApkMimeType);
            intent.AddFlags(ActivityFlags.GrantReadUriPermission);
            Instance.StartActivity(intent);
        }
        catch (Exception ex)
        {
            Log.Add(Log.LogSeverity.Error, "MainActivity.OpenPackageInstaller", ex);
        }
    }

    private static async Task ShareTextFileAndroidAsync(string texto)
    {
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            if (Instance == null) return;

            var intent = new Intent(Intent.ActionSend);
            intent.SetType("text/plain");
            intent.PutExtra(Intent.ExtraText, texto);
            intent.AddFlags(ActivityFlags.NewTask);

            try
            {
                var chooser = Intent.CreateChooser(intent, "Open text with...");
                Instance.StartActivity(chooser);
            }
            catch (Exception ex)
            {
                Log.Add(Log.LogSeverity.Error, "MainActivity.ShareTextFileAndroidAsync", ex);
            }
        });
    }

    private static async Task OpenFileExternalApp(string fullPath, string mimetype)
    {
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            if (!System.IO.File.Exists(fullPath) || Instance == null)
            return;

            try
            {
                var javaFile = new Java.IO.File(fullPath);
                string authority = $"{Instance.PackageName}.fileprovider";

                var uri = FileProvider.GetUriForFile(Instance, authority, javaFile);

                var intent = new Intent(Intent.ActionView);
                intent.SetDataAndType(uri, mimetype);
                intent.AddFlags(ActivityFlags.GrantReadUriPermission);
                intent.AddFlags(ActivityFlags.NewTask);

                Instance.StartActivity(Intent.CreateChooser(intent, "Open file with..."));
            }
            catch (Exception ex)
            {
                Log.Add(Log.LogSeverity.Error, "MainActivity.OpenFileExternalApp", ex);
            }
        });
    }

    private static async Task OpenExternalURLAsync(string url)
    {
        if (string.IsNullOrWhiteSpace(url)) return;

        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            if (Instance == null) return;

            try
            {
                if (!url.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                    url = "https://" + url.Trim();

                var uri = AndroidNet.Uri.Parse(url);
                var intent = new Intent(Intent.ActionView, uri);
                intent.AddFlags(ActivityFlags.NewTask);

                Instance.StartActivity(intent);
            }
            catch (Exception ex)
            {
                Log.Add(Log.LogSeverity.Error, "MainActivity.OpenExternalURLAsync", ex);
            }
        });
    }
}
