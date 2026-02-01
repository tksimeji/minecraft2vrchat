using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace M2V.Editor.Minecraft
{
    public abstract class AssetReader
    {
        internal abstract bool TryReadText(string path, out string text);
        internal abstract bool TryReadBytes(string path, out byte[] bytes);
        internal abstract IEnumerable<string> EnumeratePaths(string root, string extension);
    }

    public sealed class DirectoryAssetReader : AssetReader
    {
        private readonly DirectoryInfo _root;

        public DirectoryAssetReader(DirectoryInfo root)
        {
            _root = root;
        }

        internal override bool TryReadText(string path, out string text)
        {
            text = null;
            if (!TryReadBytes(path, out var bytes))
            {
                return false;
            }

            text = Encoding.UTF8.GetString(bytes);
            return true;
        }

        internal override bool TryReadBytes(string path, out byte[] bytes)
        {
            bytes = null;
            if (_root == null || string.IsNullOrEmpty(path))
            {
                return false;
            }

            var relative = NormalizeRelativePath(path);
            var fullPath = Path.Combine(_root.FullName, relative);
            if (!File.Exists(fullPath))
            {
                return false;
            }

            bytes = File.ReadAllBytes(fullPath);
            return true;
        }

        internal override IEnumerable<string> EnumeratePaths(string root, string extension)
        {
            if (_root == null || string.IsNullOrEmpty(root))
            {
                yield break;
            }

            var relativeRoot = NormalizeRelativePath(root);
            var dirPath = Path.Combine(_root.FullName, relativeRoot);
            if (!Directory.Exists(dirPath))
            {
                yield break;
            }

            var ext = NormalizeExtension(extension);
            foreach (var file in Directory.EnumerateFiles(dirPath, "*" + ext, SearchOption.AllDirectories))
            {
                var rel = Path.GetRelativePath(_root.FullName, file);
                yield return rel.Replace(Path.DirectorySeparatorChar, '/');
            }
        }

        private static string NormalizeRelativePath(string path)
        {
            var normalized = path.Replace('\\', '/').TrimStart('/');
            return normalized.Replace('/', Path.DirectorySeparatorChar);
        }

        private static string NormalizeExtension(string extension)
        {
            if (string.IsNullOrEmpty(extension))
            {
                return string.Empty;
            }

            return extension.StartsWith(".") ? extension : "." + extension;
        }
    }

    public sealed class ZipAssetReader : AssetReader, IDisposable
    {
        private readonly ZipArchive _archive;

        internal ZipAssetReader(FileInfo zipFile)
        {
            _archive = zipFile != null && zipFile.Exists
                ? ZipFile.OpenRead(zipFile.FullName)
                : null;
        }

        internal override bool TryReadText(string path, out string text)
        {
            text = null;
            if (!TryReadBytes(path, out var bytes))
            {
                return false;
            }

            text = Encoding.UTF8.GetString(bytes);
            return true;
        }

        internal override bool TryReadBytes(string path, out byte[] bytes)
        {
            bytes = null;
            if (_archive == null || string.IsNullOrEmpty(path))
            {
                return false;
            }

            var normalized = NormalizeZipPath(path);
            var entry = _archive.GetEntry(normalized);
            if (entry == null)
            {
                return false;
            }

            using (var stream = entry.Open())
            using (var ms = new MemoryStream())
            {
                stream.CopyTo(ms);
                bytes = ms.ToArray();
                return true;
            }
        }

        internal override IEnumerable<string> EnumeratePaths(string root, string extension)
        {
            if (_archive == null || string.IsNullOrEmpty(root))
            {
                yield break;
            }

            var prefix = NormalizeZipPath(root);
            if (!prefix.EndsWith("/", StringComparison.Ordinal))
            {
                prefix += "/";
            }

            var ext = NormalizeExtension(extension);
            foreach (var entry in _archive.Entries)
            {
                var name = entry.FullName;
                if (name.EndsWith("/", StringComparison.Ordinal))
                {
                    continue;
                }

                if (!name.StartsWith(prefix, StringComparison.Ordinal))
                {
                    continue;
                }

                if (!string.IsNullOrEmpty(ext) && !name.EndsWith(ext, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                yield return name;
            }
        }

        public void Dispose()
        {
            _archive?.Dispose();
        }

        private static string NormalizeZipPath(string path)
        {
            return path.Replace('\\', '/').TrimStart('/');
        }

        private static string NormalizeExtension(string extension)
        {
            if (string.IsNullOrEmpty(extension))
            {
                return string.Empty;
            }

            return extension.StartsWith(".") ? extension : "." + extension;
        }
    }

    public sealed class CompositeAssetReader : AssetReader, IDisposable
    {
        private readonly List<AssetReader> _readers;

        internal CompositeAssetReader(IEnumerable<AssetReader> readers)
        {
            _readers = readers == null ? new List<AssetReader>() : new List<AssetReader>(readers);
        }

        internal override bool TryReadText(string path, out string text)
        {
            text = null;
            foreach (var reader in _readers)
            {
                if (reader != null && reader.TryReadText(path, out text))
                {
                    return true;
                }
            }

            return false;
        }

        internal override bool TryReadBytes(string path, out byte[] bytes)
        {
            bytes = null;
            foreach (var reader in _readers)
            {
                if (reader != null && reader.TryReadBytes(path, out bytes))
                {
                    return true;
                }
            }

            return false;
        }

        internal override IEnumerable<string> EnumeratePaths(string root, string extension)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var reader in _readers)
            {
                if (reader == null)
                {
                    continue;
                }

                foreach (var path in reader.EnumeratePaths(root, extension))
                {
                    if (string.IsNullOrEmpty(path) || !seen.Add(path))
                    {
                        continue;
                    }

                    yield return path;
                }
            }
        }

        public void Dispose()
        {
            foreach (var reader in _readers)
            {
                if (reader is IDisposable disposable)
                {
                    disposable.Dispose();
                }
            }
        }
    }
}