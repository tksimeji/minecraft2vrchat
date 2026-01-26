using System;
using System.Collections.Generic;
using System.IO;
using M2V.Editor.Model;

namespace M2V.Editor.Meshing
{
    internal sealed class AssetPackResolver : IDisposable
    {
        private readonly CompositeAssetReader _modelReader;
        private readonly CompositeAssetReader _biomeReader;

        internal IAssetReader ModelReader => _modelReader;
        internal IAssetReader BiomeReader => _biomeReader;

        private AssetPackResolver(CompositeAssetReader modelReader, CompositeAssetReader biomeReader)
        {
            _modelReader = modelReader;
            _biomeReader = biomeReader;
        }

        internal static AssetPackResolver TryCreate(string minecraftJarPath, FileSystemInfo resourcePack, FileSystemInfo dataPack, out string error)
        {
            error = string.Empty;
            if (string.IsNullOrEmpty(minecraftJarPath))
            {
                error = "[Minecraft2VRChat] Minecraft jar path is missing.";
                return null;
            }

            var jarFile = new FileInfo(minecraftJarPath);
            if (!jarFile.Exists)
            {
                error = $"[Minecraft2VRChat] Minecraft jar not found: {minecraftJarPath}";
                return null;
            }

            var disposables = new List<IDisposable>();

            if (!TryCreateReader(jarFile, out var jarReader, out var jarError))
            {
                error = jarError;
                return null;
            }

            if (jarReader is IDisposable jarDisposable)
            {
                disposables.Add(jarDisposable);
            }

            var modelReaders = new List<IAssetReader>();
            var biomeReaders = new List<IAssetReader>();

            if (TryCreateReader(resourcePack, out var resourceReader, out _))
            {
                modelReaders.Add(resourceReader);
                if (resourceReader is IDisposable resourceDisposable)
                {
                    disposables.Add(resourceDisposable);
                }
            }

            foreach (var packReader in EnumerateDataPackReaders(dataPack, disposables))
            {
                biomeReaders.Add(packReader);
            }

            modelReaders.Add(jarReader);
            biomeReaders.Add(jarReader);

            var modelComposite = new CompositeAssetReader(modelReaders);
            var biomeComposite = new CompositeAssetReader(biomeReaders);
            return new AssetPackResolver(modelComposite, biomeComposite)
            {
                _disposables = disposables
            };
        }

        private List<IDisposable> _disposables;

        public void Dispose()
        {
            if (_disposables == null)
            {
                return;
            }

            foreach (var disposable in _disposables)
            {
                disposable.Dispose();
            }

            _disposables.Clear();
        }

        private static bool TryCreateReader(FileSystemInfo info, out IAssetReader reader, out string error)
        {
            reader = null;
            error = string.Empty;
            if (info == null)
            {
                return false;
            }

            if (info is DirectoryInfo dir && dir.Exists)
            {
                reader = new DirectoryAssetReader(dir);
                return true;
            }

            if (info is FileInfo file && file.Exists)
            {
                reader = new ZipAssetReader(file);
                return true;
            }

            if (info is FileInfo missingFile)
            {
                error = $"[Minecraft2VRChat] Pack file not found: {missingFile.FullName}";
            }
            else if (info is DirectoryInfo missingDir)
            {
                error = $"[Minecraft2VRChat] Pack folder not found: {missingDir.FullName}";
            }

            return false;
        }

        private static IEnumerable<IAssetReader> EnumerateDataPackReaders(FileSystemInfo dataPack, List<IDisposable> disposables)
        {
            if (dataPack == null)
            {
                yield break;
            }

            if (dataPack is FileInfo file)
            {
                if (TryCreateReader(file, out var reader, out _))
                {
                    if (reader is IDisposable disposable)
                    {
                        disposables.Add(disposable);
                    }

                    yield return reader;
                }

                yield break;
            }

            if (!(dataPack is DirectoryInfo root) || !root.Exists)
            {
                yield break;
            }

            var packInfos = new List<FileSystemInfo>();
            foreach (var dir in root.EnumerateDirectories())
            {
                packInfos.Add(dir);
            }

            foreach (var zip in root.EnumerateFiles("*.zip", SearchOption.TopDirectoryOnly))
            {
                packInfos.Add(zip);
            }

            packInfos.Sort((a, b) => string.CompareOrdinal(a.Name, b.Name));
            packInfos.Reverse();

            foreach (var info in packInfos)
            {
                if (!TryCreateReader(info, out var reader, out _))
                {
                    continue;
                }

                if (reader is IDisposable disposable)
                {
                    disposables.Add(disposable);
                }

                yield return reader;
            }
        }
    }
}
