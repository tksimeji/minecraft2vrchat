using System.Collections.Generic;
using M2V.Editor.Model;
using UnityEngine;

namespace M2V.Editor.MeshGeneration
{
    internal sealed class ModelMeshBuilder : IMeshBuilder
    {
        public Mesh BuildModelMesh(int[] blocks, int sizeX, int sizeY, int sizeZ, Vector3Int min,
            List<List<ModelPlacement>> modelCache, List<bool> fullCubeById, Dictionary<string, Rect> uvByTexture,
            Dictionary<string, TextureAlphaMode> alphaByTexture, bool applyCoordinateTransform)
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
                            EmitModel(instance, x, y, z, blocks, dims, fullCubeById, vertices, normals, uvs,
                                trianglesSolid, trianglesTranslucent, uvByTexture, alphaByTexture);
                        }
                    }
                }
            }

            if (vertices.Count == 0)
            {
                return null;
            }

            ApplyCoordinateTransform(vertices, normals, trianglesSolid, min, applyCoordinateTransform);
            ApplyCoordinateTransform(vertices, normals, trianglesTranslucent, min, applyCoordinateTransform);

            var mesh = new Mesh
            {
                indexFormat = vertices.Count > 65535
                    ? UnityEngine.Rendering.IndexFormat.UInt32
                    : UnityEngine.Rendering.IndexFormat.UInt16
            };
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

        private static void EmitModel(ModelPlacement instance, int blockX, int blockY, int blockZ, int[] blocks,
            int[] dims,
            List<bool> fullCubeById,
            List<Vector3> vertices, List<Vector3> normals, List<Vector2> uvs, List<int> trianglesSolid,
            List<int> trianglesTranslucent,
            Dictionary<string, Rect> uvByTexture, Dictionary<string, TextureAlphaMode> alphaByTexture)
        {
            if (instance == null || instance.Model == null || instance.Model.Elements == null)
            {
                return;
            }

            var blockOffset = new Vector3(blockX, blockY, blockZ);
            var modelRotation = Quaternion.Euler(-instance.RotateX, -instance.RotateY, -instance.RotateZ);
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

                    if (element.Rotation.Rescale)
                    {
                        var radians = Mathf.Abs(element.Rotation.Angle) * Mathf.Deg2Rad;
                        var scaleFactor = Mathf.Cos(radians) > 0.0001f ? 1f / Mathf.Cos(radians) : 1f;
                        Vector3 scale;
                        if (element.Rotation.AxisVector == Vector3.right)
                        {
                            scale = new Vector3(1f, scaleFactor, scaleFactor);
                        }
                        else if (element.Rotation.AxisVector == Vector3.up)
                        {
                            scale = new Vector3(scaleFactor, 1f, scaleFactor);
                        }
                        else
                        {
                            scale = new Vector3(scaleFactor, scaleFactor, 1f);
                        }

                        for (var i = 0; i < corners.Length; i++)
                        {
                            var offset = corners[i] - element.Rotation.Origin;
                            corners[i] = element.Rotation.Origin + Vector3.Scale(offset, scale);
                        }
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

                    if (!string.IsNullOrEmpty(face.CullFace) && ModelUtil.IsModelFullCube(instance.Model))
                    {
                        var cullDir = RotateDirection(ModelUtil.ParseDirection(face.CullFace), instance.RotateX,
                            instance.RotateY, instance.RotateZ);
                        if (IsNeighborFullCube(blocks, dims, fullCubeById, blockX, blockY, blockZ, cullDir))
                        {
                            continue;
                        }
                    }

                    var quad = element.GetFaceQuad(dir, corners);
                    var normal =
                        DirectionToNormal(RotateDirection(dir, instance.RotateX, instance.RotateY, instance.RotateZ));
                    var texturePath = instance.Model.ResolveTexture(face.Texture);
                    var uvRect = ResolveUvRect(texturePath, face, instance, uvByTexture, element, dir);
                    var alphaMode = ResolveAlphaMode(instance, texturePath, alphaByTexture);
                    var targetTriangles = alphaMode == TextureAlphaMode.Translucent
                        ? trianglesTranslucent
                        : trianglesSolid;
                    AddQuad(vertices, targetTriangles, normals, uvs,
                        quad[0] + blockOffset,
                        quad[1] + blockOffset,
                        quad[2] + blockOffset,
                        quad[3] + blockOffset,
                        normal, uvRect, 1f, 1f);
                }
            }
        }

        private static Rect ResolveUvRect(string texturePath, ModelFace face, ModelPlacement instance,
            Dictionary<string, Rect> uvByTexture, ModelElement element, Direction dir)
        {
            if (string.IsNullOrEmpty(texturePath) || uvByTexture == null ||
                !uvByTexture.TryGetValue(texturePath, out var atlasRect))
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
            var rot = NormalizeRotation(face.Rotation);
            if (dir == Direction.Down)
            {
                rot = NormalizeRotation(360 - rot);
            }

            if (instance != null && instance.UvLock)
            {
                rot = NormalizeRotation(rot +
                                        ComputeUvLockRotation(dir, instance.RotateX, instance.RotateY,
                                            instance.RotateZ));
            }

            if (rot != 0)
            {
                RotateUv(ref u0, ref v0, ref u1, ref v1, rot);
            }

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

        private static TextureAlphaMode ResolveAlphaMode(ModelPlacement instance, string texturePath,
            Dictionary<string, TextureAlphaMode> alphaByTexture)
        {
            if (!string.IsNullOrEmpty(texturePath) && alphaByTexture != null &&
                alphaByTexture.TryGetValue(texturePath, out var mode))
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

        private static bool IsNeighborFullCube(int[] blocks, int[] dims, List<bool> fullCubeById, int x, int y, int z,
            Direction dir)
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

        private static int NormalizeRotation(int rotation)
        {
            rotation %= 360;
            if (rotation < 0)
            {
                rotation += 360;
            }

            return rotation;
        }

        private static int ComputeUvLockRotation(Direction dir, int rotX, int rotY, int rotZ)
        {
            var modelRotation = Quaternion.Euler(-rotX, -rotY, -rotZ);
            var axes = GetFaceAxes(dir);
            var rotatedU = modelRotation * axes.U;

            var targetDir = RotateDirection(dir, rotX, rotY, rotZ);
            var targetAxes = GetFaceAxes(targetDir);

            if (Vector3.Dot(rotatedU, targetAxes.U) > 0.99f)
            {
                return 0;
            }

            if (Vector3.Dot(rotatedU, targetAxes.V) > 0.99f)
            {
                return 270;
            }

            if (Vector3.Dot(rotatedU, -targetAxes.U) > 0.99f)
            {
                return 180;
            }

            if (Vector3.Dot(rotatedU, -targetAxes.V) > 0.99f)
            {
                return 90;
            }

            return 0;
        }

        private static (Vector3 U, Vector3 V) GetFaceAxes(Direction dir)
        {
            return dir switch
            {
                Direction.North => (Vector3.right, Vector3.up),
                Direction.South => (Vector3.right, Vector3.up),
                Direction.West => (Vector3.forward, Vector3.up),
                Direction.East => (Vector3.forward, Vector3.up),
                Direction.Up => (Vector3.right, Vector3.forward),
                Direction.Down => (Vector3.right, Vector3.forward),
                _ => (Vector3.right, Vector3.up)
            };
        }

        private static Direction RotateDirection(Direction dir, int rotX, int rotY, int rotZ = 0)
        {
            var vector = DirectionToVector(dir);
            var q = Quaternion.Euler(-rotX, -rotY, -rotZ);
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

        private static int GetBlockId(int[] blocks, int[] dims, int x, int y, int z)
        {
            if (x < 0 || y < 0 || z < 0 || x >= dims[0] || y >= dims[1] || z >= dims[2])
            {
                return 0;
            }

            return blocks[x + dims[0] * (y + dims[1] * z)];
        }

        private static void AddQuad(List<Vector3> vertices, List<int> triangles, List<Vector3> normals,
            List<Vector2> uvs,
            Vector3 v0, Vector3 v1, Vector3 v2, Vector3 v3, Vector3 normal, Rect uvRect, float uvW, float uvH)
        {
            var index = vertices.Count;
            vertices.Add(v0);
            vertices.Add(v1);
            vertices.Add(v2);
            vertices.Add(v3);
            normals.Add(normal);
            normals.Add(normal);
            normals.Add(normal);
            normals.Add(normal);

            uvs.Add(new Vector2(uvRect.xMin, uvRect.yMin));
            uvs.Add(new Vector2(uvRect.xMax, uvRect.yMin));
            uvs.Add(new Vector2(uvRect.xMax, uvRect.yMax));
            uvs.Add(new Vector2(uvRect.xMin, uvRect.yMax));

            triangles.Add(index + 0);
            triangles.Add(index + 1);
            triangles.Add(index + 2);
            triangles.Add(index + 2);
            triangles.Add(index + 3);
            triangles.Add(index + 0);
        }

        private static void ApplyCoordinateTransform(List<Vector3> vertices, List<Vector3> normals, List<int> triangles,
            Vector3Int min, bool applyTransform)
        {
            if (!applyTransform)
            {
                for (var i = 0; i < vertices.Count; i++)
                {
                    vertices[i] += min;
                }

                return;
            }

            for (var i = 0; i < vertices.Count; i++)
            {
                var v = vertices[i];
                vertices[i] = new Vector3(v.x + min.x, v.y + min.y, -v.z - min.z);
                normals[i] = new Vector3(normals[i].x, normals[i].y, -normals[i].z);
            }

            for (var i = 0; i < triangles.Count; i += 3)
            {
                var tmp = triangles[i + 1];
                triangles[i + 1] = triangles[i + 2];
                triangles[i + 2] = tmp;
            }
        }
    }
}