using System.Runtime.InteropServices;

namespace Knossos.NET.Classes
{
    public static class KSapi
    {
        private const string Dll = "KSapi";
        private static bool inited;
        private static readonly object gate = new();

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        private static extern int KSapi_Init();
        [DllImport(Dll, CharSet = CharSet.Unicode, CallingConvention = CallingConvention.Cdecl)]
        private static extern int KSapi_Speak(string text, int voiceIndex, int volume);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        private static extern void KSapi_Stop();
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        private static extern void KSapi_Shutdown();

        public static void Stop()
        {
            try
            {
                if (inited) KSapi_Stop();
            }
            catch { }
        }

        public static void Speak(string text, int voice, int vol)
        {
            try
            {
                lock (gate)
                {
                    if (!inited) { KSapi_Init(); inited = true; }
                }
                KSapi_Speak(text, voice, vol);
            }catch { }
        }
    }
}
