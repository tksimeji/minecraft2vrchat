using System;
using System.Collections.Generic;
using UnityEngine;

namespace M2V.Editor.Model
{
    internal sealed class BlockModel
    {
        internal string Parent;
        internal Dictionary<string, string> Textures = new Dictionary<string, string>(StringComparer.Ordinal);
        internal List<ModelElement> Elements = new List<ModelElement>();

        internal void ApplyParent(BlockModel parent)
        {
            if (parent == null)
            {
                return;
            }

            if (Elements == null || Elements.Count == 0)
            {
                Elements = parent.Elements;
            }

            if (parent.Textures == null)
            {
                return;
            }

            foreach (var pair in parent.Textures)
            {
                if (!Textures.ContainsKey(pair.Key))
                {
                    Textures[pair.Key] = pair.Value;
                }
            }
        }

        internal string ResolveTexture(string texture)
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

    internal sealed class ModelPlacement
    {
        internal BlockModel Model;
        internal int RotateX;
        internal int RotateY;
        internal int RotateZ;
        internal bool UvLock;
    }

    internal sealed class ModelElement
    {
        internal Vector3 From;
        internal Vector3 To;
        internal ElementRotation Rotation;
        internal Dictionary<Direction, ModelFace> Faces = new Dictionary<Direction, ModelFace>();

        internal Vector3[] GetCorners()
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

        internal Vector3[] GetFaceQuad(Direction dir, Vector3[] corners)
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

        internal float[] DefaultUv(Direction dir)
        {
            var min = From;
            var max = To;
            return dir switch
            {
                Direction.North => new[] { min.x, 16f - max.y, max.x, 16f - min.y },
                Direction.South => new[] { min.x, 16f - max.y, max.x, 16f - min.y },
                Direction.West => new[] { min.z, 16f - max.y, max.z, 16f - min.y },
                Direction.East => new[] { min.z, 16f - max.y, max.z, 16f - min.y },
                _ => new[] { min.x, min.z, max.x, max.z }
            };
        }
    }

    internal sealed class ElementRotation
    {
        internal Vector3 Origin;
        internal Vector3 AxisVector;
        internal float Angle;
        internal bool Rescale;
    }

    internal sealed class ModelFace
    {
        internal float[] Uv;
        internal string Texture;
        internal string CullFace;
        internal int Rotation;
        internal int? TintIndex;
    }

    internal enum Direction
    {
        North,
        South,
        West,
        East,
        Up,
        Down
    }

    internal static class ModelUtil
    {
        internal static Direction ParseDirection(string name)
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

        internal static bool IsModelFullCube(BlockModel model)
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
    }
}
