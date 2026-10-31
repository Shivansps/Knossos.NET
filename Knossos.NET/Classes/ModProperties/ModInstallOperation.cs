using Avalonia.Threading;
using Knossos.NET.ViewModels;
using System;
using System.Collections.Generic;
using System.Linq;
using Knossos.NET.Models;
using System.Threading;

namespace Knossos.NET.Classes
{
    /// <summary>Owns progress and cancellation for one installation or verification request.</summary>
    public sealed class ModInstallOperation : IDisposable
    {
        private readonly CancellationTokenSource cancellation = new();
        private Action<TaskItemViewModel>? taskChanged;
        private static readonly Dictionary<string, int> pendingVersions = new();
        private readonly string[] reservedVersions;
        private bool disposed;
        public CancellationToken Token => cancellation.Token;
        public ModInstallOperation(Action<TaskItemViewModel> taskChanged, IEnumerable<Mod>? versions = null)
        {
            this.taskChanged = taskChanged;
            reservedVersions = versions?.Select(ModPackageCatalog.Key).Distinct().ToArray() ?? Array.Empty<string>();
            lock (pendingVersions)
                foreach (var key in reservedVersions) pendingVersions[key] = pendingVersions.GetValueOrDefault(key) + 1;
        }
        internal static bool IsVersionPending(string id, string version)
        {
            lock (pendingVersions) return pendingVersions.ContainsKey(id + "@" + version);
        }
        internal void Track(TaskItemViewModel task) => Dispatcher.UIThread.Invoke(() => taskChanged?.Invoke(task));
        public void Cancel() => cancellation.Cancel();
        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            lock (pendingVersions)
                foreach (var key in reservedVersions)
                {
                    if (pendingVersions[key] == 1) pendingVersions.Remove(key);
                    else pendingVersions[key]--;
                }
            taskChanged = null;
            cancellation.Dispose();
        }
    }
}
