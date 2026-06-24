#if ANDROID
using Android.App;
using System.Linq;
using Android.Content;
using Android.Opengl;
#endif
using Avalonia.Threading;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;

namespace Knossos.NET.Classes;

public static class AndroidHelper
{
    public static Func<string, Task>? ShareTextAsyncFunc { get; set; }
    public static Func<string, string, Task>? ShareFileAsyncFunc { get; set; }
    public static Func<string, Task>? OpenUrlAsyncFunc { get; set; }

    /// <summary>
    /// Open URL on external android web browser
    /// </summary>
    /// <param name="url"></param>
    /// <returns>task</returns>
    public static Task OpenUrlAsync(string url)
    {
        return OpenUrlAsyncFunc?.Invoke(url) ?? Task.CompletedTask;
    }

    /// <summary>
    /// Open text with a external android app
    /// </summary>
    /// <param name="text"></param>
    /// <returns>task</returns>
    public static Task ShareTextAsync(string text)
    {
        if (text.Length == 0 || ShareTextAsyncFunc == null)
            return Task.CompletedTask;
        return ShareTextAsyncFunc.Invoke(text);
    }

    /// <summary>
    /// Open a file with a external android app
    /// </summary>
    /// <param name="text"></param>
    /// <param name="mimeType"></param>
    /// <returns>task</returns>
    public static Task ShareFileAsync(string text, string mimeType = "text/plain")
    {
        if (text.Length == 0 || ShareFileAsyncFunc == null)
            return Task.CompletedTask;
        return ShareFileAsyncFunc.Invoke(text, mimeType);
    }

#if ANDROID
    /// <summary>
    /// App main storage inside the internal phone memory
    /// </summary>
    public static string? GetExternalAppFilesDir() => Application.Context.GetExternalFilesDir(null)?.AbsolutePath;

    /// <summary>
    /// Internal app storage, not accessible, only as fallback, should not be used
    /// </summary>
    public static string GetInternalAppFilesDir() => Application.Context.FilesDir!.AbsolutePath;

    /// <summary>
    /// List of app all external locations, SD, USB drives, etc
    /// </summary>
    public static string[] GetAllExternalAppFilesDirs()
        => (Application.Context.GetExternalFilesDirs(null) ?? System.Array.Empty<Java.IO.File>())
            .Where(f => f is not null)
            .Select(f => Path.Combine(f!.AbsolutePath, "library") )
            .ToArray();

    /// <summary>
    /// Default knossos library folder in android
    /// </summary>
    public static string GetDefaultLibraryDir()
    {
        var baseDir = GetExternalAppFilesDir() ?? GetInternalAppFilesDir();
        var library = Path.Combine(baseDir, "library");
        Directory.CreateDirectory(library);
        return library;
    }

    /// <summary>
    /// Default knossos directory folder in android, i dont belive this is ever used, just in case
    /// </summary>
    public static string GetDefaultKnetDir()
    {
        var baseDir = GetExternalAppFilesDir() ?? GetInternalAppFilesDir();
        var knossos = Path.Combine(baseDir, "knossos");
        Directory.CreateDirectory(knossos);
        return knossos;
    }

    /// <summary>
    /// Default knossos data dir in android, equivalent to the one on appdata in windows
    /// </summary>
    public static string GetDefaultKnetDataDir()
    {
        var baseDir = GetExternalAppFilesDir() ?? GetInternalAppFilesDir();
        var data = Path.Combine(baseDir, "data");
        Directory.CreateDirectory(data);
        return data;
    }

    /// <summary>
    /// Default FSO data path, the one thats on appdata on Windows
    /// </summary>
    public static string GetDefaultFSODataDir()
    {
        return GetInternalAppFilesDir();
    }

    /// <summary>
    /// Copy build .so files to internal app folder for execution
    /// </summary>
    private static void StageAllToInternal(string srcAbiDir, string dstAbiDir)
    {
        Directory.CreateDirectory(dstAbiDir);

        if (!Directory.Exists(srcAbiDir))
        {
            Log.Add(Log.LogSeverity.Error, "AndroidHelper.StageAllToInternal", "Source dir not found: " + srcAbiDir);
            return;
        }

        foreach (var src in Directory.EnumerateFiles(srcAbiDir, "*.so"))
        {
            string dst = System.IO.Path.Combine(dstAbiDir, System.IO.Path.GetFileName(src));
            var si = new FileInfo(src);
            var di = new FileInfo(dst);
            if (!di.Exists || di.Length != si.Length || si.LastWriteTimeUtc != di.LastWriteTimeUtc)
            {
                Log.Add(Log.LogSeverity.Information, "AndroidHelper.StageAllToInternal", "Copy " + src + " to "+dst);
                using (var input = File.OpenRead(src))
                using (var output = File.Create(dst))
                    input.CopyTo(output);

                File.SetLastWriteTime(dst, File.GetLastWriteTime(src));
            }
        }
    }

    /// <summary>
    /// Launch FSO, on Android.g
    /// All so files will be copied to app internal storage
    /// </summary>
    /// <param name="engineLibPath"></param>
    /// <param name="workingFolder"></param>
    /// <param name="cmdline"></param>
    public static void LaunchFSO(string engineLibPath, string? workingFolder, string cmdline)
    {
        try
        {
            var ctx = Application.Context;
            string dstAbiDir = System.IO.Path.Combine(ctx.FilesDir!.AbsolutePath, "natives");
            var fi = new FileInfo(engineLibPath);
            var folderPath = fi.Directory!.FullName;
            if (!folderPath.EndsWith("/"))
                folderPath += "/";
            StageAllToInternal(folderPath, dstAbiDir);
            var libName = fi.Name;
            var intent = new Intent();
            intent.SetClassName(ctx, "com.knossosnet.knossosnet.GameActivity");
            intent.AddFlags(ActivityFlags.NewTask);
            intent.PutExtra("engineLibName", Path.Combine(dstAbiDir, libName));
            if (workingFolder != null)
            {
                intent.PutExtra("workingFolder", workingFolder);
            }
            
            if (cmdline.Length > 0)
                intent.PutStringArrayListExtra("fsoArgs", cmdline.Split(" "));

            ctx.StartActivity(intent);
        }
        catch (Exception ex)
        {
            Log.Add(Log.LogSeverity.Error, "AndroidHelper.LaunchFSO", ex);
        }
    }

    static (bool s3tc, bool bc7, bool read) _gpuSupportOpenGL = (false, false, false);
    /// <summary>
    /// Creates a OpenGL ES context to check if the GPU supports S3TC and BPTC extensions
    /// </summary>
    /// <returns>true/false</returns>
    public static (bool s3tc, bool bc7, bool read) GpuSupportsBCnTexturesOpenGL()
    {
        if(_gpuSupportOpenGL.read)
            return _gpuSupportOpenGL;
        try
        {
            var display = EGL14.EglGetDisplay(EGL14.EglDefaultDisplay);
            EGL14.EglInitialize(display, new int[2], 0, new int[2], 1);

            int[] cfgAttribs = {
                EGL14.EglRenderableType, EGL14.EglOpenglEs2Bit,
                EGL14.EglSurfaceType,    EGL14.EglPbufferBit,
                EGL14.EglNone
            };
            var configs = new EGLConfig[1];
            EGL14.EglChooseConfig(display, cfgAttribs, 0, configs, 0, 1, new int[1], 0);

            int[] ctxAttribs = { EGL14.EglContextClientVersion, 2, EGL14.EglNone };
            var ctx = EGL14.EglCreateContext(display, configs[0], EGL14.EglNoContext, ctxAttribs, 0);

            int[] pbAttribs = { EGL14.EglWidth, 1, EGL14.EglHeight, 1, EGL14.EglNone };
            var surf = EGL14.EglCreatePbufferSurface(display, configs[0], pbAttribs, 0);

            EGL14.EglMakeCurrent(display, surf, surf, ctx);

            string ext = GLES20.GlGetString(GLES20.GlExtensions) ?? "";
            _gpuSupportOpenGL.s3tc = ext.Contains("GL_EXT_texture_compression_s3tc");
            _gpuSupportOpenGL.bc7 = ext.Contains("GL_EXT_texture_compression_bptc");
            _gpuSupportOpenGL.read = true;
            Log.Add(Log.LogSeverity.Information, "AndroidHelper.GpuSupportsBCnTexturesOpenGL()", $"S3TC Support: {_gpuSupportOpenGL.s3tc}");
            Log.Add(Log.LogSeverity.Information, "AndroidHelper.GpuSupportsBCnTexturesOpenGL()", $"BC7 Support: {_gpuSupportOpenGL.bc7}");

            // cleanup
            EGL14.EglMakeCurrent(display, EGL14.EglNoSurface, EGL14.EglNoSurface, EGL14.EglNoContext);
            EGL14.EglDestroySurface(display, surf);
            EGL14.EglDestroyContext(display, ctx);
            EGL14.EglTerminate(display);
        }
        catch(Exception ex)
        {
            Log.Add(Log.LogSeverity.Error, "AndroidHelper.GpuSupportsBCnTexturesOpenGL()", ex);
            _gpuSupportOpenGL = (false, false, true);
        }
        return _gpuSupportOpenGL;
    }
#else
    //Stubs
    public static string? GetExternalAppFilesDir() => "";
    public static string GetInternalAppFilesDir() => "";
    public static string[] GetAllExternalAppFilesDirs() => new string[] { };
    public static string GetDefaultLibraryDir() => "";
    public static string GetDefaultKnetDir() => "";
    public static string GetDefaultKnetDataDir() => "";
    public static string GetDefaultFSODataDir() => "";
    public static (bool s3tc, bool bc7, bool read) GpuSupportsBCnTexturesOpenGL() => (true, true, true);
    public static void LaunchFSO(string engineLibPath, string? workingFolder, string cmdline) {  }
#endif
}

