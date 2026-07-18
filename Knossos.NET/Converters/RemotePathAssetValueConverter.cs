using Avalonia.Data.Converters;
using System;
using System.Globalization;
using Avalonia.Platform;
using Avalonia.Media.Imaging;
using System.IO;
using System.Threading.Tasks;

namespace Knossos.NET.Converters
{
    public class RemotePathAssetValueConverter : IValueConverter
    {
        public static RemotePathAssetValueConverter Instance { get; } = new();

        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo? culture)
        {
            try
            {
                if (value == null) return null;

                if(!targetType.IsAssignableFrom(typeof(string)))
                    throw new NotSupportedException();

                if (value is not string rawUri)
                {
                    if(parameter is string)
                    {
                        rawUri = (string)parameter;
                    }
                    else
                    {
                        throw new NotSupportedException();
                    }
                }

                if (rawUri.StartsWith("avares://"))
                {
                    return rawUri;
                }
                else if (rawUri.ToLower().StartsWith("http"))
                {
                    var localPath = Task.Run(() => KnUtils.GetRemoteResource(rawUri)).Result;
                    if (localPath != null)
                    {
                        return localPath;
                    }
                }
                else if (File.Exists(Path.Combine(KnUtils.GetKnossosDataFolderPath(), rawUri)))
                {
                    return Path.Combine(KnUtils.GetKnossosDataFolderPath(), rawUri);
                }
                else if (File.Exists(rawUri))
                {
                    return rawUri;
                }
            }
            catch (Exception ex)
            {
                Log.Add(Log.LogSeverity.Error, "RemotePathAssetValueConverter.Convert()", ex);
            }
            return null;
        }

        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
