using Avalonia.Threading;
using Knossos.NET.Classes;
using Knossos.NET.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using VP.NET;

namespace Knossos.NET.ViewModels
{
    public partial class TaskItemViewModel : ViewModelBase
    {
        /// <summary>
        /// Third transcode stage. Runs AFTER the loose + vp .dds transcoding.
        ///
        /// Walks every .eff in the mod (loose files AND inside .vp/.vpc archives). For each $Type: dds eff it
        /// builds the theoretical frame list and checks how many frames are now available as .ktx (either just
        /// transcoded this run, present in <paramref name="transcodedNames"/>, or already .ktx in the layout):
        ///   - 0 frames converted  -> leave the eff alone (all-uncompressed UI animation, or nothing present).
        ///   - >=1 frame converted -> the eff is going ktx: patch its $Type to ktx, and add every frame that is
        ///                            still a .dds (no .ktx yet) to the returned "force" list.
        /// Loose effs are patched in place; effs inside an archive are patched and the archive is saved back to
        /// the same path. The returned set is the .dds frame names that a 2nd, forced pass must still convert.
        /// </summary>
        private async Task<HashSet<string>> PatchEffFiles(Mod mod, HashSet<string> transcodedNames, CancellationTokenSource? cancelSource = null)
        {
            var workFolder = "";
            var openArchives = new List<(string path, VPContainer vp)>();
            var forceList = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                if (TaskIsSet) throw new Exception("The task is already set, it cant be changed or re-assigned.");
                TaskIsSet = true;
                ProgressBarMax = 1;
                ProgressCurrent = 0;
                ShowProgressText = false;
                cancellationTokenSource = cancelSource ?? new CancellationTokenSource();
                CancelButtonVisible = false;
                Name = "Patching eff files";
                var token = cancellationTokenSource.Token;
                if (token.IsCancellationRequested) throw new TaskCanceledException();

                workFolder = Path.Combine(KnUtils.GetFSODataFolderPath(), "effPatchTemp");
                Directory.CreateDirectory(workFolder);

                var dataDir = mod.devMode ? mod.fullPath : Path.Combine(mod.fullPath, "data");

                // 1) Gather every archive in the mod (.vp AND .vpc; they are interchangeable - the library is
                //    transparent to compression). Look both at the mod root and inside the data folder.
                var archivePaths = new List<string>();
                foreach (var dir in new[] { mod.fullPath, dataDir }.Distinct())
                {
                    if (!Directory.Exists(dir)) continue;
                    archivePaths.AddRange(Directory.GetFiles(dir, "*.vp", SearchOption.AllDirectories));
                    archivePaths.AddRange(Directory.GetFiles(dir, "*.vpc", SearchOption.AllDirectories));
                }
                archivePaths = archivePaths.Distinct(StringComparer.OrdinalIgnoreCase).ToList();

                // 2) Build a layout-wide set of existing file names (loose + inside archives), plus the list of
                //    effs to consider. Only names are needed (no decompression of frame pixels).
                var existing = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var looseEffPaths = new List<string>();

                if (Directory.Exists(dataDir))
                {
                    foreach (var f in Directory.GetFiles(dataDir, "*.*", SearchOption.AllDirectories))
                    {
                        existing.Add(Path.GetFileName(f).ToLowerInvariant());
                        if (Path.GetExtension(f).Equals(".eff", StringComparison.OrdinalIgnoreCase))
                            looseEffPaths.Add(f);
                    }
                }

                var archiveEffNodes = new List<(string archivePath, VPContainer vp, VPFile node)>();
                foreach (var apath in archivePaths)
                {
                    token.ThrowIfCancellationRequested();
                    VPContainer vp;
                    try { vp = new VPContainer(); await vp.LoadVP(apath); }
                    catch (Exception ex)
                    {
                        Log.Add(Log.LogSeverity.Warning, "TaskItemViewModel.PatchEffFiles()", $"Could not open '{Path.GetFileName(apath)}': {ex.Message}");
                        continue;
                    }
                    openArchives.Add((apath, vp));
                    if (vp.vpFiles == null) continue;
                    foreach (var root in vp.vpFiles)
                    {
                        foreach (var n in root.SearchForFileExtension(".dds")) existing.Add(n.info.name.ToLowerInvariant());
                        foreach (var n in root.SearchForFileExtension(".ktx")) existing.Add(n.info.name.ToLowerInvariant());
                        foreach (var n in root.SearchForFileExtension(".eff")) archiveEffNodes.Add((apath, vp, n));
                    }
                }

                bool FrameKtxExists(string baseLower, int i) => existing.Contains($"{baseLower}_{i:D4}.ktx");
                bool FrameDdsExists(string baseLower, int i) => existing.Contains($"{baseLower}_{i:D4}.dds");
                bool FrameTranscodedThisRun(string baseLower, int i) => transcodedNames.Contains($"{baseLower}_{i:D4}.dds");

                // Decide a single eff. patchAction performs the actual $Type rewrite and returns true if it changed.
                async Task ConsiderEffAsync(EffHelper eff, Func<Task<bool>> patchAction, string displayName)
                {
                    if (!eff.IsDds) return; // ktx already done; png/tga/pcx/jpg/ani never apply

                    var baseLower = eff.BaseName.ToLowerInvariant();
                    int converted = 0;
                    var missing = new List<string>();
                    for (int i = 0; i < eff.FrameCount; i++)
                    {
                        if (FrameKtxExists(baseLower, i) || FrameTranscodedThisRun(baseLower, i)) converted++;
                        else if (FrameDdsExists(baseLower, i)) missing.Add($"{baseLower}_{i:D4}.dds");
                        // neither ktx nor dds present -> frame genuinely absent (eff was already incomplete); ignored
                    }

                    if (converted == 0)
                    {
                        // No frame became ktx -> this is an all-uncompressed UI animation (or has no usable frames).
                        // Leave it exactly as-is.
                        return;
                    }

                    var changed = await patchAction().ConfigureAwait(false);
                    if (changed)
                        Log.Add(Log.LogSeverity.Information, "TaskItemViewModel.PatchEffFiles()", $"Patched eff '{displayName}' -> ktx");
                    foreach (var m in missing) forceList.Add(m);
                }

                // 3a) Loose effs -> patch in place.
                foreach (var effPath in looseEffPaths)
                {
                    token.ThrowIfCancellationRequested();
                    string text;
                    EffHelper eff;
                    try { text = await File.ReadAllTextAsync(effPath, token).ConfigureAwait(false); eff = EffHelper.Parse(text, Path.GetFileName(effPath)); }
                    catch (Exception ex) { Log.Add(Log.LogSeverity.Warning, "TaskItemViewModel.PatchEffFiles()", $"Skipping unreadable eff '{Path.GetFileName(effPath)}': {ex.Message}"); continue; }

                    await ConsiderEffAsync(eff, () => EffHelper.PatchLooseAsync(effPath, "ktx", token), Path.GetFileName(effPath));
                }

                // 3b) Archive effs -> patch the node and mark the archive dirty.
                var dirtyArchives = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var (apath, vp, node) in archiveEffNodes)
                {
                    token.ThrowIfCancellationRequested();
                    EffHelper eff;
                    try
                    {
                        using var ms = new MemoryStream();
                        await node.ReadToStream(ms).ConfigureAwait(false);
                        eff = await EffHelper.ParseAsync(ms, node.info.name).ConfigureAwait(false);
                    }
                    catch (Exception ex) { Log.Add(Log.LogSeverity.Warning, "TaskItemViewModel.PatchEffFiles()", $"Skipping unreadable eff '{node.info.name}': {ex.Message}"); continue; }

                    await ConsiderEffAsync(eff, async () =>
                    {
                        var changed = await EffHelper.PatchInVpAsync(node, "ktx", workFolder, token).ConfigureAwait(false);
                        if (changed) dirtyArchives.Add(apath);
                        return changed;
                    }, node.info.name);
                }

                // 4) Save the archives whose eff we patched, back to the same path (temp + replace).
                foreach (var (apath, vp) in openArchives)
                {
                    token.ThrowIfCancellationRequested();
                    if (!dirtyArchives.Contains(apath)) continue;
                    await SaveArchiveInPlace(vp, apath, cancellationTokenSource).ConfigureAwait(false);
                    Log.Add(Log.LogSeverity.Information, "TaskItemViewModel.PatchEffFiles()", $"Saved patched archive '{Path.GetFileName(apath)}'");
                }

                IsCompleted = true;
                ProgressCurrent = ProgressBarMax;
                Info = "Eff patched. Frames to force: " + forceList.Count;
                Log.Add(Log.LogSeverity.Information, "TaskItemViewModel.PatchEffFiles()", "Eff patching finished. " + Info);
                if (workFolder != "" && Directory.Exists(workFolder)) Directory.Delete(workFolder, true);
                return forceList;
            }
            catch (TaskCanceledException)
            {
                IsCompleted = false; IsCancelled = true; CancelButtonVisible = false; Info = "Task Cancelled";
                if (cancelSource == null) cancellationTokenSource?.Dispose();
                if (workFolder != "" && Directory.Exists(workFolder)) Directory.Delete(workFolder, true);
                return forceList;
            }
            catch (Exception ex)
            {
                IsCompleted = false; IsCancelled = true; CancelButtonVisible = false; Info = "Task Failed";
                if (cancelSource == null) cancellationTokenSource?.Dispose();
                if (workFolder != "" && Directory.Exists(workFolder)) Directory.Delete(workFolder, true);
                Log.Add(Log.LogSeverity.Warning, "TaskItemViewModel.PatchEffFiles()", ex);
                return forceList;
            }
        }

        /// <summary>
        /// Save a vp/vpc back to its own path. We cannot write to the file we are reading, so we save to a temp
        /// sibling and then atomically replace the original (keeping its extension, .vp or .vpc, untouched - the
        /// library compresses or not based on the data already in the container).
        /// </summary>
        private async Task SaveArchiveInPlace(VPContainer vp, string archivePath, CancellationTokenSource cts)
        {
            var tmp = archivePath + ".tmp";
            if (File.Exists(tmp)) File.Delete(tmp);
            await vp.SaveAsAsync(tmp, compressionCallback, cts);
            File.Delete(archivePath);
            File.Move(tmp, archivePath);
        }
    }
}
