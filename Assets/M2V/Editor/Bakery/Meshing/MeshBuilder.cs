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
        public sealed record BuildResult(Mesh? Mesh, Texture2D AtlasTexture);

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

            var tintResolver = new BlockTintResolver(assetReader, biomeIndex);
            var tintByBlock = tintResolver.BuildTintByBlock(volume);

            var atlasTexture = AtlasBuilder.BuildAtlas(
                assetReader,
                texturePaths,
                out var uvByTexture,
                out var alphaByTexture
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
                atlasTexture
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
            var trianglesTranslucent = buffer.TrianglesTranslucent;

            var dims = new[] { volume.SizeX, volume.SizeY, volume.SizeZ };

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

                        var variants = blockModelIndex[id];
                        if (variants.Count == 0) continue;

                        var blockIndex = x + dims[0] * (y + dims[1] * z);
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
                                trianglesTranslucent,
                                uvByTexture,
                                alphaByTexture
                            );
                        }
                    }
                }
            }

            if (vertices.Count == 0) return null;

            M2VMathHelper.ApplyCoordinateTransform(
                vertices, normals, trianglesSolid, min, applyCoordinateTransform
            );
            M2VMathHelper.ApplyCoordinateTransform(
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
            List<int> trianglesTranslucent,
            Dictionary<ResourceLocation, RectF> uvByTexture,
            Dictionary<ResourceLocation, TextureAlphaMode> alphaByTexture
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
                    var rot = Quaternion.AngleAxis(element.Rotation.Angle, element.Rotation.ToAxisVector());
                    for (var i = 0; i < corners.Length; i++)
                    {
                        corners[i] = M2VMathHelper.RotateAround(corners[i], element.Rotation.ToToOriginVector(), rot);
                    }

                    if (element.Rotation.Rescale ?? false)
                    {
                        var radians = Mathf.Abs(element.Rotation.Angle) * Mathf.Deg2Rad;
                        var scaleFactor = Mathf.Cos(radians) > 0.0001f ? 1f / Mathf.Cos(radians) : 1f;
                        Vector3 scale;
                        if (element.Rotation.ToAxisVector() == Vector3.right)
                        {
                            scale = new Vector3(1f, scaleFactor, scaleFactor);
                        }
                        else if (element.Rotation.ToAxisVector() == Vector3.up)
                        {
                            scale = new Vector3(scaleFactor, 1f, scaleFactor);
                        }
                        else
                        {
                            scale = new Vector3(scaleFactor, scaleFactor, 1f);
                        }

                        for (var i = 0; i < corners.Length; i++)
                        {
                            var origin = element.Rotation.ToToOriginVector();
                            var offset = corners[i] - origin;
                            corners[i] = origin + Vector3.Scale(offset, scale);
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
                    var uvRect = M2VMathHelper.ResolveUvRect(
                        texturePath, face, variant, uvByTexture, element, direction
                    );
                    var alphaMode = ResolveAlphaMode(texturePath, alphaByTexture);
                    var tint = ResolveTint(face, blockIndex, tintByBlock);
                    var targetTriangles = alphaMode == TextureAlphaMode.Translucent
                        ? trianglesTranslucent
                        : trianglesSolid;
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
                        colors
                    );
                }
            }
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

            if (buffer.TrianglesTranslucent.Count > 0)
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
            public List<int> TrianglesTranslucent { get; } = new();
        }
    }
}
