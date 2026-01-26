using System.IO;
using M2V.Editor.World;
using M2V.Editor.World.Block;
using UnityEngine;
using M2V.Editor.Meshing;

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

        public static Mesh GenerateMesh(string worldFolder, string dimensionId, Vector3Int min, Vector3Int max, string minecraftJarPath, Options options, ref bool logChunkOnce, out string message, out Texture2D atlasTexture)
        {
            var useCase = CreateUseCase();
            var request = new MeshingRequest
            {
                WorldFolder = worldFolder,
                DimensionId = dimensionId,
                Min = min,
                Max = max,
                MinecraftJarPath = minecraftJarPath,
                Options = options,
                LogChunkOnce = logChunkOnce
            };

            var result = useCase.Execute(request);
            logChunkOnce = result.LogChunkOnce;
            message = result.Message;
            atlasTexture = result.AtlasTexture;
            return result.Mesh;
        }

        public static long CountBlocksInRange(string worldFolder, string dimensionId, Vector3Int min, Vector3Int max, ref bool logChunkOnce)
        {
            var world = World.World.Of(new DirectoryInfo(worldFolder));
            if (world == null)
            {
                return 0;
            }

            if (!world.HasRegionData(dimensionId))
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
                    var chunk = world.GetChunkAt(dimensionId, cx, cz);
                    if (chunk == null)
                    {
                        continue;
                    }

                    if (logChunkOnce)
                    {
                        logChunkOnce = false;
                        Debug.Log($"[Minecraft2VRChat] Chunk NBT (sample):\n{chunk}");
                    }

                    var sections = chunk.Sections;
                    if (sections == null || sections.Count == 0)
                    {
                        continue;
                    }

                    var chunkMinXWorld = cx * 16;
                    var chunkMinZWorld = cz * 16;

                    foreach (var section in sections)
                    {
                        if (section == null)
                        {
                            continue;
                        }

                        count += section.CountSolidBlocksInRange(chunkMinXWorld, chunkMinZWorld, min, max, IsAirBlock);
                    }
                }
            }

            return count;
        }

        private static MeshingUseCase CreateUseCase()
        {
            return new MeshingUseCase(
                new ModelMeshBuilder(),
                new TextureAtlasBuilder(),
                assets => new ModelRepository(assets));
        }

        private static bool IsAirBlock(BlockState state)
        {
            var name = state?.Name;
            if (string.IsNullOrEmpty(name))
            {
                return true;
            }

            return name == "minecraft:air" || name == "minecraft:cave_air" || name == "minecraft:void_air";
        }

        private static int FloorDiv(int value, int divisor)
        {
            var result = value / divisor;
            if ((value ^ divisor) < 0 && value % divisor != 0)
            {
                result--;
            }

            return result;
        }
    }
}
