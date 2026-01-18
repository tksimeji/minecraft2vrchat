using System;
using System.Collections.Generic;
using System.IO;
using fNbt;
using UnityEngine;

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
        }

        public static Mesh GenerateMesh(string worldFolder, string dimensionId, Vector3Int min, Vector3Int max, Options options, ref bool logChunkOnce, out string message)
        {
            message = string.Empty;
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

            var solids = new bool[volume];
            if (!FillSolidBlocks(worldFolder, dimensionId, min, max, solids, sizeX, sizeY, sizeZ, ref logChunkOnce, options.LogPaletteBounds))
            {
                message = "[Minecraft2VRChat] Failed to read blocks for meshing (region folder missing or read failure).";
                return null;
            }

            if (options.LogSliceStats)
            {
                LogSolidSliceStats(solids, sizeX, sizeY, sizeZ, min);
            }

            var mesh = options.UseGreedy
                ? BuildGreedyMesh(solids, min, sizeX, sizeY, sizeZ, options.ApplyCoordinateTransform)
                : BuildNaiveMesh(solids, min, sizeX, sizeY, sizeZ, options.ApplyCoordinateTransform);

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

                var isAir = new bool[palette.Count];
                for (var i = 0; i < palette.Count; i++)
                {
                    var name = GetBlockName(palette[i]);
                    isAir[i] = IsAirBlock(name);
                }

                var bits = GetBitsPerBlock(palette.Count);
                if (blockStates != null && blockStates.Length > 0)
                {
                    var bitsFromData = (blockStates.Length * 64) / 4096;
                    if (bitsFromData > 0 && bitsFromData <= 32)
                    {
                        bits = bitsFromData;
                    }
                }
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

        private static bool FillSolidBlocks(string worldFolder, string dimensionId, Vector3Int min, Vector3Int max, bool[] solids, int sizeX, int sizeY, int sizeZ, ref bool logChunkOnce, bool logPaletteBounds)
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
                    FillSolidsInChunk(regionFolder, cx, cz, min, max, solids, sizeX, sizeY, sizeZ, ref logChunkOnce, logPaletteBounds);
                }
            }

            return true;
        }

        private static void FillSolidsInChunk(string regionFolder, int chunkX, int chunkZ, Vector3Int min, Vector3Int max, bool[] solids, int sizeX, int sizeY, int sizeZ, ref bool logChunkOnce, bool logPaletteBounds)
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

                var isAir = new bool[palette.Count];
                for (var i = 0; i < palette.Count; i++)
                {
                    var name = GetBlockName(palette[i]);
                    isAir[i] = IsAirBlock(name);
                }

                var bits = GetBitsPerBlock(palette.Count);
                if (blockStates != null && blockStates.Length > 0)
                {
                    var bitsFromData = (blockStates.Length * 64) / 4096;
                    if (bitsFromData > 0 && bitsFromData <= 32)
                    {
                        bits = bitsFromData;
                    }
                }
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
                        for (var y = yMin; y <= yMax; y++)
                        {
                            for (var z = zMin; z <= zMax; z++)
                            {
                                for (var x = xMin; x <= xMax; x++)
                                {
                                    var outIndex = (x - min.x) + sizeX * ((y - min.y) + sizeY * (z - min.z));
                                    solids[outIndex] = true;
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
                            if (paletteIndex >= 0 && paletteIndex < isAir.Length && !isAir[paletteIndex])
                            {
                                var outIndex = (x - min.x) + sizeX * ((y - min.y) + sizeY * (z - min.z));
                                solids[outIndex] = true;
                            }
                            else if (paletteIndex < 0 || paletteIndex >= isAir.Length)
                            {
                                invalidCount++;
                            }
                        }
                    }
                }

                if (logPaletteBounds && invalidCount > 0)
                {
                    Debug.LogWarning($"[Minecraft2VRChat] Palette index out of range: {invalidCount}/{totalCount} " +
                                     $"(palette {isAir.Length}, bits {bits}, chunk {chunkX},{chunkZ}, sectionY {sectionY}).");
                }
            }
        }

        private static Mesh BuildGreedyMesh(bool[] solids, Vector3Int min, int sizeX, int sizeY, int sizeZ, bool applyCoordinateTransform)
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
                            var a = x[d] >= 0 && IsSolid(solids, dims, x[0], x[1], x[2]);
                            var b = x[d] < dims[d] - 1 && IsSolid(solids, dims, x[0] + q[0], x[1] + q[1], x[2] + q[2]);
                            if (a == b)
                            {
                                mask[n++] = 0;
                            }
                            else
                            {
                                mask[n++] = a ? 1 : -1;
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

                            if (c > 0)
                            {
                                // Clockwise winding for Unity front faces.
                                AddQuad(vertices, triangles, normals, uvs, v0, v2, v3, v1, AxisNormal(d, 1), uvWidth, uvHeight);
                            }
                            else
                            {
                                AddQuad(vertices, triangles, normals, uvs, v0, v1, v3, v2, AxisNormal(d, -1), uvWidth, uvHeight);
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

        private static Mesh BuildNaiveMesh(bool[] solids, Vector3Int min, int sizeX, int sizeY, int sizeZ, bool applyCoordinateTransform)
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
                        if (!IsSolid(solids, dims, x, y, z))
                        {
                            continue;
                        }

                        // -X
                        if (!IsSolid(solids, dims, x - 1, y, z))
                        {
                            AddQuad(vertices, triangles, normals, uvs,
                                new Vector3(x, y, z),
                                new Vector3(x, y + 1, z),
                                new Vector3(x, y + 1, z + 1),
                                new Vector3(x, y, z + 1),
                                new Vector3(-1, 0, 0), 1, 1);
                        }

                        // +X
                        if (!IsSolid(solids, dims, x + 1, y, z))
                        {
                            AddQuad(vertices, triangles, normals, uvs,
                                new Vector3(x + 1, y, z),
                                new Vector3(x + 1, y, z + 1),
                                new Vector3(x + 1, y + 1, z + 1),
                                new Vector3(x + 1, y + 1, z),
                                new Vector3(1, 0, 0), 1, 1);
                        }

                        // -Y
                        if (!IsSolid(solids, dims, x, y - 1, z))
                        {
                            AddQuad(vertices, triangles, normals, uvs,
                                new Vector3(x, y, z),
                                new Vector3(x, y, z + 1),
                                new Vector3(x + 1, y, z + 1),
                                new Vector3(x + 1, y, z),
                                new Vector3(0, -1, 0), 1, 1);
                        }

                        // +Y
                        if (!IsSolid(solids, dims, x, y + 1, z))
                        {
                            AddQuad(vertices, triangles, normals, uvs,
                                new Vector3(x, y + 1, z),
                                new Vector3(x + 1, y + 1, z),
                                new Vector3(x + 1, y + 1, z + 1),
                                new Vector3(x, y + 1, z + 1),
                                new Vector3(0, 1, 0), 1, 1);
                        }

                        // -Z
                        if (!IsSolid(solids, dims, x, y, z - 1))
                        {
                            AddQuad(vertices, triangles, normals, uvs,
                                new Vector3(x, y, z),
                                new Vector3(x + 1, y, z),
                                new Vector3(x + 1, y + 1, z),
                                new Vector3(x, y + 1, z),
                                new Vector3(0, 0, -1), 1, 1);
                        }

                        // +Z
                        if (!IsSolid(solids, dims, x, y, z + 1))
                        {
                            AddQuad(vertices, triangles, normals, uvs,
                                new Vector3(x, y, z + 1),
                                new Vector3(x, y + 1, z + 1),
                                new Vector3(x + 1, y + 1, z + 1),
                                new Vector3(x + 1, y, z + 1),
                                new Vector3(0, 0, 1), 1, 1);
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

            for (var i = 0; i < triangles.Count; i += 3)
            {
                var tmp = triangles[i + 1];
                triangles[i + 1] = triangles[i + 2];
                triangles[i + 2] = tmp;
            }
        }

        private static bool IsSolid(bool[] solids, int[] dims, int x, int y, int z)
        {
            if (x < 0 || y < 0 || z < 0 || x >= dims[0] || y >= dims[1] || z >= dims[2])
            {
                return false;
            }

            var index = x + dims[0] * (y + dims[1] * z);
            return solids[index];
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

        private static void AddQuad(List<Vector3> vertices, List<int> triangles, List<Vector3> normals, List<Vector2> uvs, Vector3 v0, Vector3 v1, Vector3 v2, Vector3 v3, Vector3 normal, float uvWidth, float uvHeight)
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
            uvs.Add(new Vector2(0, 0));
            uvs.Add(new Vector2(uvWidth, 0));
            uvs.Add(new Vector2(uvWidth, uvHeight));
            uvs.Add(new Vector2(0, uvHeight));
            triangles.Add(start);
            triangles.Add(start + 1);
            triangles.Add(start + 2);
            triangles.Add(start);
            triangles.Add(start + 2);
            triangles.Add(start + 3);
        }

        private static void LogSolidSliceStats(bool[] solids, int sizeX, int sizeY, int sizeZ, Vector3Int min)
        {
            const int maxSlices = 5;
            if (solids == null || solids.Length == 0)
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
                        if (solids[index])
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
