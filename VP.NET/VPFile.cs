using System.IO;

namespace VP.NET
{    
    public enum VPFileType
    {
        Directory,
        File,
        BackDir
    }

    public class VPFile
    {
        private VPContainer? vp;
        internal string? fullpath; //only used for adding new files
        internal bool delete = false;
        internal bool newFile = false;
        public VPFileType type; 
        public VPIndexEntry info;
        public CompressionInfo compressionInfo;
        public List<VPFile>? files;
        public VPFile? parent;

        /// <summary>
        /// Creates a empty VP file
        /// Pass the VPFile type, directory, file, backdir, this is used to generate the correct file info
        /// Pass the main vp object this file belong to as parameter, this is used to get the vp file path for file extraction or saving
        /// When creating a new folder manually, always remember to add a backdir to it
        /// fullpath is only used for adding a new file to the vp.
        /// </summary>
        /// <param name="type"></param>
        /// <param name="vp"></param>
        /// <param name="parent"></param>
        /// <param name="name"></param>
        /// <param name="offset"></param>
        /// <param name="size"></param>
        /// <param name="timestamp"></param>
        /// <param name="fullpath"></param>
        public VPFile(VPFileType type,VPContainer vp, VPFile? parent = null, string name = "", int offset = 0, int size = 0, int timestamp = 0, string? fullpath = null)
        {
            info.name = name;
            info.offset = offset;
            this.fullpath = fullpath; 
            this.type = type;
            this.vp = vp;
            this.parent = parent;
            switch (type)
            {
                case VPFileType.Directory:
                    info.size = 0;
                    info.timestamp = 0;
                    files = new List<VPFile>();
                    vp.numberFolders++;
                    break;
                case VPFileType.BackDir:
                    info.size = 0;
                    info.timestamp = 0;
                    info.name = "..";
                    break;
                case VPFileType.File:
                    info.size = size;
                    vp.numberFiles++;
                    info.timestamp = timestamp;
                    break;
            }
        }

        /// <summary>
        /// Creates a empty subfolder into this directory
        /// </summary>
        /// <param name="name"></param>
        public VPFile CreateEmptyDirectory(string name)
        {
            if (type != VPFileType.Directory)
                throw new Exception("This is not a VP directory!");
            var nf = new VPFile(VPFileType.Directory, vp!, this, name);
            nf.files!.Add(new VPFile(VPFileType.BackDir, vp!, nf));
            VPFile.InsertInOrder(files!, nf);
            return nf;
        }

        /// <summary>
        /// Search for a single file name, recursively.
        /// </summary>
        /// <param name="filename"></param>
        /// <returns>VPFile or null</returns>
        public VPFile? SearchForFileName(string filename)
        {
            if (type == VPFileType.File)
            {
                if (info.name.ToLower() == filename.ToLower())
                {
                    return this;
                }
            }
            else if (type == VPFileType.Directory && files != null)
            {
                foreach (var f in files)
                {
                    var found = f.SearchForFileName(filename);
                    if (found != null)
                        return found;
                }
            }

            return null;
        }

        /// <summary>
        /// Get all files in a VP that have a certain extension.
        /// </summary>
        /// <param name="ext"></param>
        /// <returns>List of VpFiles with that extension or empty list</returns>
        public List<VPFile> SearchForFileExtension(string ext)
        {
            var list = new List<VPFile>();
            if (type == VPFileType.File)
            {
                if (Path.GetExtension(info.name.ToLower()) == ext.ToLower())
                    list.Add(this);
            }
            else if (type == VPFileType.Directory && files != null)
            {
                foreach (var f in files)
                {
                    var found = f.SearchForFileExtension(ext);
                    if (found.Any())
                        list.AddRange(found);
                }
            }

            return list;
        }

        /// <summary>
        /// Adds a file into this directory
        /// </summary>
        /// <param name="file"></param>
        public VPFile? AddFile(FileInfo file)
        {
            if (type != VPFileType.Directory)
                throw new Exception("This is not a directory!");
            if(files == null)
                throw new Exception("This directory has a null file list, this is not valid.");

            var exists = files.FirstOrDefault(x => x.info!.name == file.Name && x.type == VPFileType.File);
            if (exists != null)
            {
                exists.Delete();
            }

            var nf = new VPFile(VPFileType.File, vp!, this, file.Name, 0, 0, VPTime.GetTimestampFromFile(file.FullName), file.FullName);
            VPFile.InsertInOrder(files, nf);
            return nf;
        }

        /// <summary>
        /// Adds a directory and all subdirs and files into this directory
        /// </summary>
        /// <param name="folderpath"></param>
        public void AddDirectoryRecursive(string folderpath)
        {
            if (files == null)
            {
                throw new Exception("This directory has a null file list, this is not valid.");
            }
            var di = new DirectoryInfo(folderpath);
            var folders = di.GetDirectories();
            var fls = di.GetFiles();
            if (folders != null)
            {
                foreach (DirectoryInfo dir in folders)
                {
                    var exists = files.FirstOrDefault(f => f.info!.name == dir.Name && f.type == VPFileType.Directory);
                    if (exists != null)
                    {
                        exists.AddDirectoryRecursive(dir.FullName);
                    }
                    else
                    {
                        var folder = new VPFile(VPFileType.Directory, vp!, this, dir.Name);
                        folder.files!.Add(new VPFile(VPFileType.BackDir, vp!, folder));
                        VPFile.InsertInOrder(files, folder);
                        folder.AddDirectoryRecursive(dir.FullName);
                    }
                }
            }
            if (fls != null)
            {
                foreach (var f in fls)
                {
                    var exists = files.FirstOrDefault(x => x.info!.name == f.Name  && x.type == VPFileType.File);
                    if (exists != null)
                    {
                        exists.Delete();
                    }
                    var nf = new VPFile(VPFileType.File, vp!, this, f.Name, 0, 0, VPTime.GetTimestampFromFile(f.FullName), f.FullName);
                    VPFile.InsertInOrder(files, nf);
                }
            }
        }

        /// <summary>
        /// Marks or unmarks this file or folder for deletion. Changes are only commited during the save process.
        /// If this is a folder everything inside is also deleted
        /// </summary>
        /// <param name="value"></param>
        public void Delete(bool value = true)
        {
            delete = value;
        }

        /// <summary>
        /// Retuns true if file or folder is marked for deletion, false if not.
        /// </summary>
        public bool DeleteStatus()
        {
            return delete;
        }

        /// <summary>
        /// Marks or unmarks this file or folder as a new file or folder that is not yet saved.
        /// This is only used for display purposes on the frontend, its not used internally
        /// </summary>
        /// <param name="value"></param>
        public void SetNewFile(bool value = true)
        {
            newFile = value;
        }

        /// <summary>
        /// Retuns true if file or folder is marked as a new file, false if not.
        /// </summary>
        public bool NewFileStatus()
        {
            return newFile;
        }

        /// <summary>
        /// Returns the number of files on this VPFile
        /// If this VPFile is a folder it returns the number of files on that folder and subfolders
        /// otherwise it returns 1
        /// </summary>
        /// <returns>int</returns>
        public int GetNumberOfFiles()
        {
            if(type == VPFileType.File)
            { 
                return 1; 
            }
            if(type == VPFileType.Directory && files != null && files.Count() > 0)
            {
                int count = 0;
                foreach (var f in files)
                    count += f.GetNumberOfFiles();
                return count;
            }
            return 0;
        }

        /// <summary>
        /// Extracts this file or folder to path
        /// </summary>
        /// <param name="path"></param>
        /// <returns></returns>
        public async Task ExtractRecursiveAsync(string path, Action<string, int, int>? progressCallback = null)
        {
            switch (type)
            {
                case VPFileType.Directory:
                    if (files != null)
                    {
                        Directory.CreateDirectory(path + Path.DirectorySeparatorChar + info.name);
                        foreach (var file in files)
                        {
                            if (progressCallback != null)
                                progressCallback(file.info.name, 1, vp!.numberFiles);
                            await file.ExtractRecursiveAsync(path + Path.DirectorySeparatorChar + info.name);
                        }
                    }
                    else
                    {
                        throw new Exception("This directory has a null file list, this is not valid.");
                    }
                    break;
                case VPFileType.File:
                    if (progressCallback != null)
                        progressCallback(info.name, 1, vp!.numberFiles);
                    await ExtractFile(path + Path.DirectorySeparatorChar + info.name);
                    break;
                case VPFileType.BackDir:
                    break;
            }
        }

        public async Task ReadToStream(Stream destination)
        {
            int bufferSize = 8192;
            if (info.size == 0)
            {
                throw new Exception("Files can not be of size 0, you are trying to extract a file that have not been saved to the vp yet?");
            }
            if (info.offset < 16)
            {
                throw new Exception("Invalid file offset.");
            }
            if (!File.Exists(vp?.vpFilePath))
            {
                throw new Exception("Unable to open vp file in path : " + vp?.vpFilePath);
            }

            using var source = new FileStream(vp.vpFilePath, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize);

            if (!source.CanRead)
            {
                throw new Exception("Unable to read vp file : " + vp.vpFilePath);
            }
            if (!destination.CanWrite)
            {
                throw new Exception("Unable to open file for writting : " + fullpath);
            }

            source.Seek(info.offset, SeekOrigin.Begin);

            if (compressionInfo.header.HasValue)
            {
                VPCompression.DecompressStream(source, destination, compressionInfo.header.Value, info.size);
            }
            else
            {
                int leftToCopy = (int)info.size;
                while (leftToCopy > 0)
                {
                    byte[] buffer = new byte[bufferSize];
                    int bytesToRead = leftToCopy > bufferSize ? bufferSize : leftToCopy;
                    var bytesRead = await source.ReadAsync(buffer, 0, bytesToRead);
                    if (bytesRead != bytesToRead)
                    {
                        throw new IOException("Unable to read from file");
                    }
                    if (!source.CanRead || !destination.CanWrite) throw new OperationCanceledException();
                    leftToCopy -= bytesRead;
                    await destination.WriteAsync(buffer, 0, bytesRead);
                }
            }

            destination.Position = 0;
        }

        private async Task ExtractFile(string fullpath)
        {
            int bufferSize = 8192;
            if (vp!.vpFilePath != null)
            {
                if (info.size == 0)
                {
                    throw new Exception("Files can not be of size 0, you are trying to extract a file that have not been saved to the vp yet?");
                }
                if (info.offset < 16)
                {
                    throw new Exception("Invalid file offset.");
                }
                if (!File.Exists(vp.vpFilePath))
                {
                    throw new Exception("Unable to open vp file in path : " + vp.vpFilePath);
                }

                var source = new FileStream(vp.vpFilePath, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize);
                var destination = new FileStream(fullpath, FileMode.Create, FileAccess.Write, FileShare.None, bufferSize);

                if (!source.CanRead)
                {
                    throw new Exception("Unable to read vp file : " + vp.vpFilePath);
                }
                if (!destination.CanWrite)
                {
                    throw new Exception("Unable to open file for writting : " + fullpath);
                }

                source.Seek(info.offset, SeekOrigin.Begin);

                if (compressionInfo.header.HasValue)
                {
                    VPCompression.DecompressStream(source, destination, compressionInfo.header.Value, info.size);
                }
                else
                {
                    int leftToCopy = (int)info.size;
                    while (leftToCopy > 0)
                    {
                        byte[] buffer = new byte[bufferSize];
                        int bytesToRead = leftToCopy > bufferSize ? bufferSize : leftToCopy;
                        var bytesRead = await source.ReadAsync(buffer, 0, bytesToRead);
                        if (bytesRead != bytesToRead)
                        {
                            throw new IOException("Unable to read from file");
                        }
                        leftToCopy -= bytesRead;
                        await destination.WriteAsync(buffer, 0, bytesRead);
                    }
                } 

                source.Close();
                await source.DisposeAsync();
                destination.Close();
                await destination.DisposeAsync();
            }
            else
            {
                throw new Exception("Invalid VP path : Null");
            }
        }

        internal static void InsertInOrder(List<VPFile> vpFileList, VPFile vpFile)
        {
            int i;
            for (i = 0; i < vpFileList.Count; i++)
            {
                if (vpFileList[i].type == VPFileType.BackDir || vpFileList[i].type == vpFile.type && String.Compare(vpFileList[i].info.name, vpFile.info.name) > 0)
                {
                    break;
                }
            }
            vpFileList.Insert(i, vpFile);
        }

        //Determine if this file has to be compressed
        internal bool CheckHaveToCompress()
        {
            if (type != VPFileType.File)
                return false;

            if (info.size < VPCompression.MinimumSize)
                return false;

            int periodPos = info.name.LastIndexOf('.');
            if (periodPos > 0)
            {
                string ext = info.name.ToLower().Substring(periodPos, info.name.Length - periodPos);
                if (vp!.compression && !compressionInfo.header.HasValue && VPCompression.ExtensionIgnoreList.IndexOf(ext) == -1)
                {
                    return true;
                }
            }
            else
            {
                if (vp!.compression && !compressionInfo.header.HasValue)
                {
                    return true;
                }
            }
            return false;
        }

        //Determine if this file has to be decompressed
        internal bool CheckHaveToDecompress()
        {
            if (type != VPFileType.File)
                return false;

            if (compressionInfo.header.HasValue && !vp!.compression)
            {
                return true;
            }

            return false;
        }
    }
}
