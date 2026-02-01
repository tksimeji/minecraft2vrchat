#nullable enable

using M2V.Editor.Minecraft;
using System.Collections.Generic;
using M2V.Editor.Minecraft.Model;
using UnityEngine;

namespace M2V.Editor.Bakery
{
    public static class M2VMathHelper
    {
        public static int FloorDiv(int value, int divisor)
        {
            var result = value / divisor;
            if ((value ^ divisor) < 0 && value % divisor != 0)
            {
                result--;
            }

            return result;
        }

        public static Vector3 RotateAround(Vector3 point, Vector3 origin, Quaternion rotation)
        {
            return rotation * (point - origin) + origin;
        }

        public static Direction RotateDirection(Direction direction, int rotX, int rotY, int rotZ = 0)
        {
            var vector = DirectionToVector(direction);
            var q = Quaternion.Euler(-rotX, -rotY, -rotZ);
            var rotated = q * vector;
            return VectorToDirection(rotated);
        }

        public static Vector3 DirectionToNormal(Direction direction)
        {
            return direction switch
            {
                Direction.North => new Vector3(0f, 0f, -1f),
                Direction.South => new Vector3(0f, 0f, 1f),
                Direction.West => new Vector3(-1f, 0f, 0f),
                Direction.East => new Vector3(1f, 0f, 0f),
                Direction.Up => new Vector3(0f, 1f, 0f),
                _ => new Vector3(0f, -1f, 0f)
            };
        }

        public static RectF ResolveUvRect(
            ResourceLocation texturePath,
            BlockElementFace blockElementFace,
            Variant variant,
            Dictionary<ResourceLocation, RectF> uvByTexture,
            BlockElement blockElement,
            Direction direction
        )
        {
            if (texturePath.IsEmpty
                || uvByTexture == null
                || !uvByTexture.TryGetValue(texturePath, out var atlasRect))
            {
                return new RectF(0f, 0f, 1f, 1f);
            }

            var uv = blockElementFace.GetUv(blockElement, direction);

            var u0 = uv.x / 16f;
            var v0 = uv.y / 16f;
            var u1 = uv.z / 16f;
            var v1 = uv.w / 16f;
            var rot = NormalizeRotation(blockElementFace.Rotation);
            if (direction == Direction.Down)
            {
                rot = NormalizeRotation(360 - rot);
            }

            if (variant is { HasUvLock: true })
            {
                rot = NormalizeRotation(rot + ComputeUvLockRotation(
                        direction,
                        variant.RotationX, variant.RotationY, variant.RotationZ
                    )
                );
            }

            if (rot != 0)
            {
                RotateUv(ref u0, ref v0, ref u1, ref v1, rot);
            }

            var v0F = 1f - v1;
            var v1F = 1f - v0;
            v0 = v0F;
            v1 = v1F;

            var x = atlasRect.X + atlasRect.Width * u0;
            var y = atlasRect.Y + atlasRect.Height * v0;
            var w = atlasRect.Width * (u1 - u0);
            var h = atlasRect.Height * (v1 - v0);
            return new RectF(x, y, w, h);
        }

        public static void ApplyCoordinateTransform(
            List<Float3> vertices,
            List<Float3> normals,
            List<int> triangles,
            Vector3Int min,
            bool applyTransform
        )
        {
            if (!applyTransform)
            {
                for (var i = 0; i < vertices.Count; i++)
                {
                    var v = vertices[i];
                    vertices[i] = new Float3(v.X + min.x, v.Y + min.y, v.Z + min.z);
                }

                return;
            }

            for (var i = 0; i < vertices.Count; i++)
            {
                var v = vertices[i];
                vertices[i] = new Float3(v.X + min.x, v.Y + min.y, -v.Z - min.z);
                var n = normals[i];
                normals[i] = new Float3(n.X, n.Y, -n.Z);
            }

            for (var i = 0; i < triangles.Count; i += 3)
            {
                (triangles[i + 1], triangles[i + 2]) = (triangles[i + 2], triangles[i + 1]);
            }
        }

        private static (Vector3 U, Vector3 V) GetFaceAxes(Direction direction)
        {
            return direction switch
            {
                Direction.North or Direction.South => (Vector3.right, Vector3.up),
                Direction.West or Direction.East => (Vector3.forward, Vector3.up),
                Direction.Up or Direction.Down => (Vector3.right, Vector3.forward),
                _ => (Vector3.right, Vector3.up)
            };
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

        private static int ComputeUvLockRotation(Direction direction, int rotX, int rotY, int rotZ)
        {
            var modelRotation = Quaternion.Euler(-rotX, -rotY, -rotZ);
            var axes = GetFaceAxes(direction);
            var rotatedU = modelRotation * axes.U;

            var targetDir = RotateDirection(direction, rotX, rotY, rotZ);
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

        private static Vector3 DirectionToVector(Direction direction)
        {
            return direction switch
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
            return v.z switch
            {
                < 0 => Direction.North,
                > 0 => Direction.South,
                _ => v.x switch
                {
                    < 0 => Direction.West,
                    > 0 => Direction.East,
                    _ => v.y > 0 ? Direction.Up : Direction.Down
                }
            };
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
                var temp = corners[0];
                corners[0] = corners[3];
                corners[3] = corners[2];
                corners[2] = corners[1];
                corners[1] = temp;
            }

            u0 = corners[0].x;
            v0 = corners[0].y;
            u1 = corners[2].x;
            v1 = corners[2].y;
        }
    }

    public readonly struct Float2
    {
        public readonly float X;
        public readonly float Y;

        public Float2(float x, float y)
        {
            X = x;
            Y = y;
        }
    }

    public readonly struct Float3
    {
        public readonly float X;
        public readonly float Y;
        public readonly float Z;

        public Float3(float x, float y, float z)
        {
            X = x;
            Y = y;
            Z = z;
        }
    }

    public readonly struct RectF
    {
        public readonly float X;
        public readonly float Y;
        public readonly float Width;
        public readonly float Height;

        public RectF(float x, float y, float width, float height)
        {
            X = x;
            Y = y;
            Width = width;
            Height = height;
        }
    }
}
