#nullable enable

using System;
using System.Collections.Generic;
using M2V.Editor.Bakery.Tinting;
using M2V.Editor.Minecraft;
using M2V.Editor.Minecraft.Model;
using UnityEngine;

namespace M2V.Editor.Bakery.Meshing
{
    public sealed class MeshBuilder
    {
        private static readonly ResourceLocation FallbackTexture = ResourceLocation.Of("block/dirt");
        private const float ThinEpsilon = 0.0001f;

        public sealed record BuildResult(Mesh? Mesh, Texture2D AtlasTexture, AtlasAnimation? AtlasAnimation);

        public static BuildResult Build(
            Volume volume,
            Vector3Int min,
            BlockModelResolver blockModelResolver,
            BiomeIndex biomeIndex,
            AssetReader assetReader,
            bool applyCoordinateTransform = true
        )
        {
            var blockModelIndex = blockModelResolver.BuildBlockVariants(volume.BlockStates);
            var fullCubeById = blockModelResolver.BuildFullCubeFlags(blockModelIndex);
            var texturePaths = blockModelResolver.CollectTexturePaths(blockModelIndex);
            texturePaths.Add(FallbackTexture);
            Fluid.AddFluidTextures(texturePaths, volume.BlockStates);

            var tintResolver = new BlockTintResolver(assetReader, biomeIndex);
            var tintByBlock = tintResolver.BuildTintByBlock(volume);

            var atlasTexture = AtlasBuilder.BuildAtlas(
                assetReader,
                texturePaths,
                out var uvByTexture,
                out var alphaByTexture,
                out var atlasAnimation
            );

            return new BuildResult(
                BuildMesh(
                    volume,
                    min,
                    blockModelIndex,
                    blockModelResolver,
                    fullCubeById,
                    tintByBlock,
                    uvByTexture,
                    alphaByTexture,
                    applyCoordinateTransform
                ),
                atlasTexture,
                atlasAnimation
            );
        }

        private static Mesh? BuildMesh(
            Volume volume,
            Vector3Int min,
            BlockModelIndex blockModelIndex,
            BlockModelResolver blockModelResolver,
            List<bool> fullCubeById,
            IReadOnlyList<Color32Byte> tintByBlock,
            Dictionary<ResourceLocation, RectF> uvByTexture,
            Dictionary<ResourceLocation, TextureAlphaMode> alphaByTexture,
            bool applyCoordinateTransform
        )
        {
            var buffer = new MeshBuffer();

            var vertices = buffer.Vertices;
            var normals = buffer.Normals;
            var uvs = buffer.UVs;
            var colors = buffer.Colors;
            var trianglesSolid = buffer.TrianglesSolid;
            var trianglesDoubleSidedCutout = buffer.TrianglesDoubleSidedCutout;
            var trianglesTranslucent = buffer.TrianglesTranslucent;
            var quadDedup = new HashSet<QuadKey>();

            var sizeX = volume.SizeX;
            var sizeY = volume.SizeY;

            for (var z = 0; z < volume.SizeZ; z++)
            {
                for (var y = 0; y < volume.SizeY; y++)
                {
                    for (var x = 0; x < volume.SizeX; x++)
                    {
                        var id = volume.GetBlockId(x, y, z);
                        if (id <= 0 || id >= blockModelIndex.Count)
                        {
                            continue;
                        }

                        var blockIndex = x + sizeX * (y + sizeY * z);
                        var state = volume.BlockStates[id];
                        if (Fluid.TryEmit(
                                state,
                                blockIndex,
                                x, y, z,
                                volume,
                                tintByBlock,
                                vertices,
                                normals,
                                uvs,
                                colors,
                                trianglesSolid,
                                trianglesTranslucent,
                                uvByTexture,
                                alphaByTexture,
                                applyCoordinateTransform
                            ))
                        {
                            continue;
                        }

                        var variants = blockModelIndex[id];
                        if (variants.Count == 0) continue;

                        foreach (var variant in variants)
                        {
                            EmitModel(
                                variant,
                                blockModelResolver,
                                blockIndex,
                                x, y, z,
                                volume,
                                fullCubeById,
                                tintByBlock,
                                vertices, normals, uvs, colors,
                                trianglesSolid,
                                trianglesDoubleSidedCutout,
                                trianglesTranslucent,
                                uvByTexture,
                                alphaByTexture,
                                quadDedup,
                                applyCoordinateTransform
                            );
                        }
                    }
                }
            }

            if (vertices.Count == 0) return null;

            ApplyCoordinateTransform(
                vertices, normals, trianglesSolid, min, applyCoordinateTransform
            );
            ApplyCoordinateTransform(
                vertices, normals, trianglesDoubleSidedCutout, min, applyCoordinateTransform
            );
            ApplyCoordinateTransform(
                vertices, normals, trianglesTranslucent, min, applyCoordinateTransform
            );
            return CreateMesh(buffer);
        }

        private static void EmitModel(
            Variant variant,
            BlockModelResolver blockModelResolver,
            int blockIndex,
            int blockX, int blockY, int blockZ,
            Volume volume,
            List<bool> fullCubeById,
            IReadOnlyList<Color32Byte> tintByBlock,
            List<Float3> vertices, List<Float3> normals, List<Float2> uvs, List<Color32Byte> colors,
            List<int> trianglesSolid,
            List<int> trianglesDoubleSidedCutout,
            List<int> trianglesTranslucent,
            Dictionary<ResourceLocation, RectF> uvByTexture,
            Dictionary<ResourceLocation, TextureAlphaMode> alphaByTexture,
            HashSet<QuadKey> quadDedup,
            bool applyCoordinateTransform
        )
        {
            var model = blockModelResolver.ResolveBlockModel(variant.Model);
            if (model?.Elements == null) return;

            var blockOffset = new Vector3(blockX, blockY, blockZ);
            var modelRotation = Quaternion.Euler(-variant.RotationX, -variant.RotationY, -variant.RotationZ);
            var modelOrigin = new Vector3(0.5f, 0.5f, 0.5f);

            foreach (var element in model.Elements)
            {
                var corners = element.GetCornerPositions();
                if (element.Rotation != null)
                {
                    var axisVector = element.Rotation.ToAxisVector();
                    var rotationOrigin = element.Rotation.ToToOriginVector();
                    var rot = Quaternion.AngleAxis(element.Rotation.Angle, axisVector);
                    for (var i = 0; i < corners.Length; i++)
                    {
                        corners[i] = M2VMathHelper.RotateAround(corners[i], rotationOrigin, rot);
                    }

                    if (element.Rotation.Rescale ?? false)
                    {
                        var radians = Mathf.Abs(element.Rotation.Angle) * Mathf.Deg2Rad;
                        var scaleFactor = Mathf.Cos(radians) > ThinEpsilon ? 1f / Mathf.Cos(radians) : 1f;
                        Vector3 scale;
                        if (axisVector == Vector3.right)
                        {
                            scale = new Vector3(1f, scaleFactor, scaleFactor);
                        }
                        else if (axisVector == Vector3.up)
                        {
                            scale = new Vector3(scaleFactor, 1f, scaleFactor);
                        }
                        else
                        {
                            scale = new Vector3(scaleFactor, scaleFactor, 1f);
                        }

                        for (var i = 0; i < corners.Length; i++)
                        {
                            var offset = corners[i] - rotationOrigin;
                            corners[i] = rotationOrigin + Vector3.Scale(offset, scale);
                        }
                    }
                }

                for (var i = 0; i < corners.Length; i++)
                {
                    corners[i] = M2VMathHelper.RotateAround(corners[i], modelOrigin, modelRotation);
                }

                if (element.Faces == null) continue;

                foreach (var (direction, face) in element.Faces)
                {
                    if (face == null) continue;

                    if (face.CullFace.HasValue && model.IsFullCube())
                    {
                        var cullDirection = M2VMathHelper.RotateDirection(
                            face.CullFace.Value,
                            variant.RotationX, variant.RotationY, variant.RotationZ
                        );
                        if (IsNeighborFullCube(volume, fullCubeById, blockX, blockY, blockZ, cullDirection)) continue;
                    }

                    var quad = BlockElement.GetFaceQuadCorners(direction, corners);
                    var normal = M2VMathHelper.DirectionToNormal(
                        M2VMathHelper.RotateDirection(
                            direction,
                            variant.RotationX, variant.RotationY, variant.RotationZ
                        )
                    );
                    var texturePath = model.ResolveTexture(face.Texture ?? string.Empty);
                    if (texturePath.IsEmpty)
                    {
                        texturePath = FallbackTexture;
                    }

                    var uvRect = M2VMathHelper.ResolveUvRect(
                        texturePath, face, variant, uvByTexture, element, direction
                    );
                    var alphaMode = ResolveAlphaMode(texturePath, alphaByTexture);
                    var tint = ResolveTint(face, blockIndex, tintByBlock);
                    var isThinPlane = IsThinPlaneFace(element, direction);
                    if (isThinPlane && ShouldSkipOppositeThinFace(element, direction))
                    {
                        continue;
                    }

                    var targetTriangles = alphaMode == TextureAlphaMode.Translucent
                        ? trianglesTranslucent
                        : (isThinPlane ? trianglesDoubleSidedCutout : trianglesSolid);
                    if (!TryAddQuad(quadDedup, quad[0] + blockOffset, quad[1] + blockOffset,
                            quad[2] + blockOffset, quad[3] + blockOffset, uvRect))
                    {
                        continue;
                    }

                    AddQuad(
                        vertices,
                        targetTriangles,
                        normals,
                        uvs,
                        quad[0] + blockOffset,
                        quad[1] + blockOffset,
                        quad[2] + blockOffset,
                        quad[3] + blockOffset,
                        normal,
                        uvRect,
                        tint,
                        colors,
                        NeedsWindingFlip(direction),
                        applyCoordinateTransform
                    );
                }
            }
        }

        private static bool IsThinPlaneFace(BlockElement element, Direction direction)
        {
            var thinX = Mathf.Abs(element.From.x - element.To.x) < ThinEpsilon;
            var thinY = Mathf.Abs(element.From.y - element.To.y) < ThinEpsilon;
            var thinZ = Mathf.Abs(element.From.z - element.To.z) < ThinEpsilon;

            return direction switch
            {
                Direction.West or Direction.East => thinX,
                Direction.Down or Direction.Up => thinY,
                Direction.North or Direction.South => thinZ,
                _ => false
            };
        }

        private static bool ShouldSkipOppositeThinFace(BlockElement element, Direction direction)
        {
            if (element.Faces == null) return false;

            return direction switch
            {
                Direction.East => element.Faces.ContainsKey(Direction.West),
                Direction.West => false,
                Direction.Up => element.Faces.ContainsKey(Direction.Down),
                Direction.Down => false,
                Direction.South => element.Faces.ContainsKey(Direction.North),
                _ => false
            };
        }

        private static bool NeedsWindingFlip(Direction direction)
        {
            return direction is Direction.West or Direction.East or Direction.Down;
        }

        private static void ApplyCoordinateTransform(
            List<Float3> vertices,
            List<Float3> normals,
            List<int> triangles,
            Vector3Int min,
            bool applyCoordinateTransform
        )
        {
            M2VMathHelper.ApplyCoordinateTransform(vertices, normals, triangles, min, applyCoordinateTransform);
        }

        private static TextureAlphaMode ResolveAlphaMode(
            ResourceLocation texturePath,
            Dictionary<ResourceLocation, TextureAlphaMode> alphaByTexture
        )
        {
            if (!texturePath.IsEmpty && alphaByTexture.TryGetValue(texturePath, out var mode))
            {
                if (mode == TextureAlphaMode.Translucent && !IsForcedTranslucent(texturePath))
                {
                    return TextureAlphaMode.Cutout;
                }

                return mode;
            }

            return TextureAlphaMode.Opaque;
        }

        private static bool IsForcedTranslucent(ResourceLocation texturePath)
        {
            if (texturePath.IsEmpty)
            {
                return false;
            }

            var name = string.Equals(texturePath.Namespace, ResourceLocation.MinecraftNamespace,
                StringComparison.Ordinal)
                ? texturePath.Path
                : texturePath.ToString();

            return name.Contains("water", StringComparison.Ordinal)
                   || name.Contains("lava", StringComparison.Ordinal);
        }

        private static bool IsNeighborFullCube(
            Volume volume,
            List<bool> fullCubeById,
            int x, int y, int z,
            Direction direction
        )
        {
            var nx = x;
            var ny = y;
            var nz = z;
            switch (direction)
            {
                case Direction.North: nz -= 1; break;
                case Direction.South: nz += 1; break;
                case Direction.West: nx -= 1; break;
                case Direction.East: nx += 1; break;
                case Direction.Down: ny -= 1; break;
                case Direction.Up: ny += 1; break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(direction), direction, null);
            }

            var id = volume.GetBlockId(nx, ny, nz);
            return id > 0 && id < fullCubeById.Count && fullCubeById[id];
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
            List<Color32Byte> colors,
            bool flipWinding,
            bool applyCoordinateTransform
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

            if (applyCoordinateTransform)
            {
                flipWinding = !flipWinding;
            }

            if (!flipWinding)
            {
                triangles.Add(index + 0);
                triangles.Add(index + 1);
                triangles.Add(index + 2);
                triangles.Add(index + 3);
                triangles.Add(index + 0);
                triangles.Add(index + 2);
            }
            else
            {
                triangles.Add(index + 0);
                triangles.Add(index + 2);
                triangles.Add(index + 1);
                triangles.Add(index + 3);
                triangles.Add(index + 2);
                triangles.Add(index + 0);
            }
        }

        private static bool TryAddQuad(
            HashSet<QuadKey> dedup,
            Vector3 v0,
            Vector3 v1,
            Vector3 v2,
            Vector3 v3,
            RectF uvRect
        )
        {
            var key = QuadKey.From(v0, v1, v2, v3, uvRect);
            return dedup.Add(key);
        }

        private readonly struct QuadKey : IEquatable<QuadKey>
        {
            private readonly int _h0;
            private readonly int _h1;
            private readonly int _h2;
            private readonly int _h3;

            private QuadKey(int h0, int h1, int h2, int h3)
            {
                _h0 = h0;
                _h1 = h1;
                _h2 = h2;
                _h3 = h3;
            }

            public static QuadKey From(Vector3 v0, Vector3 v1, Vector3 v2, Vector3 v3, RectF uvRect)
            {
                var uv0 = new Vector2(uvRect.X, uvRect.Y);
                var uv1 = new Vector2(uvRect.X + uvRect.Width, uvRect.Y);
                var uv2 = new Vector2(uvRect.X + uvRect.Width, uvRect.Y + uvRect.Height);
                var uv3 = new Vector2(uvRect.X, uvRect.Y + uvRect.Height);

                var a = new QuadVertex(v0, uv0);
                var b = new QuadVertex(v1, uv1);
                var c = new QuadVertex(v2, uv2);
                var d = new QuadVertex(v3, uv3);

                var list = new[] { a, b, c, d };
                Array.Sort(list, QuadVertexComparer.Instance);

                var h0 = list[0].GetHash();
                var h1 = list[1].GetHash();
                var h2 = list[2].GetHash();
                var h3 = list[3].GetHash();

                return new QuadKey(h0, h1, h2, h3);
            }

            public bool Equals(QuadKey other)
            {
                return _h0 == other._h0 && _h1 == other._h1 && _h2 == other._h2 && _h3 == other._h3;
            }

            public override bool Equals(object? obj)
            {
                return obj is QuadKey other && Equals(other);
            }

            public override int GetHashCode()
            {
                return HashCode.Combine(_h0, _h1, _h2, _h3);
            }
        }

        private readonly struct QuadVertex
        {
            private readonly int _x;
            private readonly int _y;
            private readonly int _z;
            private readonly int _u;
            private readonly int _v;

            public QuadVertex(Vector3 position, Vector2 uv)
            {
                _x = Quantize(position.x);
                _y = Quantize(position.y);
                _z = Quantize(position.z);
                _u = Quantize(uv.x);
                _v = Quantize(uv.y);
            }

            public int GetHash()
            {
                return HashCode.Combine(_x, _y, _z, _u, _v);
            }
        }

        private sealed class QuadVertexComparer : IComparer<QuadVertex>
        {
            public static readonly QuadVertexComparer Instance = new();

            public int Compare(QuadVertex left, QuadVertex right)
            {
                var lh = left.GetHash();
                var rh = right.GetHash();
                return lh.CompareTo(rh);
            }
        }

        private static int Quantize(float value)
        {
            return (int)Mathf.Round(value * 10000f);
        }

        private static Color32Byte ResolveTint(
            BlockElementFace? blockElementFace,
            int blockIndex,
            IReadOnlyList<Color32Byte> tintByBlock
        )
        {
            if (blockElementFace?.TintIndex == null || blockIndex < 0 || blockIndex >= tintByBlock.Count)
            {
                return new Color32Byte(255, 255, 255, 255);
            }

            return tintByBlock[blockIndex];
        }

        private static Mesh? CreateMesh(MeshBuffer buffer)
        {
            if (buffer.Vertices.Count == 0) return null;

            var mesh = new Mesh
            {
                indexFormat = buffer.Vertices.Count > 65535
                    ? UnityEngine.Rendering.IndexFormat.UInt32
                    : UnityEngine.Rendering.IndexFormat.UInt16
            };

            var vertices = new Vector3[buffer.Vertices.Count];
            var normals = new Vector3[buffer.Normals.Count];
            var uvs = new Vector2[buffer.UVs.Count];
            var colors = new Color32[buffer.Colors.Count];

            for (var i = 0; i < vertices.Length; i++)
            {
                var v = buffer.Vertices[i];
                vertices[i] = new Vector3(v.X, v.Y, v.Z);
            }

            for (var i = 0; i < normals.Length; i++)
            {
                var n = buffer.Normals[i];
                normals[i] = new Vector3(n.X, n.Y, n.Z);
            }

            for (var i = 0; i < uvs.Length; i++)
            {
                var uv = buffer.UVs[i];
                uvs[i] = new Vector2(uv.X, uv.Y);
            }

            for (var i = 0; i < colors.Length; i++)
            {
                var c = buffer.Colors[i];
                colors[i] = new Color32(c.R, c.G, c.B, c.A);
            }

            mesh.SetVertices(vertices);
            mesh.SetNormals(normals);
            mesh.SetUVs(0, uvs);
            if (colors.Length == vertices.Length)
            {
                mesh.colors32 = colors;
            }

            var hasDoubleSidedCutout = buffer.TrianglesDoubleSidedCutout.Count > 0;
            var hasTranslucent = buffer.TrianglesTranslucent.Count > 0;

            if (hasDoubleSidedCutout && hasTranslucent)
            {
                mesh.subMeshCount = 3;
                mesh.SetTriangles(buffer.TrianglesSolid, 0);
                mesh.SetTriangles(buffer.TrianglesDoubleSidedCutout, 1);
                mesh.SetTriangles(buffer.TrianglesTranslucent, 2);
            }
            else if (hasDoubleSidedCutout)
            {
                mesh.subMeshCount = 2;
                mesh.SetTriangles(buffer.TrianglesSolid, 0);
                mesh.SetTriangles(buffer.TrianglesDoubleSidedCutout, 1);
            }
            else if (hasTranslucent)
            {
                mesh.subMeshCount = 2;
                mesh.SetTriangles(buffer.TrianglesSolid, 0);
                mesh.SetTriangles(buffer.TrianglesTranslucent, 1);
            }
            else
            {
                mesh.subMeshCount = 1;
                mesh.SetTriangles(buffer.TrianglesSolid, 0);
            }

            mesh.RecalculateBounds();
            return mesh;
        }

        private sealed class MeshBuffer
        {
            public List<Float3> Vertices { get; } = new();
            public List<Float3> Normals { get; } = new();
            public List<Float2> UVs { get; } = new();
            public List<Color32Byte> Colors { get; } = new();
            public List<int> TrianglesSolid { get; } = new();
            public List<int> TrianglesDoubleSidedCutout { get; } = new();
            public List<int> TrianglesTranslucent { get; } = new();
        }
    }
}