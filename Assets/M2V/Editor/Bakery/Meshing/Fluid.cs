#nullable enable
using System;
using System.Collections.Generic;
using M2V.Editor.Bakery;
using M2V.Editor.Minecraft;
using M2V.Editor.Minecraft.World;
using UnityEngine;

namespace M2V.Editor.Bakery.Meshing
{
    public static class Fluid
    {
        private static readonly ResourceLocation WaterStill = ResourceLocation.Of("block/water_still");
        private static readonly ResourceLocation WaterFlow = ResourceLocation.Of("block/water_flow");
        private static readonly ResourceLocation LavaStill = ResourceLocation.Of("block/lava_still");
        private static readonly ResourceLocation LavaFlow = ResourceLocation.Of("block/lava_flow");
        private static readonly ResourceLocation FallbackTexture = ResourceLocation.Of("block/dirt");

        public static void AddFluidTextures(HashSet<ResourceLocation> textures, IReadOnlyList<BlockState> blockStates)
        {
            var hasWater = false;
            var hasLava = false;
            for (var i = 1; i < blockStates.Count; i++)
            {
                var state = blockStates[i];
                if (state.Name.IsEmpty || state.IsAir)
                {
                    continue;
                }

                if (IsFluidName(state.Name, FluidType.Water))
                {
                    hasWater = true;
                }
                else if (IsFluidName(state.Name, FluidType.Lava))
                {
                    hasLava = true;
                }

                if (hasWater && hasLava)
                {
                    break;
                }
            }

            if (hasWater)
            {
                textures.Add(WaterStill);
                textures.Add(WaterFlow);
            }

            if (hasLava)
            {
                textures.Add(LavaStill);
                textures.Add(LavaFlow);
            }
        }

        public static bool TryEmit(
            BlockState state,
            int blockIndex,
            int blockX,
            int blockY,
            int blockZ,
            Volume volume,
            IReadOnlyList<Color32Byte> tintByBlock,
            List<Float3> vertices,
            List<Float3> normals,
            List<Float2> uvs,
            List<Color32Byte> colors,
            List<int> trianglesSolid,
            List<int> trianglesTranslucent,
            Dictionary<ResourceLocation, RectF> uvByTexture,
            Dictionary<ResourceLocation, TextureAlphaMode> alphaByTexture
        )
        {
            if (!TryGetFluidInfo(state, out var type, out var level, out var falling))
            {
                return false;
            }

            EmitFluidBlock(
                type,
                level,
                falling,
                blockIndex,
                blockX,
                blockY,
                blockZ,
                volume,
                tintByBlock,
                vertices,
                normals,
                uvs,
                colors,
                trianglesSolid,
                trianglesTranslucent,
                uvByTexture,
                alphaByTexture
            );
            return true;
        }

        private enum FluidType
        {
            Water,
            Lava
        }

        private static bool TryGetFluidInfo(
            BlockState state,
            out FluidType type,
            out int level,
            out bool falling
        )
        {
            level = 0;
            falling = false;
            type = FluidType.Water;

            if (state.Name.IsEmpty)
            {
                return false;
            }

            var isWater = IsFluidName(state.Name, FluidType.Water);
            var isLava = IsFluidName(state.Name, FluidType.Lava);
            if (!isWater && !isLava)
            {
                return false;
            }

            type = isLava ? FluidType.Lava : FluidType.Water;
            if (state.Properties.TryGetValue("level", out var levelTag))
            {
                level = TryGetInt(levelTag);
            }

            if (state.Properties.TryGetValue("falling", out var fallingTag))
            {
                falling = TryGetBool(fallingTag);
            }

            return true;
        }

        private static bool IsFluidName(ResourceLocation name, FluidType type)
        {
            if (!string.Equals(name.Namespace, ResourceLocation.MinecraftNamespace, StringComparison.Ordinal))
            {
                return false;
            }

            return type == FluidType.Water
                ? string.Equals(name.Path, "water", StringComparison.Ordinal)
                : string.Equals(name.Path, "lava", StringComparison.Ordinal);
        }

        private static int TryGetInt(fNbt.NbtTag tag)
        {
            return tag switch
            {
                fNbt.NbtByte b => b.Value,
                fNbt.NbtShort s => s.Value,
                fNbt.NbtInt i => i.Value,
                fNbt.NbtLong l => (int)l.Value,
                fNbt.NbtString str => int.TryParse(str.Value, out var value) ? value : 0,
                _ => 0
            };
        }

        private static bool TryGetBool(fNbt.NbtTag tag)
        {
            return tag switch
            {
                fNbt.NbtByte b => b.Value != 0,
                fNbt.NbtString str => string.Equals(str.Value, "true", StringComparison.OrdinalIgnoreCase),
                _ => false
            };
        }

        private static float GetFluidHeight(int level, bool falling)
        {
            if (level <= 0)
            {
                return 1f;
            }

            if (falling || level >= 8)
            {
                return 1f;
            }

            var clamped = Mathf.Clamp(level, 0, 7);
            return 1f - clamped / 8f;
        }

        private static float GetFluidHeightAt(Volume volume, int x, int y, int z, FluidType type)
        {
            if (x < 0 || y < 0 || z < 0 || x >= volume.SizeX || y >= volume.SizeY || z >= volume.SizeZ)
            {
                return 0f;
            }

            var id = volume.GetBlockId(x, y, z);
            if (id <= 0 || id >= volume.BlockStateCount)
            {
                return 0f;
            }

            var state = volume.BlockStates[id];
            if (!TryGetFluidInfo(state, out var otherType, out var level, out var falling) || otherType != type)
            {
                return 0f;
            }

            if (y + 1 < volume.SizeY)
            {
                var aboveId = volume.GetBlockId(x, y + 1, z);
                if (aboveId > 0 && aboveId < volume.BlockStateCount)
                {
                    var aboveState = volume.BlockStates[aboveId];
                    if (TryGetFluidInfo(aboveState, out var aboveType, out _, out _) && aboveType == type)
                    {
                        return 1f;
                    }
                }
            }

            return GetFluidHeight(level, falling);
        }

        private static float GetCornerHeight(Volume volume, int x, int y, int z, FluidType type)
        {
            var h0 = GetFluidHeightAt(volume, x, y, z, type);
            var h1 = GetFluidHeightAt(volume, x - 1, y, z, type);
            var h2 = GetFluidHeightAt(volume, x, y, z - 1, type);
            var h3 = GetFluidHeightAt(volume, x - 1, y, z - 1, type);

            if (h0 >= 1f || h1 >= 1f || h2 >= 1f || h3 >= 1f)
            {
                return 1f;
            }

            var sum = 0f;
            var count = 0;
            if (h0 > 0f) { sum += h0; count++; }
            if (h1 > 0f) { sum += h1; count++; }
            if (h2 > 0f) { sum += h2; count++; }
            if (h3 > 0f) { sum += h3; count++; }

            return count > 0 ? sum / count : 0f;
        }

        private static RectF GetFluidUvRect(
            ResourceLocation texturePath,
            Dictionary<ResourceLocation, RectF> uvByTexture,
            float height
        )
        {
            if (texturePath.IsEmpty || !uvByTexture.TryGetValue(texturePath, out var rect))
            {
                if (uvByTexture.TryGetValue(FallbackTexture, out var fallback))
                {
                    rect = fallback;
                }
                else
                {
                    return new RectF(0f, 0f, 1f, 1f);
                }
            }

            var h = Mathf.Clamp01(height);
            if (h >= 0.999f)
            {
                return rect;
            }

            var y = rect.Y + rect.Height * (1f - h);
            var heightScaled = rect.Height * h;
            return new RectF(rect.X, y, rect.Width, heightScaled);
        }

        private static Float2[] RotateUv(RectF rect, float angleRadians)
        {
            var cx = rect.X + rect.Width * 0.5f;
            var cy = rect.Y + rect.Height * 0.5f;

            var corners = new[]
            {
                new Float2(rect.X, rect.Y),
                new Float2(rect.X + rect.Width, rect.Y),
                new Float2(rect.X + rect.Width, rect.Y + rect.Height),
                new Float2(rect.X, rect.Y + rect.Height)
            };

            var cos = Mathf.Cos(angleRadians);
            var sin = Mathf.Sin(angleRadians);

            var minX = rect.X;
            var maxX = rect.X + rect.Width;
            var minY = rect.Y;
            var maxY = rect.Y + rect.Height;

            for (var i = 0; i < corners.Length; i++)
            {
                var x = corners[i].X - cx;
                var y = corners[i].Y - cy;
                var rx = x * cos - y * sin;
                var ry = x * sin + y * cos;
                var u = rx + cx;
                var v = ry + cy;
                if (u < minX) u = minX;
                if (u > maxX) u = maxX;
                if (v < minY) v = minY;
                if (v > maxY) v = maxY;
                corners[i] = new Float2(u, v);
            }

            return corners;
        }

        private static float GetFlowAngle(Volume volume, int x, int y, int z, FluidType type)
        {
            var hN = GetFluidHeightAt(volume, x, y, z - 1, type);
            var hS = GetFluidHeightAt(volume, x, y, z + 1, type);
            var hW = GetFluidHeightAt(volume, x - 1, y, z, type);
            var hE = GetFluidHeightAt(volume, x + 1, y, z, type);

            var dx = hW - hE;
            var dz = hN - hS;

            if (Mathf.Abs(dx) < 0.0001f && Mathf.Abs(dz) < 0.0001f)
            {
                return 0f;
            }

            return Mathf.Atan2(dz, dx);
        }

        private static void EmitFluidBlock(
            FluidType type,
            int level,
            bool falling,
            int blockIndex,
            int blockX,
            int blockY,
            int blockZ,
            Volume volume,
            IReadOnlyList<Color32Byte> tintByBlock,
            List<Float3> vertices,
            List<Float3> normals,
            List<Float2> uvs,
            List<Color32Byte> colors,
            List<int> trianglesSolid,
            List<int> trianglesTranslucent,
            Dictionary<ResourceLocation, RectF> uvByTexture,
            Dictionary<ResourceLocation, TextureAlphaMode> alphaByTexture
        )
        {
            var baseHeight = GetFluidHeight(level, falling);
            var h00 = GetCornerHeight(volume, blockX, blockY, blockZ, type);
            var h10 = GetCornerHeight(volume, blockX + 1, blockY, blockZ, type);
            var h11 = GetCornerHeight(volume, blockX + 1, blockY, blockZ + 1, type);
            var h01 = GetCornerHeight(volume, blockX, blockY, blockZ + 1, type);
            h00 = Mathf.Max(h00, baseHeight);
            h10 = Mathf.Max(h10, baseHeight);
            h11 = Mathf.Max(h11, baseHeight);
            h01 = Mathf.Max(h01, baseHeight);

            var tint = blockIndex >= 0 && blockIndex < tintByBlock.Count
                ? tintByBlock[blockIndex]
                : new Color32Byte(255, 255, 255, 255);
            var topTexture = (level > 0 || falling)
                ? (type == FluidType.Water ? WaterFlow : LavaFlow)
                : (type == FluidType.Water ? WaterStill : LavaStill);

            var aboveId = blockY + 1 < volume.SizeY ? volume.GetBlockId(blockX, blockY + 1, blockZ) : 0;
            var hasCoverAbove = false;
            if (aboveId > 0 && aboveId < volume.BlockStateCount)
            {
                var aboveState = volume.BlockStates[aboveId];
                if (TryGetFluidInfo(aboveState, out var aboveType, out _, out _))
                {
                    hasCoverAbove = aboveType == type;
                }
                else
                {
                    hasCoverAbove = !aboveState.IsAir;
                }
            }

            if (!hasCoverAbove)
            {
                var topUv = GetFluidUvRect(topTexture, uvByTexture, 1f);
                var topTriangles = ResolveAlphaMode(topTexture, alphaByTexture) == TextureAlphaMode.Translucent
                    ? trianglesTranslucent
                    : trianglesSolid;
                var flowAngle = GetFlowAngle(volume, blockX, blockY, blockZ, type);
                if (Mathf.Abs(flowAngle) > 0.001f)
                {
                    var rotated = RotateUv(topUv, flowAngle);
                    AddQuadWithUvs(
                        vertices,
                        topTriangles,
                        normals,
                        uvs,
                        new Vector3(blockX, blockY + h00, blockZ),
                        new Vector3(blockX + 1, blockY + h10, blockZ),
                        new Vector3(blockX + 1, blockY + h11, blockZ + 1),
                        new Vector3(blockX, blockY + h01, blockZ + 1),
                        Vector3.up,
                        rotated,
                        tint,
                        colors
                    );
                }
                else
                {
                    AddQuad(
                        vertices,
                        topTriangles,
                        normals,
                        uvs,
                        new Vector3(blockX, blockY + h00, blockZ),
                        new Vector3(blockX + 1, blockY + h10, blockZ),
                        new Vector3(blockX + 1, blockY + h11, blockZ + 1),
                        new Vector3(blockX, blockY + h01, blockZ + 1),
                        Vector3.up,
                        topUv,
                        tint,
                        colors
                    );
                }
            }

            EmitFluidSide(Direction.North, blockX, blockY, blockZ, h00, h10, type, tint, volume, vertices, normals,
                uvs, colors, trianglesSolid, trianglesTranslucent, uvByTexture, alphaByTexture);
            EmitFluidSide(Direction.South, blockX, blockY, blockZ, h01, h11, type, tint, volume, vertices, normals,
                uvs, colors, trianglesSolid, trianglesTranslucent, uvByTexture, alphaByTexture);
            EmitFluidSide(Direction.West, blockX, blockY, blockZ, h00, h01, type, tint, volume, vertices, normals,
                uvs, colors, trianglesSolid, trianglesTranslucent, uvByTexture, alphaByTexture);
            EmitFluidSide(Direction.East, blockX, blockY, blockZ, h10, h11, type, tint, volume, vertices, normals,
                uvs, colors, trianglesSolid, trianglesTranslucent, uvByTexture, alphaByTexture);

            var belowId = blockY - 1 >= 0 ? volume.GetBlockId(blockX, blockY - 1, blockZ) : 0;
            var hasCoverBelow = false;
            if (belowId > 0 && belowId < volume.BlockStateCount)
            {
                var belowState = volume.BlockStates[belowId];
                if (TryGetFluidInfo(belowState, out var belowType, out _, out _))
                {
                    hasCoverBelow = belowType == type;
                }
                else
                {
                    hasCoverBelow = !belowState.IsAir;
                }
            }

            if (!hasCoverBelow)
            {
                var bottomTexture = type == FluidType.Water ? WaterStill : LavaStill;
                var bottomUv = GetFluidUvRect(bottomTexture, uvByTexture, 1f);
                var bottomTriangles = ResolveAlphaMode(bottomTexture, alphaByTexture) == TextureAlphaMode.Translucent
                    ? trianglesTranslucent
                    : trianglesSolid;
                AddQuad(
                    vertices,
                    bottomTriangles,
                    normals,
                    uvs,
                    new Vector3(blockX, blockY, blockZ + 1),
                    new Vector3(blockX + 1, blockY, blockZ + 1),
                    new Vector3(blockX + 1, blockY, blockZ),
                    new Vector3(blockX, blockY, blockZ),
                    Vector3.down,
                    bottomUv,
                    tint,
                    colors
                );
            }
        }

        private static void EmitFluidSide(
            Direction direction,
            int blockX,
            int blockY,
            int blockZ,
            float heightA,
            float heightB,
            FluidType type,
            Color32Byte tint,
            Volume volume,
            List<Float3> vertices,
            List<Float3> normals,
            List<Float2> uvs,
            List<Color32Byte> colors,
            List<int> trianglesSolid,
            List<int> trianglesTranslucent,
            Dictionary<ResourceLocation, RectF> uvByTexture,
            Dictionary<ResourceLocation, TextureAlphaMode> alphaByTexture
        )
        {
            if (heightA <= 0f && heightB <= 0f)
            {
                return;
            }

            var neighborX = blockX;
            var neighborZ = blockZ;
            switch (direction)
            {
                case Direction.North: neighborZ -= 1; break;
                case Direction.South: neighborZ += 1; break;
                case Direction.West: neighborX -= 1; break;
                case Direction.East: neighborX += 1; break;
                default: return;
            }

            var neighborId = (neighborX < 0 || neighborZ < 0 || neighborX >= volume.SizeX || neighborZ >= volume.SizeZ)
                ? 0
                : volume.GetBlockId(neighborX, blockY, neighborZ);
            if (neighborId > 0 && neighborId < volume.BlockStateCount)
            {
                var neighborState = volume.BlockStates[neighborId];
                if (TryGetFluidInfo(neighborState, out _, out _, out _))
                {
                    return;
                }

                if (!neighborState.IsAir)
                {
                    return;
                }
            }

            var sideTexture = type == FluidType.Water ? WaterFlow : LavaFlow;
            var height = Mathf.Max(heightA, heightB);
            var uvRect = GetFluidUvRect(sideTexture, uvByTexture, height);
            var targetTriangles = ResolveAlphaMode(sideTexture, alphaByTexture) == TextureAlphaMode.Translucent
                ? trianglesTranslucent
                : trianglesSolid;

            Vector3 v0;
            Vector3 v1;
            Vector3 v2;
            Vector3 v3;

            switch (direction)
            {
                case Direction.North:
                    v0 = new Vector3(blockX, blockY, blockZ);
                    v1 = new Vector3(blockX + 1, blockY, blockZ);
                    v2 = new Vector3(blockX + 1, blockY + heightB, blockZ);
                    v3 = new Vector3(blockX, blockY + heightA, blockZ);
                    break;
                case Direction.South:
                    v0 = new Vector3(blockX + 1, blockY, blockZ + 1);
                    v1 = new Vector3(blockX, blockY, blockZ + 1);
                    v2 = new Vector3(blockX, blockY + heightA, blockZ + 1);
                    v3 = new Vector3(blockX + 1, blockY + heightB, blockZ + 1);
                    break;
                case Direction.West:
                    v0 = new Vector3(blockX, blockY, blockZ);
                    v1 = new Vector3(blockX, blockY, blockZ + 1);
                    v2 = new Vector3(blockX, blockY + heightB, blockZ + 1);
                    v3 = new Vector3(blockX, blockY + heightA, blockZ);
                    break;
                case Direction.East:
                    v0 = new Vector3(blockX + 1, blockY, blockZ + 1);
                    v1 = new Vector3(blockX + 1, blockY, blockZ);
                    v2 = new Vector3(blockX + 1, blockY + heightA, blockZ);
                    v3 = new Vector3(blockX + 1, blockY + heightB, blockZ + 1);
                    break;
                default:
                    return;
            }

            AddQuad(
                vertices,
                targetTriangles,
                normals,
                uvs,
                v0,
                v1,
                v2,
                v3,
                M2VMathHelper.DirectionToNormal(direction),
                uvRect,
                tint,
                colors
            );
        }

        private static void AddQuad(
            List<Float3> vertices,
            List<int> triangles,
            List<Float3> normals,
            List<Float2> uvs,
            Vector3 v0, Vector3 v1, Vector3 v2, Vector3 v3,
            Vector3 normal,
            RectF uvRect,
            Color32Byte tint,
            List<Color32Byte> colors
        )
        {
            var index = vertices.Count;
            vertices.Add(new Float3(v0.x, v0.y, v0.z));
            vertices.Add(new Float3(v1.x, v1.y, v1.z));
            vertices.Add(new Float3(v2.x, v2.y, v2.z));
            vertices.Add(new Float3(v3.x, v3.y, v3.z));
            var packedNormal = new Float3(normal.x, normal.y, normal.z);
            normals.Add(packedNormal);
            normals.Add(packedNormal);
            normals.Add(packedNormal);
            normals.Add(packedNormal);

            colors.Add(tint);
            colors.Add(tint);
            colors.Add(tint);
            colors.Add(tint);

            uvs.Add(new Float2(uvRect.X, uvRect.Y));
            uvs.Add(new Float2(uvRect.X + uvRect.Width, uvRect.Y));
            uvs.Add(new Float2(uvRect.X + uvRect.Width, uvRect.Y + uvRect.Height));
            uvs.Add(new Float2(uvRect.X, uvRect.Y + uvRect.Height));

            triangles.Add(index + 0);
            triangles.Add(index + 1);
            triangles.Add(index + 2);
            triangles.Add(index + 3);
            triangles.Add(index + 0);
            triangles.Add(index + 2);
        }

        private static void AddQuadWithUvs(
            List<Float3> vertices,
            List<int> triangles,
            List<Float3> normals,
            List<Float2> uvs,
            Vector3 v0, Vector3 v1, Vector3 v2, Vector3 v3,
            Vector3 normal,
            Float2[] uv,
            Color32Byte tint,
            List<Color32Byte> colors
        )
        {
            var index = vertices.Count;
            vertices.Add(new Float3(v0.x, v0.y, v0.z));
            vertices.Add(new Float3(v1.x, v1.y, v1.z));
            vertices.Add(new Float3(v2.x, v2.y, v2.z));
            vertices.Add(new Float3(v3.x, v3.y, v3.z));
            var packedNormal = new Float3(normal.x, normal.y, normal.z);
            normals.Add(packedNormal);
            normals.Add(packedNormal);
            normals.Add(packedNormal);
            normals.Add(packedNormal);

            colors.Add(tint);
            colors.Add(tint);
            colors.Add(tint);
            colors.Add(tint);

            if (uv.Length >= 4)
            {
                uvs.Add(uv[0]);
                uvs.Add(uv[1]);
                uvs.Add(uv[2]);
                uvs.Add(uv[3]);
            }
            else
            {
                uvs.Add(new Float2(0f, 0f));
                uvs.Add(new Float2(1f, 0f));
                uvs.Add(new Float2(1f, 1f));
                uvs.Add(new Float2(0f, 1f));
            }

            triangles.Add(index + 0);
            triangles.Add(index + 1);
            triangles.Add(index + 2);
            triangles.Add(index + 3);
            triangles.Add(index + 0);
            triangles.Add(index + 2);
        }

        private static TextureAlphaMode ResolveAlphaMode(
            ResourceLocation texturePath,
            Dictionary<ResourceLocation, TextureAlphaMode> alphaByTexture
        )
        {
            if (!texturePath.IsEmpty && alphaByTexture.TryGetValue(texturePath, out var mode))
            {
                return mode;
            }

            return TextureAlphaMode.Opaque;
        }
    }
}