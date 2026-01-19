using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using fNbt;
using UnityEngine;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace M2V.Editor
{
    public static class M2VMeshGenerator
    {
        public struct Options
        {
            public bool UseGreedy;
            public bool ApplyCoordinateTransform;
            public bool LogSliceStats;
            public bool LogPaletteBounds;
            public bool UseTextureAtlas;
        }

        private enum TextureAlphaMode
        {
            Opaque,
            Cutout,
            Translucent
        }

        public static Mesh GenerateMesh(string worldFolder, string dimensionId, Vector3Int min, Vector3Int max, string minecraftJarPath, Options options, ref bool logChunkOnce, out string message, out Texture2D atlasTexture)
        {
            message = string.Empty;
            atlasTexture = null;
            var sizeX = max.x - min.x + 1;
            var sizeY = max.y - min.y + 1;
            var sizeZ = max.z - min.z + 1;
            if (sizeX <= 0 || sizeY <= 0 || sizeZ <= 0)
            {
                message = "[Minecraft2VRChat] Invalid range: size <= 0.";
                return null;
            }

            var volume = (long)sizeX * sizeY * sizeZ;
            if (volume > 2_000_000)
            {
                message = $"[Minecraft2VRChat] Range too large for meshing ({volume} blocks). Reduce the range.";
                return null;
            }

            if (string.IsNullOrEmpty(minecraftJarPath) || !File.Exists(minecraftJarPath))
            {
                message = "[Minecraft2VRChat] Minecraft version jar not found for model lookup.";
                return null;
            }

            var blockStates = new List<BlockStateDefinition> { BlockStateDefinition.Empty };
            var blocks = new int[volume];
            if (!FillBlockStateIds(worldFolder, dimensionId, min, max, blocks, sizeX, sizeY, sizeZ, blockStates, ref logChunkOnce, options.LogPaletteBounds))
            {
                message = "[Minecraft2VRChat] Failed to read blocks for meshing (region folder missing or read failure).";
                return null;
            }

            if (options.LogSliceStats)
            {
                LogSolidSliceStats(blocks, sizeX, sizeY, sizeZ, min);
            }

            using var zip = ZipFile.OpenRead(minecraftJarPath);
            var resolver = new ModelResolver(zip);
            var modelCache = resolver.BuildBlockModels(blockStates);
            var fullCubeById = resolver.BuildFullCubeFlags(modelCache);
            var texturePaths = resolver.CollectTexturePaths(modelCache);
            atlasTexture = BuildTextureAtlasFromTextures(zip, texturePaths, out var uvByTexture, out var alphaByTexture);
            if (atlasTexture == null)
            {
                message = "[Minecraft2VRChat] Failed to build texture atlas from models.";
                return null;
            }

            var mesh = BuildModelMesh(blocks, sizeX, sizeY, sizeZ, min, modelCache, fullCubeById, uvByTexture, alphaByTexture, options.ApplyCoordinateTransform);

            if (mesh == null)
            {
                message = "[Minecraft2VRChat] Mesh generation produced 0 faces (all air or no data).";
            }

            return mesh;
        }

        public static long CountBlocksInRange(string worldFolder, string dimensionId, Vector3Int min, Vector3Int max, ref bool logChunkOnce)
        {
            var dimensionFolder = GetDimensionFolder(worldFolder, dimensionId);
            if (string.IsNullOrEmpty(dimensionFolder))
            {
                return 0;
            }

            var regionFolder = Path.Combine(dimensionFolder, "region");
            if (!Directory.Exists(regionFolder))
            {
                return 0;
            }

            var chunkMinX = FloorDiv(min.x, 16);
            var chunkMaxX = FloorDiv(max.x, 16);
            var chunkMinZ = FloorDiv(min.z, 16);
            var chunkMaxZ = FloorDiv(max.z, 16);

            long count = 0;
            for (var cx = chunkMinX; cx <= chunkMaxX; cx++)
            {
                for (var cz = chunkMinZ; cz <= chunkMaxZ; cz++)
                {
                    count += CountBlocksInChunk(regionFolder, cx, cz, min, max, ref logChunkOnce);
                }
            }

            return count;
        }

        private static HashSet<string> CollectBlockNames(string worldFolder, string dimensionId, Vector3Int min, Vector3Int max, ref bool logChunkOnce)
        {
            var result = new HashSet<string>(StringComparer.Ordinal);
            var dimensionFolder = GetDimensionFolder(worldFolder, dimensionId);
            if (string.IsNullOrEmpty(dimensionFolder))
            {
                return result;
            }

            var regionFolder = Path.Combine(dimensionFolder, "region");
            if (!Directory.Exists(regionFolder))
            {
                return result;
            }

            var chunkMinX = FloorDiv(min.x, 16);
            var chunkMaxX = FloorDiv(max.x, 16);
            var chunkMinZ = FloorDiv(min.z, 16);
            var chunkMaxZ = FloorDiv(max.z, 16);

            for (var cx = chunkMinX; cx <= chunkMaxX; cx++)
            {
                for (var cz = chunkMinZ; cz <= chunkMaxZ; cz++)
                {
                    CollectBlockNamesInChunk(regionFolder, cx, cz, min, max, result, ref logChunkOnce);
                }
            }

            return result;
        }

        private static void CollectBlockNamesInChunk(string regionFolder, int chunkX, int chunkZ, Vector3Int min, Vector3Int max, HashSet<string> result, ref bool logChunkOnce)
        {
            var chunk = LoadChunk(regionFolder, chunkX, chunkZ);
            if (chunk == null)
            {
                return;
            }

            if (logChunkOnce)
            {
                logChunkOnce = false;
                Debug.Log($"[Minecraft2VRChat] Chunk NBT (sample):\n{chunk}");
            }

            var baseTag = chunk;
            if (chunk["Level"] is NbtCompound levelCompound)
            {
                baseTag = levelCompound;
            }

            var sections = baseTag["Sections"] as NbtList ?? baseTag["sections"] as NbtList;
            if (sections == null)
            {
                return;
            }

            var chunkMinX = chunkX * 16;
            var chunkMinZ = chunkZ * 16;

            foreach (var sectionTag in sections)
            {
                if (sectionTag is not NbtCompound section)
                {
                    continue;
                }

                if (!TryGetSectionY(section, out var sectionY))
                {
                    continue;
                }

                var sectionMinY = sectionY * 16;
                var sectionMaxY = sectionMinY + 15;
                if (max.y < sectionMinY || min.y > sectionMaxY)
                {
                    continue;
                }

                if (!TryGetSectionBlockData(section, out var palette, out var blockStates))
                {
                    continue;
                }

                BuildPaletteCaches(palette, out var paletteNames, out var isAir);
                var bits = GetBitsForSection(palette.Count, blockStates);

                if (bits == 0)
                {
                    if (!isAir[0])
                    {
                        result.Add(paletteNames[0]);
                    }

                    continue;
                }

                if (blockStates == null || blockStates.Length == 0)
                {
                    continue;
                }

                var xMin = Mathf.Max(min.x, chunkMinX);
                var xMax = Mathf.Min(max.x, chunkMinX + 15);
                var zMin = Mathf.Max(min.z, chunkMinZ);
                var zMax = Mathf.Min(max.z, chunkMinZ + 15);
                var yMin = Mathf.Max(min.y, sectionMinY);
                var yMax = Mathf.Min(max.y, sectionMaxY);

                for (var y = yMin; y <= yMax; y++)
                {
                    var localY = y - sectionMinY;
                    for (var z = zMin; z <= zMax; z++)
                    {
                        var localZ = z - chunkMinZ;
                        for (var x = xMin; x <= xMax; x++)
                        {
                            var localX = x - chunkMinX;
                            var index = (localY << 8) | (localZ << 4) | localX;
                            var paletteIndex = GetBlockStateIndex(blockStates, index, bits);
                            if (paletteIndex >= 0 && paletteIndex < paletteNames.Length && !isAir[paletteIndex])
                            {
                                result.Add(paletteNames[paletteIndex]);
                            }
                        }
                    }
                }
            }
        }

        private static Texture2D BuildTextureAtlas(string jarPath, HashSet<string> blockNames, out Dictionary<string, Rect> uvByName)
        {
            uvByName = new Dictionary<string, Rect>(StringComparer.Ordinal);
            if (!File.Exists(jarPath))
            {
                return null;
            }

            var names = new List<string>(blockNames);
            names.Sort(StringComparer.Ordinal);

            var textures = new List<Texture2D>(names.Count);
            var tileSize = 16;

            using var zip = ZipFile.OpenRead(jarPath);
            foreach (var fullName in names)
            {
                var tex = LoadBlockTexture(zip, fullName);
                if (tex == null)
                {
                    tex = LoadBlockTexture(zip, "minecraft:dirt");
                }

                if (tex == null)
                {
                    tex = new Texture2D(16, 16, TextureFormat.RGBA32, false);
                    tex.SetPixels(BuildFallbackPixels(16, 16));
                    tex.Apply();
                }

                tileSize = Mathf.Max(tileSize, tex.width);
                textures.Add(tex);
            }

            var count = textures.Count;
            if (count == 0)
            {
                return null;
            }

            var columns = Mathf.CeilToInt(Mathf.Sqrt(count));
            var rows = Mathf.CeilToInt((float)count / columns);
            var atlasSize = Mathf.NextPowerOfTwo(Mathf.Max(1, columns * tileSize));
            atlasSize = Mathf.Max(atlasSize, Mathf.NextPowerOfTwo(rows * tileSize));

            var atlas = new Texture2D(atlasSize, atlasSize, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Repeat
            };
            atlas.SetPixels(BuildFallbackPixels(atlasSize, atlasSize));

            for (var i = 0; i < textures.Count; i++)
            {
                var col = i % columns;
                var row = i / columns;
                var x = col * tileSize;
                var y = row * tileSize;
                CopyTextureToAtlas(textures[i], atlas, x, y, tileSize);

                var rect = new Rect(
                    (float)x / atlasSize,
                    (float)y / atlasSize,
                    (float)tileSize / atlasSize,
                    (float)tileSize / atlasSize);

                uvByName[names[i]] = rect;
            }

            atlas.Apply();
            return atlas;
        }

        private static Texture2D BuildTextureAtlasFromTextures(ZipArchive zip, HashSet<string> texturePaths, out Dictionary<string, Rect> uvByTexture, out Dictionary<string, TextureAlphaMode> alphaByTexture)
        {
            uvByTexture = new Dictionary<string, Rect>(StringComparer.Ordinal);
            alphaByTexture = new Dictionary<string, TextureAlphaMode>(StringComparer.Ordinal);
            if (zip == null)
            {
                return null;
            }

            var names = new List<string>(texturePaths);
            names.Sort(StringComparer.Ordinal);

            var textures = new List<Texture2D>(names.Count);
            var tileSize = 16;

            foreach (var fullName in names)
            {
                var tex = LoadTextureByPath(zip, fullName);
                if (tex == null)
                {
                    tex = LoadTextureByPath(zip, "minecraft:block/dirt");
                }

                if (tex == null)
                {
                    tex = new Texture2D(16, 16, TextureFormat.RGBA32, false);
                    tex.SetPixels(BuildFallbackPixels(16, 16));
                    tex.Apply();
                }

                tileSize = Mathf.Max(tileSize, tex.width);
                textures.Add(tex);
            }

            var count = textures.Count;
            if (count == 0)
            {
                return null;
            }

            var columns = Mathf.CeilToInt(Mathf.Sqrt(count));
            var rows = Mathf.CeilToInt((float)count / columns);
            var atlasSize = Mathf.NextPowerOfTwo(Mathf.Max(1, columns * tileSize));
            atlasSize = Mathf.Max(atlasSize, Mathf.NextPowerOfTwo(rows * tileSize));

            var atlas = new Texture2D(atlasSize, atlasSize, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Repeat
            };
            atlas.SetPixels(BuildFallbackPixels(atlasSize, atlasSize));

            for (var i = 0; i < textures.Count; i++)
            {
                var col = i % columns;
                var row = i / columns;
                var x = col * tileSize;
                var y = row * tileSize;
                CopyTextureToAtlas(textures[i], atlas, x, y, tileSize);

                var rect = new Rect(
                    (float)x / atlasSize,
                    (float)y / atlasSize,
                    (float)tileSize / atlasSize,
                    (float)tileSize / atlasSize);

                var name = names[i];
                uvByTexture[name] = rect;
                alphaByTexture[name] = DetermineTextureAlphaMode(textures[i]);
            }

            atlas.Apply();
            return atlas;
        }

        private static Texture2D LoadBlockTexture(ZipArchive zip, string blockName)
        {
            if (zip == null)
            {
                return null;
            }

            var textureName = blockName.Contains(":") ? blockName.Split(':')[1] : blockName;
            var path = $"assets/minecraft/textures/block/{textureName}.png";
            var entry = zip.GetEntry(path);
            if (entry == null)
            {
                return null;
            }

            using var stream = entry.Open();
            using var memory = new MemoryStream();
            stream.CopyTo(memory);
            var bytes = memory.ToArray();
            var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            if (!tex.LoadImage(bytes))
            {
                return null;
            }

            tex.filterMode = FilterMode.Point;
            tex.wrapMode = TextureWrapMode.Repeat;
            return tex;
        }

        private static Texture2D LoadTextureByPath(ZipArchive zip, string texturePath)
        {
            if (zip == null || string.IsNullOrEmpty(texturePath))
            {
                return null;
            }

            var path = texturePath;
            if (path.StartsWith("minecraft:"))
            {
                path = path.Substring("minecraft:".Length);
            }

            if (!path.StartsWith("block/") && !path.StartsWith("item/"))
            {
                path = "block/" + path;
            }

            var entry = zip.GetEntry($"assets/minecraft/textures/{path}.png");
            if (entry == null)
            {
                return null;
            }

            using var stream = entry.Open();
            using var memory = new MemoryStream();
            stream.CopyTo(memory);
            var bytes = memory.ToArray();
            var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            if (!tex.LoadImage(bytes))
            {
                return null;
            }

            tex.filterMode = FilterMode.Point;
            tex.wrapMode = TextureWrapMode.Repeat;
            return tex;
        }

        private static void CopyTextureToAtlas(Texture2D source, Texture2D atlas, int destX, int destY, int tileSize)
        {
            if (source == null || atlas == null)
            {
                return;
            }

            var width = source.width;
            var height = source.height;
            if (width == tileSize && height == tileSize)
            {
                var pixels = source.GetPixels();
                atlas.SetPixels(destX, destY, tileSize, tileSize, pixels);
                return;
            }

            var scaled = new Color[tileSize * tileSize];
            for (var y = 0; y < tileSize; y++)
            {
                var srcY = y * height / tileSize;
                for (var x = 0; x < tileSize; x++)
                {
                    var srcX = x * width / tileSize;
                    scaled[x + y * tileSize] = source.GetPixel(srcX, srcY);
                }
            }

            atlas.SetPixels(destX, destY, tileSize, tileSize, scaled);
        }

        private static TextureAlphaMode DetermineTextureAlphaMode(Texture2D texture)
        {
            if (texture == null)
            {
                return TextureAlphaMode.Opaque;
            }

            var pixels = texture.GetPixels32();
            var hasAlpha = false;
            var hasPartial = false;
            foreach (var pixel in pixels)
            {
                if (pixel.a == 255)
                {
                    continue;
                }

                hasAlpha = true;
                if (pixel.a != 0)
                {
                    hasPartial = true;
                    break;
                }
            }

            if (!hasAlpha)
            {
                return TextureAlphaMode.Opaque;
            }

            return hasPartial ? TextureAlphaMode.Translucent : TextureAlphaMode.Cutout;
        }

        private static Color[] BuildFallbackPixels(int width, int height)
        {
            var colors = new Color[width * height];
            for (var i = 0; i < colors.Length; i++)
            {
                colors[i] = new Color(1f, 0f, 1f, 1f);
            }

            return colors;
        }

        private static string GetDimensionFolder(string worldFolder, string dimensionId)
        {
            if (string.IsNullOrEmpty(worldFolder))
            {
                return string.Empty;
            }

            if (dimensionId == "minecraft:overworld")
            {
                return worldFolder;
            }

            if (dimensionId == "minecraft:the_nether")
            {
                return Path.Combine(worldFolder, "DIM-1");
            }

            if (dimensionId == "minecraft:the_end")
            {
                return Path.Combine(worldFolder, "DIM1");
            }

            if (string.IsNullOrEmpty(dimensionId) || !dimensionId.Contains(":"))
            {
                return string.Empty;
            }

            var parts = dimensionId.Split(':');
            return Path.Combine(worldFolder, "dimensions", parts[0], parts[1]);
        }

        private static long CountBlocksInChunk(string regionFolder, int chunkX, int chunkZ, Vector3Int min, Vector3Int max, ref bool logChunkOnce)
        {
            var chunk = LoadChunk(regionFolder, chunkX, chunkZ);
            if (chunk == null)
            {
                return 0;
            }

            if (logChunkOnce)
            {
                logChunkOnce = false;
                Debug.Log($"[Minecraft2VRChat] Chunk NBT (sample):\n{chunk}");
            }

            var baseTag = chunk;
            if (chunk["Level"] is NbtCompound levelCompound)
            {
                baseTag = levelCompound;
            }

            var sections = baseTag["Sections"] as NbtList ?? baseTag["sections"] as NbtList;
            if (sections == null)
            {
                return 0;
            }

            var chunkMinX = chunkX * 16;
            var chunkMinZ = chunkZ * 16;
            long count = 0;

            foreach (var sectionTag in sections)
            {
                if (sectionTag is not NbtCompound section)
                {
                    continue;
                }

                if (!TryGetSectionY(section, out var sectionY))
                {
                    continue;
                }

                var sectionMinY = sectionY * 16;
                var sectionMaxY = sectionMinY + 15;
                if (max.y < sectionMinY || min.y > sectionMaxY)
                {
                    continue;
                }

                if (!TryGetSectionBlockData(section, out var palette, out var blockStates))
                {
                    continue;
                }

                BuildPaletteCaches(palette, out var paletteNames, out var isAir);
                var bits = GetBitsForSection(palette.Count, blockStates);
                if (bits == 0)
                {
                    if (isAir[0])
                    {
                        continue;
                    }

                    var intersect = GetIntersection(chunkMinX, chunkMinZ, sectionMinY, min, max);
                    count += intersect;
                    continue;
                }

                if (blockStates == null || blockStates.Length == 0)
                {
                    continue;
                }

                var xMin = Mathf.Max(min.x, chunkMinX);
                var xMax = Mathf.Min(max.x, chunkMinX + 15);
                var zMin = Mathf.Max(min.z, chunkMinZ);
                var zMax = Mathf.Min(max.z, chunkMinZ + 15);
                var yMin = Mathf.Max(min.y, sectionMinY);
                var yMax = Mathf.Min(max.y, sectionMaxY);

                for (var y = yMin; y <= yMax; y++)
                {
                    var localY = y - sectionMinY;
                    for (var z = zMin; z <= zMax; z++)
                    {
                        var localZ = z - chunkMinZ;
                        for (var x = xMin; x <= xMax; x++)
                        {
                            var localX = x - chunkMinX;
                            var index = (localY << 8) | (localZ << 4) | localX;
                            var paletteIndex = GetBlockStateIndex(blockStates, index, bits);
                            if (paletteIndex >= 0 && paletteIndex < isAir.Length && !isAir[paletteIndex])
                            {
                                count++;
                            }
                        }
                    }
                }
            }

            return count;
        }

        private static bool FillBlockStateIds(string worldFolder, string dimensionId, Vector3Int min, Vector3Int max, int[] blocks, int sizeX, int sizeY, int sizeZ, List<BlockStateDefinition> states, ref bool logChunkOnce, bool logPaletteBounds)
        {
            var dimensionFolder = GetDimensionFolder(worldFolder, dimensionId);
            if (string.IsNullOrEmpty(dimensionFolder))
            {
                Debug.LogWarning($"[Minecraft2VRChat] Dimension folder not found for {dimensionId}.");
                return false;
            }

            var regionFolder = Path.Combine(dimensionFolder, "region");
            if (!Directory.Exists(regionFolder))
            {
                Debug.LogWarning($"[Minecraft2VRChat] Region folder not found: {regionFolder}");
                return false;
            }

            var chunkMinX = FloorDiv(min.x, 16);
            var chunkMaxX = FloorDiv(max.x, 16);
            var chunkMinZ = FloorDiv(min.z, 16);
            var chunkMaxZ = FloorDiv(max.z, 16);

            for (var cx = chunkMinX; cx <= chunkMaxX; cx++)
            {
                for (var cz = chunkMinZ; cz <= chunkMaxZ; cz++)
                {
                    FillBlockStateIdsInChunk(regionFolder, cx, cz, min, max, blocks, sizeX, sizeY, sizeZ, states, ref logChunkOnce, logPaletteBounds);
                }
            }

            return true;
        }

        private static void FillBlockStateIdsInChunk(string regionFolder, int chunkX, int chunkZ, Vector3Int min, Vector3Int max, int[] blocks, int sizeX, int sizeY, int sizeZ, List<BlockStateDefinition> states, ref bool logChunkOnce, bool logPaletteBounds)
        {
            var chunk = LoadChunk(regionFolder, chunkX, chunkZ);
            if (chunk == null)
            {
                return;
            }

            if (logChunkOnce)
            {
                logChunkOnce = false;
                Debug.Log($"[Minecraft2VRChat] Chunk NBT (sample):\n{chunk}");
            }

            var baseTag = chunk;
            if (chunk["Level"] is NbtCompound levelCompound)
            {
                baseTag = levelCompound;
            }

            var sections = baseTag["Sections"] as NbtList ?? baseTag["sections"] as NbtList;
            if (sections == null)
            {
                return;
            }

            var chunkMinX = chunkX * 16;
            var chunkMinZ = chunkZ * 16;

            foreach (var sectionTag in sections)
            {
                if (sectionTag is not NbtCompound section)
                {
                    continue;
                }

                if (!TryGetSectionY(section, out var sectionY))
                {
                    continue;
                }

                var sectionMinY = sectionY * 16;
                var sectionMaxY = sectionMinY + 15;
                if (max.y < sectionMinY || min.y > sectionMaxY)
                {
                    continue;
                }

                if (!TryGetSectionBlockData(section, out var palette, out var blockStates))
                {
                    continue;
                }

                var paletteIds = new int[palette.Count];
                var isAir = new bool[palette.Count];
                for (var i = 0; i < palette.Count; i++)
                {
                    var entry = palette[i];
                    var name = GetBlockName(entry);
                    isAir[i] = IsAirBlock(name);
                    paletteIds[i] = GetOrCreateBlockStateId(states, name, entry);
                }

                var bits = GetBitsForSection(palette.Count, blockStates);
                var xMin = Mathf.Max(min.x, chunkMinX);
                var xMax = Mathf.Min(max.x, chunkMinX + 15);
                var zMin = Mathf.Max(min.z, chunkMinZ);
                var zMax = Mathf.Min(max.z, chunkMinZ + 15);
                var yMin = Mathf.Max(min.y, sectionMinY);
                var yMax = Mathf.Min(max.y, sectionMaxY);

                if (bits == 0)
                {
                    if (!isAir[0])
                    {
                        var id = paletteIds[0];
                        if (id > 0)
                        {
                            for (var y = yMin; y <= yMax; y++)
                            {
                                for (var z = zMin; z <= zMax; z++)
                                {
                                    for (var x = xMin; x <= xMax; x++)
                                    {
                                        var outIndex = (x - min.x) + sizeX * ((y - min.y) + sizeY * (z - min.z));
                                        blocks[outIndex] = id;
                                    }
                                }
                            }
                        }
                    }

                    continue;
                }

                if (blockStates == null || blockStates.Length == 0)
                {
                    continue;
                }

                var invalidCount = 0;
                var totalCount = 0;

                for (var y = yMin; y <= yMax; y++)
                {
                    var localY = y - sectionMinY;
                    for (var z = zMin; z <= zMax; z++)
                    {
                        var localZ = z - chunkMinZ;
                        for (var x = xMin; x <= xMax; x++)
                        {
                            var localX = x - chunkMinX;
                            var index = (localY << 8) | (localZ << 4) | localX;
                            var paletteIndex = GetBlockStateIndex(blockStates, index, bits);
                            totalCount++;
                            if (paletteIndex >= 0 && paletteIndex < paletteIds.Length)
                            {
                                var id = paletteIds[paletteIndex];
                                if (id > 0 && !isAir[paletteIndex])
                                {
                                    var outIndex = (x - min.x) + sizeX * ((y - min.y) + sizeY * (z - min.z));
                                    blocks[outIndex] = id;
                                }
                            }
                            else
                            {
                                invalidCount++;
                            }
                        }
                    }
                }

                if (logPaletteBounds && invalidCount > 0)
                {
                    Debug.LogWarning($"[Minecraft2VRChat] Palette index out of range: {invalidCount}/{totalCount} " +
                                     $"(palette {paletteIds.Length}, bits {bits}, chunk {chunkX},{chunkZ}, sectionY {sectionY}).");
                }
            }
        }

        private static int GetOrCreateBlockStateId(List<BlockStateDefinition> states, string name, NbtCompound entry)
        {
            if (string.IsNullOrEmpty(name) || IsAirBlock(name))
            {
                return 0;
            }

            var properties = new Dictionary<string, string>(StringComparer.Ordinal);
            if (entry["Properties"] is NbtCompound props)
            {
                foreach (var tag in props.Tags)
                {
                    if (tag is NbtString str)
                    {
                        properties[tag.Name] = str.Value ?? string.Empty;
                    }
                }
            }

            var key = BuildStateKey(name, properties);
            for (var i = 1; i < states.Count; i++)
            {
                if (states[i].Key == key)
                {
                    return i;
                }
            }

            var state = new BlockStateDefinition(name, properties, key);
            states.Add(state);
            return states.Count - 1;
        }

        private static string BuildStateKey(string name, Dictionary<string, string> properties)
        {
            if (properties == null || properties.Count == 0)
            {
                return name;
            }

            var keys = new List<string>(properties.Keys);
            keys.Sort(StringComparer.Ordinal);
            var parts = new List<string>(keys.Count);
            foreach (var key in keys)
            {
                parts.Add($"{key}={properties[key]}");
            }

            return $"{name}|{string.Join(";", parts)}";
        }

        private static Mesh BuildModelMesh(int[] blocks, int sizeX, int sizeY, int sizeZ, Vector3Int min, List<List<ModelInstance>> modelCache, List<bool> fullCubeById, Dictionary<string, Rect> uvByTexture, Dictionary<string, TextureAlphaMode> alphaByTexture, bool applyCoordinateTransform)
        {
            var vertices = new List<Vector3>();
            var normals = new List<Vector3>();
            var uvs = new List<Vector2>();
            var trianglesSolid = new List<int>();
            var trianglesTranslucent = new List<int>();

            var dims = new[] { sizeX, sizeY, sizeZ };

            for (var z = 0; z < sizeZ; z++)
            {
                for (var y = 0; y < sizeY; y++)
                {
                    for (var x = 0; x < sizeX; x++)
                    {
                        var id = GetBlockId(blocks, dims, x, y, z);
                        if (id <= 0 || id >= modelCache.Count)
                        {
                            continue;
                        }

                        var models = modelCache[id];
                        if (models == null || models.Count == 0)
                        {
                            continue;
                        }

                        foreach (var instance in models)
                        {
                            EmitModel(instance, x, y, z, blocks, dims, fullCubeById, vertices, normals, uvs, trianglesSolid, trianglesTranslucent, uvByTexture, alphaByTexture);
                        }
                    }
                }
            }

            if (vertices.Count == 0)
            {
                return null;
            }

            ApplyCoordinateTransform(vertices, normals, trianglesSolid, min, applyCoordinateTransform);
            if (applyCoordinateTransform && trianglesTranslucent.Count > 0)
            {
                FlipTriangleWinding(trianglesTranslucent);
            }

            var mesh = new Mesh();
            mesh.indexFormat = vertices.Count > 65000 ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16;
            mesh.SetVertices(vertices);
            mesh.SetNormals(normals);
            mesh.SetUVs(0, uvs);
            if (trianglesTranslucent.Count > 0)
            {
                mesh.subMeshCount = 2;
                mesh.SetTriangles(trianglesSolid, 0);
                mesh.SetTriangles(trianglesTranslucent, 1);
            }
            else
            {
                mesh.subMeshCount = 1;
                mesh.SetTriangles(trianglesSolid, 0);
            }
            mesh.RecalculateBounds();
            return mesh;
        }

        private static void EmitModel(ModelInstance instance, int blockX, int blockY, int blockZ, int[] blocks, int[] dims,
            List<bool> fullCubeById,
            List<Vector3> vertices, List<Vector3> normals, List<Vector2> uvs, List<int> trianglesSolid, List<int> trianglesTranslucent,
            Dictionary<string, Rect> uvByTexture, Dictionary<string, TextureAlphaMode> alphaByTexture)
        {
            if (instance == null || instance.Model == null || instance.Model.Elements == null)
            {
                return;
            }

            var blockOffset = new Vector3(blockX, blockY, blockZ);
            var modelRotation = Quaternion.Euler(instance.RotateX, instance.RotateY, 0f);
            var modelOrigin = new Vector3(0.5f, 0.5f, 0.5f);

            foreach (var element in instance.Model.Elements)
            {
                var corners = element.GetCorners();
                if (element.Rotation != null)
                {
                    var rot = Quaternion.AngleAxis(element.Rotation.Angle, element.Rotation.AxisVector);
                    for (var i = 0; i < corners.Length; i++)
                    {
                        corners[i] = RotateAround(corners[i], element.Rotation.Origin, rot);
                    }
                }

                for (var i = 0; i < corners.Length; i++)
                {
                    corners[i] = RotateAround(corners[i], modelOrigin, modelRotation);
                }

                foreach (var faceEntry in element.Faces)
                {
                    var dir = faceEntry.Key;
                    var face = faceEntry.Value;

                    if (!string.IsNullOrEmpty(face.CullFace) && IsModelFullCube(instance.Model))
                    {
                        var cullDir = RotateDirection(ParseDirection(face.CullFace), instance.RotateX, instance.RotateY);
                        if (IsNeighborFullCube(blocks, dims, fullCubeById, blockX, blockY, blockZ, cullDir))
                        {
                            continue;
                        }
                    }

                    var quad = element.GetFaceQuad(dir, corners);
                    var normal = DirectionToNormal(RotateDirection(dir, instance.RotateX, instance.RotateY));
                    var texturePath = instance.Model.ResolveTexture(face.Texture);
                    var uvRect = ResolveUvRect(texturePath, face, uvByTexture, element, dir);
                    var alphaMode = ResolveAlphaMode(instance, texturePath, alphaByTexture);
                    var targetTriangles = alphaMode == TextureAlphaMode.Translucent ? trianglesTranslucent : trianglesSolid;
                    AddQuad(vertices, targetTriangles, normals, uvs,
                        quad[0] + blockOffset,
                        quad[1] + blockOffset,
                        quad[2] + blockOffset,
                        quad[3] + blockOffset,
                        normal, uvRect, 1f, 1f);
                }
            }
        }

        private static Rect ResolveUvRect(string texturePath, ModelFace face, Dictionary<string, Rect> uvByTexture, ModelElement element, Direction dir)
        {
            if (string.IsNullOrEmpty(texturePath) || uvByTexture == null || !uvByTexture.TryGetValue(texturePath, out var atlasRect))
            {
                return new Rect(0f, 0f, 1f, 1f);
            }

            var uv = face.Uv;
            if (uv == null || uv.Length != 4)
            {
                uv = element.DefaultUv(dir);
            }

            var u0 = uv[0] / 16f;
            var v0 = uv[1] / 16f;
            var u1 = uv[2] / 16f;
            var v1 = uv[3] / 16f;
            var rot = face.Rotation;
            if (rot != 0)
            {
                RotateUv(ref u0, ref v0, ref u1, ref v1, rot);
            }

            // Minecraft UV origin is top-left; Unity is bottom-left.
            var v0f = 1f - v1;
            var v1f = 1f - v0;
            v0 = v0f;
            v1 = v1f;

            var x = atlasRect.xMin + atlasRect.width * u0;
            var y = atlasRect.yMin + atlasRect.height * v0;
            var w = atlasRect.width * (u1 - u0);
            var h = atlasRect.height * (v1 - v0);
            return new Rect(x, y, w, h);
        }

        private static TextureAlphaMode ResolveAlphaMode(ModelInstance instance, string texturePath, Dictionary<string, TextureAlphaMode> alphaByTexture)
        {
            if (!string.IsNullOrEmpty(instance?.Model?.RenderType))
            {
                var renderType = instance.Model.RenderType.ToLowerInvariant();
                if (renderType.Contains("translucent"))
                {
                    return TextureAlphaMode.Translucent;
                }

                if (renderType.Contains("cutout"))
                {
                    return TextureAlphaMode.Cutout;
                }
            }

            if (!string.IsNullOrEmpty(texturePath) && alphaByTexture != null && alphaByTexture.TryGetValue(texturePath, out var mode))
            {
                return mode;
            }

            return TextureAlphaMode.Opaque;
        }

        private static void RotateUv(ref float u0, ref float v0, ref float u1, ref float v1, int rotation)
        {
            rotation = ((rotation % 360) + 360) % 360;
            if (rotation == 0)
            {
                return;
            }

            var corners = new[]
            {
                new Vector2(u0, v0),
                new Vector2(u1, v0),
                new Vector2(u1, v1),
                new Vector2(u0, v1)
            };

            var steps = rotation / 90;
            for (var i = 0; i < steps; i++)
            {
                var tmp = corners[0];
                corners[0] = corners[3];
                corners[3] = corners[2];
                corners[2] = corners[1];
                corners[1] = tmp;
            }

            u0 = corners[0].x;
            v0 = corners[0].y;
            u1 = corners[2].x;
            v1 = corners[2].y;
        }

        private static Vector3 RotateAround(Vector3 point, Vector3 origin, Quaternion rotation)
        {
            return rotation * (point - origin) + origin;
        }

        private static bool IsNeighborFullCube(int[] blocks, int[] dims, List<bool> fullCubeById, int x, int y, int z, Direction dir)
        {
            var nx = x;
            var ny = y;
            var nz = z;
            switch (dir)
            {
                case Direction.North: nz -= 1; break;
                case Direction.South: nz += 1; break;
                case Direction.West: nx -= 1; break;
                case Direction.East: nx += 1; break;
                case Direction.Down: ny -= 1; break;
                case Direction.Up: ny += 1; break;
            }

            var id = GetBlockId(blocks, dims, nx, ny, nz);
            return id > 0 && fullCubeById != null && id < fullCubeById.Count && fullCubeById[id];
        }

        private static Direction ParseDirection(string name)
        {
            return name switch
            {
                "north" => Direction.North,
                "south" => Direction.South,
                "west" => Direction.West,
                "east" => Direction.East,
                "up" => Direction.Up,
                "down" => Direction.Down,
                _ => Direction.North
            };
        }

        private static Direction RotateDirection(Direction dir, int rotX, int rotY)
        {
            var vector = DirectionToVector(dir);
            var q = Quaternion.Euler(rotX, rotY, 0f);
            var rotated = q * vector;
            return VectorToDirection(rotated);
        }

        private static Vector3 DirectionToVector(Direction dir)
        {
            return dir switch
            {
                Direction.North => new Vector3(0f, 0f, -1f),
                Direction.South => new Vector3(0f, 0f, 1f),
                Direction.West => new Vector3(-1f, 0f, 0f),
                Direction.East => new Vector3(1f, 0f, 0f),
                Direction.Up => new Vector3(0f, 1f, 0f),
                Direction.Down => new Vector3(0f, -1f, 0f),
                _ => Vector3.forward
            };
        }

        private static Direction VectorToDirection(Vector3 vector)
        {
            var v = new Vector3(Mathf.Round(vector.x), Mathf.Round(vector.y), Mathf.Round(vector.z));
            if (v.z < 0) return Direction.North;
            if (v.z > 0) return Direction.South;
            if (v.x < 0) return Direction.West;
            if (v.x > 0) return Direction.East;
            if (v.y > 0) return Direction.Up;
            return Direction.Down;
        }

        private static Vector3 DirectionToNormal(Direction dir)
        {
            return dir switch
            {
                Direction.North => new Vector3(0f, 0f, -1f),
                Direction.South => new Vector3(0f, 0f, 1f),
                Direction.West => new Vector3(-1f, 0f, 0f),
                Direction.East => new Vector3(1f, 0f, 0f),
                Direction.Up => new Vector3(0f, 1f, 0f),
                _ => new Vector3(0f, -1f, 0f)
            };
        }

        private static bool IsModelFullCube(BlockModel model)
        {
            if (model == null || model.Elements == null || model.Elements.Count == 0)
            {
                return false;
            }

            foreach (var element in model.Elements)
            {
                if (element == null || element.Rotation != null)
                {
                    return false;
                }

                if (element.From != Vector3.zero || element.To != new Vector3(16f, 16f, 16f))
                {
                    return false;
                }
            }

            return true;
        }

        private enum Direction
        {
            North,
            South,
            West,
            East,
            Up,
            Down
        }

        private sealed class BlockStateDefinition
        {
            public static readonly BlockStateDefinition Empty = new BlockStateDefinition("minecraft:air", new Dictionary<string, string>(), "minecraft:air");

            public string Name { get; }
            public Dictionary<string, string> Properties { get; }
            public string Key { get; }

            public BlockStateDefinition(string name, Dictionary<string, string> properties, string key)
            {
                Name = name;
                Properties = properties;
                Key = key;
            }
        }

        private sealed class ModelInstance
        {
            public BlockModel Model;
            public int RotateX;
            public int RotateY;
        }

        private sealed class BlockModel
        {
            public string Parent;
            public Dictionary<string, string> Textures = new Dictionary<string, string>(StringComparer.Ordinal);
            public List<ModelElement> Elements = new List<ModelElement>();
            public string RenderType;

            public string ResolveTexture(string texture)
            {
                if (string.IsNullOrEmpty(texture))
                {
                    return string.Empty;
                }

                var current = texture;
                var guard = 0;
                while (current.StartsWith("#") && guard++ < 16)
                {
                    var key = current.Substring(1);
                    if (Textures.TryGetValue(key, out var resolved))
                    {
                        current = resolved;
                    }
                    else
                    {
                        return string.Empty;
                    }
                }

                if (current.StartsWith("minecraft:"))
                {
                    return current;
                }

                return "minecraft:" + current;
            }
        }

        private sealed class ModelElement
        {
            public Vector3 From;
            public Vector3 To;
            public ElementRotation Rotation;
            public Dictionary<Direction, ModelFace> Faces = new Dictionary<Direction, ModelFace>();

            public Vector3[] GetCorners()
            {
                var min = From / 16f;
                var max = To / 16f;
                return new[]
                {
                    new Vector3(min.x, min.y, min.z),
                    new Vector3(max.x, min.y, min.z),
                    new Vector3(max.x, max.y, min.z),
                    new Vector3(min.x, max.y, min.z),
                    new Vector3(min.x, min.y, max.z),
                    new Vector3(max.x, min.y, max.z),
                    new Vector3(max.x, max.y, max.z),
                    new Vector3(min.x, max.y, max.z)
                };
            }

            public Vector3[] GetFaceQuad(Direction dir, Vector3[] corners)
            {
                return dir switch
                {
                    Direction.North => new[] { corners[0], corners[1], corners[2], corners[3] },
                    Direction.South => new[] { corners[5], corners[4], corners[7], corners[6] },
                    Direction.West => new[] { corners[0], corners[4], corners[7], corners[3] },
                    Direction.East => new[] { corners[5], corners[1], corners[2], corners[6] },
                    Direction.Down => new[] { corners[0], corners[1], corners[5], corners[4] },
                    _ => new[] { corners[3], corners[2], corners[6], corners[7] }
                };
            }

            public float[] DefaultUv(Direction dir)
            {
                var min = From;
                var max = To;
                return dir switch
                {
                    Direction.North => new[] { min.x, 16f - max.y, max.x, 16f - min.y },
                    Direction.South => new[] { min.x, 16f - max.y, max.x, 16f - min.y },
                    Direction.West => new[] { min.z, 16f - max.y, max.z, 16f - min.y },
                    Direction.East => new[] { min.z, 16f - max.y, max.z, 16f - min.y },
                    Direction.Down => new[] { min.x, min.z, max.x, max.z },
                    _ => new[] { min.x, min.z, max.x, max.z }
                };
            }
        }

        private sealed class ElementRotation
        {
            public Vector3 Origin;
            public Vector3 AxisVector;
            public float Angle;
        }

        private sealed class ModelFace
        {
            public float[] Uv;
            public string Texture;
            public string CullFace;
            public int Rotation;
        }

        private sealed class ModelResolver
        {
            private readonly ZipArchive _zip;
            private readonly Dictionary<string, BlockStateFile> _blockStateCache = new Dictionary<string, BlockStateFile>(StringComparer.Ordinal);
            private readonly Dictionary<string, BlockModel> _modelCache = new Dictionary<string, BlockModel>(StringComparer.Ordinal);

            public ModelResolver(ZipArchive zip)
            {
                _zip = zip;
            }

            public List<List<ModelInstance>> BuildBlockModels(List<BlockStateDefinition> states)
            {
                var result = new List<List<ModelInstance>>(states.Count);
                result.Add(new List<ModelInstance>());
                for (var i = 1; i < states.Count; i++)
                {
                    result.Add(ResolveBlockModels(states[i]));
                }

                return result;
            }

            public List<bool> BuildFullCubeFlags(List<List<ModelInstance>> modelCache)
            {
                var result = new List<bool>(modelCache.Count);
                result.Add(false);
                for (var i = 1; i < modelCache.Count; i++)
                {
                    var instances = modelCache[i];
                    var full = true;
                    if (instances == null || instances.Count == 0)
                    {
                        full = false;
                    }
                    else
                    {
                        foreach (var instance in instances)
                        {
                            if (instance?.Model == null || !IsModelFullCube(instance.Model))
                            {
                                full = false;
                                break;
                            }
                        }
                    }

                    result.Add(full);
                }

                return result;
            }

            public HashSet<string> CollectTexturePaths(List<List<ModelInstance>> modelCache)
            {
                var textures = new HashSet<string>(StringComparer.Ordinal);
                foreach (var list in modelCache)
                {
                    if (list == null)
                    {
                        continue;
                    }

                    foreach (var instance in list)
                    {
                        if (instance?.Model == null)
                        {
                            continue;
                        }

                        if (instance.Model.Textures != null)
                        {
                            foreach (var tex in instance.Model.Textures.Values)
                            {
                                var resolved = instance.Model.ResolveTexture(tex);
                                if (!string.IsNullOrEmpty(resolved))
                                {
                                    textures.Add(resolved);
                                }
                            }
                        }

                        if (instance.Model.Elements == null)
                        {
                            continue;
                        }

                        foreach (var element in instance.Model.Elements)
                        {
                            if (element?.Faces == null)
                            {
                                continue;
                            }

                            foreach (var face in element.Faces.Values)
                            {
                                var resolved = instance.Model.ResolveTexture(face.Texture);
                                if (!string.IsNullOrEmpty(resolved))
                                {
                                    textures.Add(resolved);
                                }
                            }
                        }
                    }
                }

                return textures;
            }

            private List<ModelInstance> ResolveBlockModels(BlockStateDefinition state)
            {
                var models = new List<ModelInstance>();
                var blockName = state.Name.Contains(":") ? state.Name.Split(':')[1] : state.Name;
                var blockStates = LoadBlockState(blockName);
                if (blockStates == null)
                {
                    models.Add(new ModelInstance { Model = ResolveModel("minecraft:block/" + blockName), RotateX = 0, RotateY = 0 });
                    return models;
                }

                if (blockStates.Variants != null)
                {
                    var best = FindBestVariant(blockStates.Variants, state.Properties);
                    if (best != null)
                    {
                        models.AddRange(ParseModelList(best));
                    }
                }

                if (blockStates.Multipart != null)
                {
                    foreach (var entry in blockStates.Multipart)
                    {
                        if (entry == null || !MultipartMatches(entry.When, state.Properties))
                        {
                            continue;
                        }

                        models.AddRange(ParseModelList(entry.Apply));
                    }
                }

                if (models.Count == 0)
                {
                    models.Add(new ModelInstance { Model = ResolveModel("minecraft:block/" + blockName), RotateX = 0, RotateY = 0 });
                }

                return models;
            }

            private BlockStateFile LoadBlockState(string blockName)
            {
                if (_blockStateCache.TryGetValue(blockName, out var cached))
                {
                    return cached;
                }

                var entry = _zip.GetEntry($"assets/minecraft/blockstates/{blockName}.json");
                if (entry == null)
                {
                    _blockStateCache[blockName] = null;
                    return null;
                }

                using var stream = entry.Open();
                using var reader = new StreamReader(stream);
                var json = reader.ReadToEnd();
                var data = JsonConvert.DeserializeObject<BlockStateFile>(json, JsonSettings);
                _blockStateCache[blockName] = data;
                return data;
            }

            private static List<ModelVariant> FindBestVariant(Dictionary<string, List<ModelVariant>> variants, Dictionary<string, string> props)
            {
                List<ModelVariant> best = null;
                var bestScore = -1;

                foreach (var pair in variants)
                {
                    if (!VariantMatches(pair.Key, props, out var score))
                    {
                        continue;
                    }

                    if (score > bestScore)
                    {
                        bestScore = score;
                        best = pair.Value;
                    }
                }

                return best;
            }

            private static bool VariantMatches(string key, Dictionary<string, string> props, out int score)
            {
                score = 0;
                if (string.IsNullOrEmpty(key))
                {
                    return props == null || props.Count == 0;
                }

                var parts = key.Split(',');
                foreach (var part in parts)
                {
                    var kv = part.Split('=');
                    if (kv.Length != 2)
                    {
                        continue;
                    }

                    if (props == null || !props.TryGetValue(kv[0], out var value))
                    {
                        return false;
                    }

                    if (!ValueMatches(value, kv[1]))
                    {
                        return false;
                    }

                    score++;
                }

                return true;
            }

            private static bool ValueMatches(string value, string selector)
            {
                var options = selector.Split('|');
                foreach (var option in options)
                {
                    if (string.Equals(value, option, StringComparison.Ordinal))
                    {
                        return true;
                    }
                }

                return false;
            }

            private static bool MultipartMatches(WhenCondition when, Dictionary<string, string> props)
            {
                if (when == null)
                {
                    return true;
                }

                if (when.Or != null && when.Or.Count > 0)
                {
                    foreach (var option in when.Or)
                    {
                        if (WhenMatches(option, props))
                        {
                            return true;
                        }
                    }

                    return false;
                }

                return WhenMatches(when.Props, props);
            }

            private static bool WhenMatches(Dictionary<string, string> whenDict, Dictionary<string, string> props)
            {
                if (whenDict == null || whenDict.Count == 0)
                {
                    return true;
                }

                foreach (var pair in whenDict)
                {
                    var key = pair.Key;
                    var expected = pair.Value ?? string.Empty;
                    if (!props.TryGetValue(key, out var value))
                    {
                        return false;
                    }

                    if (!ValueMatches(value, expected))
                    {
                        return false;
                    }
                }

                return true;
            }

            private List<ModelInstance> ParseModelList(List<ModelVariant> variants)
            {
                var result = new List<ModelInstance>();
                if (variants == null || variants.Count == 0)
                {
                    return result;
                }

                var chosen = ChooseWeightedVariant(variants);
                if (chosen != null)
                {
                    var instance = ParseModel(chosen);
                    if (instance != null)
                    {
                        result.Add(instance);
                    }
                }

                return result;
            }

            private ModelInstance ParseModel(ModelVariant variant)
            {
                if (variant == null || string.IsNullOrEmpty(variant.Model))
                {
                    return null;
                }

                var instance = new ModelInstance
                {
                    Model = ResolveModel(variant.Model),
                    RotateX = variant.X ?? 0,
                    RotateY = variant.Y ?? 0
                };
                return instance;
            }

            private BlockModel ResolveModel(string modelName)
            {
                var normalized = NormalizeModelName(modelName);
                if (_modelCache.TryGetValue(normalized, out var cached))
                {
                    return cached;
                }

                var entry = _zip.GetEntry($"assets/minecraft/models/{normalized}.json");
                if (entry == null)
                {
                    var fallback = new BlockModel { Textures = new Dictionary<string, string>(StringComparer.Ordinal) };
                    _modelCache[normalized] = fallback;
                    return fallback;
                }

                using var stream = entry.Open();
                using var reader = new StreamReader(stream);
                var json = reader.ReadToEnd();
                var data = JsonConvert.DeserializeObject<ModelFile>(json, JsonSettings);
                var model = ParseModelData(data);
                _modelCache[normalized] = model;

                if (!string.IsNullOrEmpty(model.Parent))
                {
                    var parent = ResolveModel(model.Parent);
                    if (model.Elements == null || model.Elements.Count == 0)
                    {
                        model.Elements = parent.Elements;
                    }

                    foreach (var pair in parent.Textures)
                    {
                        if (!model.Textures.ContainsKey(pair.Key))
                        {
                            model.Textures[pair.Key] = pair.Value;
                        }
                    }
                }

                return model;
            }

            private static string NormalizeModelName(string modelName)
            {
                var name = modelName;
                if (name.StartsWith("minecraft:"))
                {
                    name = name.Substring("minecraft:".Length);
                }

                if (!name.Contains("/"))
                {
                    name = "block/" + name;
                }

                return name;
            }

            private static BlockModel ParseModelData(ModelFile data)
            {
                var model = new BlockModel();
                if (data == null)
                {
                    return model;
                }

                model.Parent = data.Parent;
                model.RenderType = data.RenderType;

                if (data.Textures != null)
                {
                    foreach (var pair in data.Textures)
                    {
                        model.Textures[pair.Key] = pair.Value;
                    }
                }

                if (data.Elements != null)
                {
                    foreach (var elementDef in data.Elements)
                    {
                        var element = ParseElement(elementDef);
                        if (element != null)
                        {
                            model.Elements.Add(element);
                        }
                    }
                }

                return model;
            }

            private static ModelElement ParseElement(ModelElementDef def)
            {
                var element = new ModelElement();
                if (def.From != null && def.From.Length >= 3)
                {
                    element.From = new Vector3(def.From[0], def.From[1], def.From[2]);
                }
                if (def.To != null && def.To.Length >= 3)
                {
                    element.To = new Vector3(def.To[0], def.To[1], def.To[2]);
                }

                if (def.Rotation != null)
                {
                    var rotation = new ElementRotation();
                    if (def.Rotation.Origin != null && def.Rotation.Origin.Length >= 3)
                    {
                        rotation.Origin = new Vector3(def.Rotation.Origin[0], def.Rotation.Origin[1], def.Rotation.Origin[2]) / 16f;
                    }
                    if (!string.IsNullOrEmpty(def.Rotation.Axis))
                    {
                        rotation.AxisVector = def.Rotation.Axis switch
                        {
                            "x" => Vector3.right,
                            "y" => Vector3.up,
                            "z" => Vector3.forward,
                            _ => Vector3.up
                        };
                    }
                    rotation.Angle = def.Rotation.Angle;
                    element.Rotation = rotation;
                }

                if (def.Faces != null)
                {
                    foreach (var facePair in def.Faces)
                    {
                        var dir = ParseDirection(facePair.Key);
                        var face = new ModelFace();
                        face.Uv = facePair.Value.Uv;
                        face.Texture = facePair.Value.Texture;
                        face.CullFace = facePair.Value.CullFace;
                        face.Rotation = facePair.Value.Rotation;
                        element.Faces[dir] = face;
                    }
                }

                return element;
            }
        }

        private static readonly JsonSerializerSettings JsonSettings = new JsonSerializerSettings
        {
            MissingMemberHandling = MissingMemberHandling.Ignore,
            NullValueHandling = NullValueHandling.Ignore,
            Converters = new List<JsonConverter>
            {
                new VariantListConverter(),
                new ApplyListConverter(),
                new WhenConverter(),
                new ModelFacesConverter()
            }
        };

        private static readonly JsonSerializer SafeSerializer = JsonSerializer.Create(new JsonSerializerSettings
        {
            MissingMemberHandling = MissingMemberHandling.Ignore,
            NullValueHandling = NullValueHandling.Ignore
        });

        private sealed class BlockStateFile
        {
            [JsonProperty("variants")]
            public Dictionary<string, List<ModelVariant>> Variants { get; set; }

            [JsonProperty("multipart")]
            public List<MultipartEntry> Multipart { get; set; }
        }

        private sealed class MultipartEntry
        {
            [JsonProperty("when")]
            public WhenCondition When { get; set; }

            [JsonProperty("apply")]
            [JsonConverter(typeof(ApplyListConverter))]
            public List<ModelVariant> Apply { get; set; }
        }

        private sealed class WhenCondition
        {
            public Dictionary<string, string> Props { get; set; }
            public List<Dictionary<string, string>> Or { get; set; }
        }

        private sealed class ModelVariant
        {
            [JsonProperty("model")]
            public string Model { get; set; }

            [JsonProperty("x")]
            public int? X { get; set; }

            [JsonProperty("y")]
            public int? Y { get; set; }

            [JsonProperty("uvlock")]
            public bool? UvLock { get; set; }

            [JsonProperty("weight")]
            public int? Weight { get; set; }
        }

        private sealed class ModelFile
        {
            [JsonProperty("parent")]
            public string Parent { get; set; }

            [JsonProperty("textures")]
            public Dictionary<string, string> Textures { get; set; }

            [JsonProperty("elements")]
            public List<ModelElementDef> Elements { get; set; }

            [JsonProperty("render_type")]
            public string RenderType { get; set; }
        }

        private sealed class ModelElementDef
        {
            [JsonProperty("from")]
            public float[] From { get; set; }

            [JsonProperty("to")]
            public float[] To { get; set; }

            [JsonProperty("rotation")]
            public ElementRotationDef Rotation { get; set; }

            [JsonProperty("faces")]
            [JsonConverter(typeof(ModelFacesConverter))]
            public Dictionary<string, ModelFaceDef> Faces { get; set; }
        }

        private sealed class ElementRotationDef
        {
            [JsonProperty("origin")]
            public float[] Origin { get; set; }

            [JsonProperty("axis")]
            public string Axis { get; set; }

            [JsonProperty("angle")]
            public float Angle { get; set; }
        }

        private sealed class ModelFaceDef
        {
            [JsonProperty("uv")]
            public float[] Uv { get; set; }

            [JsonProperty("texture")]
            public string Texture { get; set; }

            [JsonProperty("cullface")]
            public string CullFace { get; set; }

            [JsonProperty("rotation")]
            public int Rotation { get; set; }

            [JsonProperty("tintindex")]
            public int? TintIndex { get; set; }
        }

        private sealed class VariantListConverter : JsonConverter<List<ModelVariant>>
        {
            public override List<ModelVariant> ReadJson(JsonReader reader, Type objectType, List<ModelVariant> existingValue, bool hasExistingValue, JsonSerializer serializer)
            {
                var token = JToken.Load(reader);
                return DeserializeVariantList(token);
            }

            public override void WriteJson(JsonWriter writer, List<ModelVariant> value, JsonSerializer serializer)
            {
                serializer.Serialize(writer, value);
            }
        }

        private sealed class ApplyListConverter : JsonConverter<List<ModelVariant>>
        {
            public override List<ModelVariant> ReadJson(JsonReader reader, Type objectType, List<ModelVariant> existingValue, bool hasExistingValue, JsonSerializer serializer)
            {
                var token = JToken.Load(reader);
                return DeserializeVariantList(token);
            }

            public override void WriteJson(JsonWriter writer, List<ModelVariant> value, JsonSerializer serializer)
            {
                serializer.Serialize(writer, value);
            }
        }

        private sealed class WhenConverter : JsonConverter<WhenCondition>
        {
            public override WhenCondition ReadJson(JsonReader reader, Type objectType, WhenCondition existingValue, bool hasExistingValue, JsonSerializer serializer)
            {
                if (reader.TokenType != JsonToken.StartObject)
                {
                    return null;
                }

                var obj = JObject.Load(reader);
                if (obj.TryGetValue("OR", out var orToken))
                {
                    var list = new List<Dictionary<string, string>>();
                    foreach (var child in orToken.Children<JObject>())
                    {
                        list.Add(child.ToObject<Dictionary<string, string>>());
                    }
                    return new WhenCondition { Or = list };
                }

                return new WhenCondition { Props = obj.ToObject<Dictionary<string, string>>() };
            }

            public override void WriteJson(JsonWriter writer, WhenCondition value, JsonSerializer serializer)
            {
                serializer.Serialize(writer, value);
            }
        }

        private sealed class ModelFacesConverter : JsonConverter<Dictionary<string, ModelFaceDef>>
        {
            public override Dictionary<string, ModelFaceDef> ReadJson(JsonReader reader, Type objectType, Dictionary<string, ModelFaceDef> existingValue, bool hasExistingValue, JsonSerializer serializer)
            {
                if (reader.TokenType != JsonToken.StartObject)
                {
                    return null;
                }

                var obj = JObject.Load(reader);
                var dict = new Dictionary<string, ModelFaceDef>(StringComparer.Ordinal);
                foreach (var prop in obj.Properties())
                {
                    dict[prop.Name] = prop.Value.ToObject<ModelFaceDef>();
                }
                return dict;
            }

            public override void WriteJson(JsonWriter writer, Dictionary<string, ModelFaceDef> value, JsonSerializer serializer)
            {
                serializer.Serialize(writer, value);
            }
        }

        private static ModelVariant ChooseWeightedVariant(List<ModelVariant> variants)
        {
            if (variants == null || variants.Count == 0)
            {
                return null;
            }

            ModelVariant best = null;
            var bestWeight = -1;
            foreach (var variant in variants)
            {
                var weight = variant?.Weight ?? 1;
                if (weight > bestWeight)
                {
                    bestWeight = weight;
                    best = variant;
                }
            }

            return best ?? variants[0];
        }

        private static List<ModelVariant> DeserializeVariantList(JToken token)
        {
            if (token == null)
            {
                return new List<ModelVariant>();
            }

            if (token.Type == JTokenType.Array)
            {
                var list = new List<ModelVariant>();
                foreach (var child in token.Children())
                {
                    var item = child.ToObject<ModelVariant>(SafeSerializer);
                    if (item != null)
                    {
                        list.Add(item);
                    }
                }
                return list;
            }

            if (token.Type == JTokenType.Object)
            {
                var single = token.ToObject<ModelVariant>(SafeSerializer);
                return single != null ? new List<ModelVariant> { single } : new List<ModelVariant>();
            }

            return new List<ModelVariant>();
        }

        private static bool FillBlockIds(string worldFolder, string dimensionId, Vector3Int min, Vector3Int max, int[] blocks, int sizeX, int sizeY, int sizeZ, Dictionary<string, int> idByName, ref bool logChunkOnce, bool logPaletteBounds)
        {
            var dimensionFolder = GetDimensionFolder(worldFolder, dimensionId);
            if (string.IsNullOrEmpty(dimensionFolder))
            {
                Debug.LogWarning($"[Minecraft2VRChat] Dimension folder not found for {dimensionId}.");
                return false;
            }

            var regionFolder = Path.Combine(dimensionFolder, "region");
            if (!Directory.Exists(regionFolder))
            {
                Debug.LogWarning($"[Minecraft2VRChat] Region folder not found: {regionFolder}");
                return false;
            }

            var chunkMinX = FloorDiv(min.x, 16);
            var chunkMaxX = FloorDiv(max.x, 16);
            var chunkMinZ = FloorDiv(min.z, 16);
            var chunkMaxZ = FloorDiv(max.z, 16);

            for (var cx = chunkMinX; cx <= chunkMaxX; cx++)
            {
                for (var cz = chunkMinZ; cz <= chunkMaxZ; cz++)
                {
                    FillBlockIdsInChunk(regionFolder, cx, cz, min, max, blocks, sizeX, sizeY, sizeZ, idByName, ref logChunkOnce, logPaletteBounds);
                }
            }

            return true;
        }

        private static void FillBlockIdsInChunk(string regionFolder, int chunkX, int chunkZ, Vector3Int min, Vector3Int max, int[] blocks, int sizeX, int sizeY, int sizeZ, Dictionary<string, int> idByName, ref bool logChunkOnce, bool logPaletteBounds)
        {
            var chunk = LoadChunk(regionFolder, chunkX, chunkZ);
            if (chunk == null)
            {
                return;
            }

            if (logChunkOnce)
            {
                logChunkOnce = false;
                Debug.Log($"[Minecraft2VRChat] Chunk NBT (sample):\n{chunk}");
            }

            var baseTag = chunk;
            if (chunk["Level"] is NbtCompound levelCompound)
            {
                baseTag = levelCompound;
            }

            var sections = baseTag["Sections"] as NbtList ?? baseTag["sections"] as NbtList;
            if (sections == null)
            {
                return;
            }

            var chunkMinX = chunkX * 16;
            var chunkMinZ = chunkZ * 16;

            foreach (var sectionTag in sections)
            {
                if (sectionTag is not NbtCompound section)
                {
                    continue;
                }

                if (!TryGetSectionY(section, out var sectionY))
                {
                    continue;
                }

                var sectionMinY = sectionY * 16;
                var sectionMaxY = sectionMinY + 15;
                if (max.y < sectionMinY || min.y > sectionMaxY)
                {
                    continue;
                }

                if (!TryGetSectionBlockData(section, out var palette, out var blockStates))
                {
                    continue;
                }

                var paletteIds = new int[palette.Count];
                for (var i = 0; i < palette.Count; i++)
                {
                    var name = GetBlockName(palette[i]);
                    paletteIds[i] = ResolveBlockId(idByName, name);
                }
                BuildPaletteCaches(palette, out _, out var isAir);

                var bits = GetBitsForSection(palette.Count, blockStates);
                var xMin = Mathf.Max(min.x, chunkMinX);
                var xMax = Mathf.Min(max.x, chunkMinX + 15);
                var zMin = Mathf.Max(min.z, chunkMinZ);
                var zMax = Mathf.Min(max.z, chunkMinZ + 15);
                var yMin = Mathf.Max(min.y, sectionMinY);
                var yMax = Mathf.Min(max.y, sectionMaxY);

                if (bits == 0)
                {
                    if (!isAir[0])
                    {
                        var id = paletteIds[0];
                        if (id > 0)
                        {
                            for (var y = yMin; y <= yMax; y++)
                            {
                                for (var z = zMin; z <= zMax; z++)
                                {
                                    for (var x = xMin; x <= xMax; x++)
                                    {
                                        var outIndex = (x - min.x) + sizeX * ((y - min.y) + sizeY * (z - min.z));
                                        blocks[outIndex] = id;
                                    }
                                }
                            }
                        }
                    }

                    continue;
                }

                if (blockStates == null || blockStates.Length == 0)
                {
                    continue;
                }

                var invalidCount = 0;
                var totalCount = 0;

                for (var y = yMin; y <= yMax; y++)
                {
                    var localY = y - sectionMinY;
                    for (var z = zMin; z <= zMax; z++)
                    {
                        var localZ = z - chunkMinZ;
                        for (var x = xMin; x <= xMax; x++)
                        {
                            var localX = x - chunkMinX;
                            var index = (localY << 8) | (localZ << 4) | localX;
                            var paletteIndex = GetBlockStateIndex(blockStates, index, bits);
                            totalCount++;
                            if (paletteIndex >= 0 && paletteIndex < paletteIds.Length)
                            {
                                var id = paletteIds[paletteIndex];
                                if (id > 0 && !isAir[paletteIndex])
                                {
                                    var outIndex = (x - min.x) + sizeX * ((y - min.y) + sizeY * (z - min.z));
                                    blocks[outIndex] = id;
                                }
                            }
                            else
                            {
                                invalidCount++;
                            }
                        }
                    }
                }

                if (logPaletteBounds && invalidCount > 0)
                {
                    Debug.LogWarning($"[Minecraft2VRChat] Palette index out of range: {invalidCount}/{totalCount} " +
                                     $"(palette {paletteIds.Length}, bits {bits}, chunk {chunkX},{chunkZ}, sectionY {sectionY}).");
                }
            }
        }

        private static int ResolveBlockId(Dictionary<string, int> idByName, string name)
        {
            if (idByName == null)
            {
                return IsAirBlock(name) ? 0 : 1;
            }

            if (string.IsNullOrEmpty(name))
            {
                return 0;
            }

            if (idByName.TryGetValue(name, out var id))
            {
                return id;
            }

            return 0;
        }

        private static Mesh BuildGreedyMesh(int[] blocks, List<Rect> uvById, Vector3Int min, int sizeX, int sizeY, int sizeZ, bool applyCoordinateTransform)
        {
            var vertices = new List<Vector3>();
            var normals = new List<Vector3>();
            var uvs = new List<Vector2>();
            var triangles = new List<int>();

            var dims = new[] { sizeX, sizeY, sizeZ };
            var x = new int[3];
            var q = new int[3];

            for (var d = 0; d < 3; d++)
            {
                var u = (d + 1) % 3;
                var v = (d + 2) % 3;
                q[0] = q[1] = q[2] = 0;
                q[d] = 1;

                var mask = new int[dims[u] * dims[v]];
                for (x[d] = -1; x[d] < dims[d];)
                {
                    var n = 0;
                    for (x[v] = 0; x[v] < dims[v]; x[v]++)
                    {
                        for (x[u] = 0; x[u] < dims[u]; x[u]++)
                        {
                            var a = x[d] >= 0 ? GetBlockId(blocks, dims, x[0], x[1], x[2]) : 0;
                            var b = x[d] < dims[d] - 1 ? GetBlockId(blocks, dims, x[0] + q[0], x[1] + q[1], x[2] + q[2]) : 0;
                            var aSolid = a > 0;
                            var bSolid = b > 0;
                            if (aSolid == bSolid)
                            {
                                mask[n++] = 0;
                            }
                            else
                            {
                                mask[n++] = aSolid ? a : -b;
                            }
                        }
                    }

                    x[d]++;

                    n = 0;
                    for (var j = 0; j < dims[v]; j++)
                    {
                        for (var i = 0; i < dims[u];)
                        {
                            var c = mask[n];
                            if (c == 0)
                            {
                                i++;
                                n++;
                                continue;
                            }

                            var w = 1;
                            while (i + w < dims[u] && mask[n + w] == c)
                            {
                                w++;
                            }

                            var h = 1;
                            while (j + h < dims[v])
                            {
                                var k = 0;
                                while (k < w && mask[n + k + h * dims[u]] == c)
                                {
                                    k++;
                                }

                                if (k < w)
                                {
                                    break;
                                }

                                h++;
                            }

                            var du = new int[3];
                            var dv = new int[3];
                            du[u] = w;
                            dv[v] = h;

                            x[u] = i;
                            x[v] = j;
                            var x0 = x[0];
                            var y0 = x[1];
                            var z0 = x[2];

                            var v0 = new Vector3(x0, y0, z0);
                            var v1 = new Vector3(x0 + du[0], y0 + du[1], z0 + du[2]);
                            var v2 = new Vector3(x0 + dv[0], y0 + dv[1], z0 + dv[2]);
                            var v3 = new Vector3(x0 + du[0] + dv[0], y0 + du[1] + dv[1], z0 + du[2] + dv[2]);

                            var uvWidth = w;
                            var uvHeight = h;
                            var uvRect = GetUvRect(uvById, Mathf.Abs(c));

                            if (c > 0)
                            {
                                AddQuad(vertices, triangles, normals, uvs, v0, v2, v3, v1, AxisNormal(d, 1), uvRect, uvWidth, uvHeight);
                            }
                            else
                            {
                                AddQuad(vertices, triangles, normals, uvs, v0, v1, v3, v2, AxisNormal(d, -1), uvRect, uvWidth, uvHeight);
                            }

                            for (var l = 0; l < h; l++)
                            {
                                for (var k2 = 0; k2 < w; k2++)
                                {
                                    mask[n + k2 + l * dims[u]] = 0;
                                }
                            }

                            i += w;
                            n += w;
                        }
                    }
                }
            }

            if (vertices.Count == 0)
            {
                return null;
            }

            ApplyCoordinateTransform(vertices, normals, triangles, min, applyCoordinateTransform);

            var mesh = new Mesh();
            mesh.indexFormat = vertices.Count > 65000 ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16;
            mesh.SetVertices(vertices);
            mesh.SetNormals(normals);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateBounds();
            return mesh;
        }

        private static Mesh BuildNaiveMesh(int[] blocks, List<Rect> uvById, Vector3Int min, int sizeX, int sizeY, int sizeZ, bool applyCoordinateTransform)
        {
            var vertices = new List<Vector3>();
            var normals = new List<Vector3>();
            var uvs = new List<Vector2>();
            var triangles = new List<int>();

            var dims = new[] { sizeX, sizeY, sizeZ };

            for (var z = 0; z < sizeZ; z++)
            {
                for (var y = 0; y < sizeY; y++)
                {
                    for (var x = 0; x < sizeX; x++)
                    {
                        var id = GetBlockId(blocks, dims, x, y, z);
                        if (id <= 0)
                        {
                            continue;
                        }

                        var uvRect = GetUvRect(uvById, id);

                        // -X
                        if (GetBlockId(blocks, dims, x - 1, y, z) == 0)
                        {
                            AddQuad(vertices, triangles, normals, uvs,
                                new Vector3(x, y, z),
                                new Vector3(x, y + 1, z),
                                new Vector3(x, y + 1, z + 1),
                                new Vector3(x, y, z + 1),
                                new Vector3(-1, 0, 0), uvRect, 1, 1);
                        }

                        // +X
                        if (GetBlockId(blocks, dims, x + 1, y, z) == 0)
                        {
                            AddQuad(vertices, triangles, normals, uvs,
                                new Vector3(x + 1, y, z),
                                new Vector3(x + 1, y, z + 1),
                                new Vector3(x + 1, y + 1, z + 1),
                                new Vector3(x + 1, y + 1, z),
                                new Vector3(1, 0, 0), uvRect, 1, 1);
                        }

                        // -Y
                        if (GetBlockId(blocks, dims, x, y - 1, z) == 0)
                        {
                            AddQuad(vertices, triangles, normals, uvs,
                                new Vector3(x, y, z),
                                new Vector3(x, y, z + 1),
                                new Vector3(x + 1, y, z + 1),
                                new Vector3(x + 1, y, z),
                                new Vector3(0, -1, 0), uvRect, 1, 1);
                        }

                        // +Y
                        if (GetBlockId(blocks, dims, x, y + 1, z) == 0)
                        {
                            AddQuad(vertices, triangles, normals, uvs,
                                new Vector3(x, y + 1, z),
                                new Vector3(x + 1, y + 1, z),
                                new Vector3(x + 1, y + 1, z + 1),
                                new Vector3(x, y + 1, z + 1),
                                new Vector3(0, 1, 0), uvRect, 1, 1);
                        }

                        // -Z
                        if (GetBlockId(blocks, dims, x, y, z - 1) == 0)
                        {
                            AddQuad(vertices, triangles, normals, uvs,
                                new Vector3(x, y, z),
                                new Vector3(x + 1, y, z),
                                new Vector3(x + 1, y + 1, z),
                                new Vector3(x, y + 1, z),
                                new Vector3(0, 0, -1), uvRect, 1, 1);
                        }

                        // +Z
                        if (GetBlockId(blocks, dims, x, y, z + 1) == 0)
                        {
                            AddQuad(vertices, triangles, normals, uvs,
                                new Vector3(x, y, z + 1),
                                new Vector3(x, y + 1, z + 1),
                                new Vector3(x + 1, y + 1, z + 1),
                                new Vector3(x + 1, y, z + 1),
                                new Vector3(0, 0, 1), uvRect, 1, 1);
                        }
                    }
                }
            }

            if (vertices.Count == 0)
            {
                return null;
            }

            ApplyCoordinateTransform(vertices, normals, triangles, min, applyCoordinateTransform);

            var mesh = new Mesh();
            mesh.indexFormat = vertices.Count > 65000 ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16;
            mesh.SetVertices(vertices);
            mesh.SetNormals(normals);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateBounds();
            return mesh;
        }

        private static void ApplyCoordinateTransform(List<Vector3> vertices, List<Vector3> normals, List<int> triangles, Vector3Int min, bool applyCoordinateTransform)
        {
            for (var i = 0; i < vertices.Count; i++)
            {
                var v = vertices[i] + min;
                vertices[i] = applyCoordinateTransform ? new Vector3(v.x, v.y, -v.z) : v;
            }

            if (!applyCoordinateTransform)
            {
                return;
            }

            for (var i = 0; i < normals.Count; i++)
            {
                var n = normals[i];
                normals[i] = new Vector3(n.x, n.y, -n.z);
            }

            FlipTriangleWinding(triangles);
        }

        private static void FlipTriangleWinding(List<int> triangles)
        {
            for (var i = 0; i < triangles.Count; i += 3)
            {
                var tmp = triangles[i + 1];
                triangles[i + 1] = triangles[i + 2];
                triangles[i + 2] = tmp;
            }
        }

        private static int GetBlockId(int[] blocks, int[] dims, int x, int y, int z)
        {
            if (x < 0 || y < 0 || z < 0 || x >= dims[0] || y >= dims[1] || z >= dims[2])
            {
                return 0;
            }

            var index = x + dims[0] * (y + dims[1] * z);
            return blocks[index];
        }

        private static Rect GetUvRect(List<Rect> uvById, int id)
        {
            if (uvById == null || id <= 0 || id >= uvById.Count)
            {
                return new Rect(0f, 0f, 1f, 1f);
            }

            return uvById[id];
        }

        private static Vector3 AxisNormal(int axis, int sign)
        {
            return axis switch
            {
                0 => new Vector3(sign, 0, 0),
                1 => new Vector3(0, sign, 0),
                _ => new Vector3(0, 0, sign)
            };
        }

        private static void AddQuad(List<Vector3> vertices, List<int> triangles, List<Vector3> normals, List<Vector2> uvs, Vector3 v0, Vector3 v1, Vector3 v2, Vector3 v3, Vector3 normal, Rect uvRect, float uvWidth, float uvHeight)
        {
            var start = vertices.Count;
            vertices.Add(v0);
            vertices.Add(v1);
            vertices.Add(v2);
            vertices.Add(v3);
            normals.Add(normal);
            normals.Add(normal);
            normals.Add(normal);
            normals.Add(normal);

            var u0 = uvRect.xMin;
            var v0uv = uvRect.yMin;
            var u1 = uvRect.xMin + uvRect.width * uvWidth;
            var v1uv = uvRect.yMin + uvRect.height * uvHeight;
            uvs.Add(new Vector2(u0, v0uv));
            uvs.Add(new Vector2(u1, v0uv));
            uvs.Add(new Vector2(u1, v1uv));
            uvs.Add(new Vector2(u0, v1uv));

            triangles.Add(start);
            triangles.Add(start + 1);
            triangles.Add(start + 2);
            triangles.Add(start);
            triangles.Add(start + 2);
            triangles.Add(start + 3);
        }

        private static void LogSolidSliceStats(int[] blocks, int sizeX, int sizeY, int sizeZ, Vector3Int min)
        {
            const int maxSlices = 5;
            if (blocks == null || blocks.Length == 0)
            {
                return;
            }

            var step = Mathf.Max(1, sizeZ / maxSlices);
            for (var z = 0; z < sizeZ; z += step)
            {
                var count = 0;
                for (var y = 0; y < sizeY; y++)
                {
                    for (var x = 0; x < sizeX; x++)
                    {
                        var index = x + sizeX * (y + sizeY * z);
                        if (blocks[index] > 0)
                        {
                            count++;
                        }
                    }
                }

                Debug.Log($"[Minecraft2VRChat] Solids slice z={z + min.z}: {count} blocks.");
            }
        }

        private static long GetIntersection(int chunkMinX, int chunkMinZ, int sectionMinY, Vector3Int min, Vector3Int max)
        {
            var xMin = Mathf.Max(min.x, chunkMinX);
            var xMax = Mathf.Min(max.x, chunkMinX + 15);
            var zMin = Mathf.Max(min.z, chunkMinZ);
            var zMax = Mathf.Min(max.z, chunkMinZ + 15);
            var yMin = Mathf.Max(min.y, sectionMinY);
            var yMax = Mathf.Min(max.y, sectionMinY + 15);

            if (xMin > xMax || yMin > yMax || zMin > zMax)
            {
                return 0;
            }

            var dx = xMax - xMin + 1;
            var dy = yMax - yMin + 1;
            var dz = zMax - zMin + 1;
            return (long)dx * dy * dz;
        }

        private static int GetBitsPerBlock(int paletteCount)
        {
            if (paletteCount <= 1)
            {
                return 0;
            }

            var bits = 0;
            var value = paletteCount - 1;
            while (value > 0)
            {
                bits++;
                value >>= 1;
            }

            return Mathf.Max(4, bits);
        }

        private static int GetBitsForSection(int paletteCount, long[] blockStates)
        {
            var bits = GetBitsPerBlock(paletteCount);
            if (blockStates != null && blockStates.Length > 0)
            {
                var bitsFromData = (blockStates.Length * 64) / 4096;
                if (bitsFromData > 0 && bitsFromData <= 32)
                {
                    bits = bitsFromData;
                }
            }

            return bits;
        }

        private static int GetBlockStateIndex(long[] data, int index, int bits)
        {
            if (bits == 0)
            {
                return 0;
            }

            if (index < 0 || data == null || data.Length == 0)
            {
                return 0;
            }

            var bitIndex = (long)index * bits;
            var startLong = (int)(bitIndex >> 6);
            var startOffset = (int)(bitIndex & 63);
            if (startLong < 0 || startLong >= data.Length)
            {
                return 0;
            }

            var mask = bits == 64 ? ulong.MaxValue : ((1UL << bits) - 1UL);
            var value = ((ulong)data[startLong] >> startOffset) & mask;

            var endOffset = startOffset + bits;
            if (endOffset > 64 && startLong + 1 < data.Length)
            {
                var next = (ulong)data[startLong + 1];
                value |= next << (64 - startOffset);
                value &= mask;
            }

            return (int)value;
        }

        private static bool TryGetSectionY(NbtCompound section, out int sectionY)
        {
            sectionY = 0;
            if (section["Y"] is NbtTag yTag)
            {
                if (yTag is NbtByte yByte)
                {
                    sectionY = unchecked((sbyte)yByte.Value);
                    return true;
                }

                if (yTag is NbtInt yInt)
                {
                    sectionY = yInt.Value;
                    return true;
                }
            }

            if (section["y"] is NbtTag yLower)
            {
                if (yLower is NbtByte yLowerByte)
                {
                    sectionY = unchecked((sbyte)yLowerByte.Value);
                    return true;
                }

                if (yLower is NbtInt yLowerInt)
                {
                    sectionY = yLowerInt.Value;
                    return true;
                }
            }

            return false;
        }

        private static bool TryGetSectionBlockData(NbtCompound section, out List<NbtCompound> palette, out long[] blockStates)
        {
            palette = null;
            blockStates = null;

            var paletteTag = section["Palette"] as NbtList ?? section["palette"] as NbtList;
            var blockStatesTag = section["BlockStates"] as NbtLongArray ?? section["block_states"] as NbtLongArray;

            if (paletteTag == null)
            {
                var blockStatesCompound = section["block_states"] as NbtCompound ?? section["BlockStates"] as NbtCompound;
                if (blockStatesCompound != null)
                {
                    paletteTag = blockStatesCompound["palette"] as NbtList ?? blockStatesCompound["Palette"] as NbtList;
                    blockStatesTag = blockStatesCompound["data"] as NbtLongArray ?? blockStatesCompound["Data"] as NbtLongArray;
                }
            }

            if (paletteTag == null || paletteTag.Count == 0)
            {
                return false;
            }

            var paletteList = new List<NbtCompound>(paletteTag.Count);
            foreach (var item in paletteTag)
            {
                if (item is NbtCompound compound)
                {
                    paletteList.Add(compound);
                }
            }

            if (paletteList.Count == 0)
            {
                return false;
            }

            palette = paletteList;
            blockStates = blockStatesTag?.Value;
            return true;
        }

        private static string GetBlockName(NbtCompound tag)
        {
            if (tag["Name"] is NbtString nameString)
            {
                return nameString.Value ?? string.Empty;
            }

            if (tag["name"] is NbtString nameLowerString)
            {
                return nameLowerString.Value ?? string.Empty;
            }

            return string.Empty;
        }

        private static void BuildPaletteCaches(List<NbtCompound> palette, out string[] names, out bool[] isAir)
        {
            names = new string[palette.Count];
            isAir = new bool[palette.Count];
            for (var i = 0; i < palette.Count; i++)
            {
                var name = GetBlockName(palette[i]);
                names[i] = name;
                isAir[i] = IsAirBlock(name);
            }
        }

        private static bool IsAirBlock(string name)
        {
            return name == "minecraft:air" || name == "minecraft:cave_air" || name == "minecraft:void_air";
        }

        private static int FloorDiv(int value, int divisor)
        {
            if (divisor == 0)
            {
                return 0;
            }

            if (value >= 0)
            {
                return value / divisor;
            }

            return -(((-value) + divisor - 1) / divisor);
        }

        private static NbtCompound LoadChunk(string regionFolder, int chunkX, int chunkZ)
        {
            var regionX = FloorDiv(chunkX, 32);
            var regionZ = FloorDiv(chunkZ, 32);
            var regionPath = Path.Combine(regionFolder, $"r.{regionX}.{regionZ}.mca");
            if (!File.Exists(regionPath))
            {
                return null;
            }

            try
            {
                using var stream = new FileStream(regionPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                var header = new byte[4096];
                if (stream.Read(header, 0, header.Length) != header.Length)
                {
                    return null;
                }

                var localX = chunkX - regionX * 32;
                var localZ = chunkZ - regionZ * 32;
                if (localX < 0 || localX >= 32 || localZ < 0 || localZ >= 32)
                {
                    return null;
                }

                var index = (localX + localZ * 32) * 4;
                var offset = (header[index] << 16) | (header[index + 1] << 8) | header[index + 2];
                if (offset == 0)
                {
                    return null;
                }

                stream.Seek(offset * 4096, SeekOrigin.Begin);
                var lengthBuffer = new byte[4];
                if (stream.Read(lengthBuffer, 0, 4) != 4)
                {
                    return null;
                }

                var length = (lengthBuffer[0] << 24) | (lengthBuffer[1] << 16) | (lengthBuffer[2] << 8) | lengthBuffer[3];
                if (length <= 1)
                {
                    return null;
                }

                var compressionType = stream.ReadByte();
                if (compressionType == -1)
                {
                    return null;
                }

                var compressed = new byte[length - 1];
                if (stream.Read(compressed, 0, compressed.Length) != compressed.Length)
                {
                    return null;
                }

                using var nbtStream = new MemoryStream(compressed);

                var nbtFile = new NbtFile();
                var compression = compressionType switch
                {
                    1 => NbtCompression.GZip,
                    2 => NbtCompression.ZLib,
                    3 => NbtCompression.None,
                    _ => NbtCompression.None
                };

                nbtFile.LoadFromStream(nbtStream, compression);
                return nbtFile.RootTag;
            }
            catch
            {
                return null;
            }
        }
    }
}
