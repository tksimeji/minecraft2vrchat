using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using UnityEngine;

namespace M2V.Editor.Model
{
    internal record BlockStateDefinitions(
        [property: JsonProperty("variants")] Dictionary<string, List<Variant>> Variants,
        [property: JsonProperty("multipart")] List<Multipart> Multipart
    )
    {
        internal List<ModelPlacement> ResolvePlacements(BlockStateKey state, Func<string, BlockModel> resolveModel)
        {
            var result = new List<ModelPlacement>();

            if (Variants != null)
            {
                var best = FindBestVariant(Variants, state.Properties);
                if (best != null)
                {
                    AddPlacements(result, best, resolveModel);
                }
            }

            if (Multipart != null)
            {
                foreach (var entry in Multipart)
                {
                    if (entry != null && entry.Matches(state.Properties))
                    {
                        AddPlacements(result, entry.Apply, resolveModel);
                    }
                }
            }

            return result;
        }

        private static void AddPlacements(List<ModelPlacement> target, List<Variant> variants,
            Func<string, BlockModel> resolveModel)
        {
            var chosen = ChooseWeightedVariant(variants);
            if (chosen == null)
            {
                return;
            }

            var placement = chosen.ToPlacement(resolveModel);
            if (placement != null)
            {
                target.Add(placement);
            }
        }

        private static List<Variant> FindBestVariant(Dictionary<string, List<Variant>> variants,
            Dictionary<string, string> props)
        {
            List<Variant> best = null;
            var bestScore = -1;

            foreach (var pair in variants)
            {
                if (!VariantKeyMatches(pair.Key, props, out var score))
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

        private static bool VariantKeyMatches(string key, Dictionary<string, string> props, out int score)
        {
            score = 0;
            if (string.IsNullOrEmpty(key))
            {
                return true;
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

        private static Variant ChooseWeightedVariant(List<Variant> variants)
        {
            if (variants == null || variants.Count == 0)
            {
                return null;
            }

            Variant best = null;
            var bestWeight = -1;
            foreach (var variant in variants)
            {
                var weight = variant?.Weight ?? 1;
                if (weight <= bestWeight)
                {
                    continue;
                }

                bestWeight = weight;
                best = variant;
            }

            return best ?? variants[0];
        }
    }

    internal record Multipart(
        [property: JsonProperty("when")] When When,
        [property: JsonProperty("apply"), JsonConverter(typeof(ApplyListConverter))]
        List<Variant> Apply
    )
    {
        internal bool Matches(Dictionary<string, string> props)
        {
            return When == null || When.Matches(props);
        }
    }

    internal record When(
        Dictionary<string, string> Props,
        List<Dictionary<string, string>> Or
    )
    {
        internal bool Matches(Dictionary<string, string> props)
        {
            if (Or != null && Or.Count > 0)
            {
                foreach (var option in Or)
                {
                    if (Matches(option, props))
                    {
                        return true;
                    }
                }

                return false;
            }

            return Matches(Props, props);
        }

        private static bool Matches(Dictionary<string, string> whenDict, Dictionary<string, string> props)
        {
            if (whenDict == null || whenDict.Count == 0)
            {
                return true;
            }

            if (props == null || props.Count == 0)
            {
                return false;
            }

            foreach (var pair in whenDict)
            {
                var key = pair.Key;
                var expected = pair.Value ?? string.Empty;
                if (!props.TryGetValue(key, out var value))
                {
                    return false;
                }

                var options = expected.Split('|');
                var matched = false;
                foreach (var option in options)
                {
                    if (string.Equals(value, option, StringComparison.Ordinal))
                    {
                        matched = true;
                        break;
                    }
                }

                if (!matched)
                {
                    return false;
                }
            }

            return true;
        }
    }

    internal record Variant(
        [property: JsonProperty("model")] string Model,
        [property: JsonProperty("x")] int? X,
        [property: JsonProperty("y")] int? Y,
        [property: JsonProperty("z")] int? Z,
        [property: JsonProperty("uvlock")] bool? UvLock,
        [property: JsonProperty("weight")] int? Weight
    )
    {
        internal ModelPlacement ToPlacement(Func<string, BlockModel> resolveModel)
        {
            if (string.IsNullOrEmpty(Model))
            {
                return null;
            }

            var model = resolveModel(Model);
            if (model == null)
            {
                return null;
            }

            return new ModelPlacement
            {
                Model = model,
                RotateX = X ?? 0,
                RotateY = Y ?? 0,
                RotateZ = Z ?? 0,
                UvLock = UvLock ?? false
            };
        }
    }

    internal record Model(
        [property: JsonProperty("parent")] string Parent,
        [property: JsonProperty("textures")] Dictionary<string, string> Textures,
        [property: JsonProperty("elements")] List<Element> Elements
    )
    {
        internal BlockModel ToModel()
        {
            var model = new BlockModel { Parent = Parent };
            if (Textures != null)
            {
                foreach (var pair in Textures)
                {
                    model.Textures[pair.Key] = pair.Value;
                }
            }

            if (Elements != null)
            {
                foreach (var elementDef in Elements)
                {
                    var element = elementDef?.ToElement();
                    if (element != null)
                    {
                        model.Elements.Add(element);
                    }
                }
            }

            return model;
        }
    }

    internal record Element(
        [property: JsonProperty("from")] float[] From,
        [property: JsonProperty("to")] float[] To,
        [property: JsonProperty("rotation")] Rotation Rotation,
        [property: JsonProperty("faces"), JsonConverter(typeof(ModelFacesConverter))]
        Dictionary<string, Face> Faces
    )
    {
        internal ModelElement ToElement()
        {
            var element = new ModelElement();
            if (From != null && From.Length >= 3)
            {
                element.From = new Vector3(From[0], From[1], From[2]);
            }

            if (To != null && To.Length >= 3)
            {
                element.To = new Vector3(To[0], To[1], To[2]);
            }

            if (Rotation != null)
            {
                element.Rotation = Rotation.ToRotation();
            }

            if (Faces != null)
            {
                foreach (var facePair in Faces)
                {
                    var dir = ModelUtil.ParseDirection(facePair.Key);
                    var face = facePair.Value?.ToFace();
                    if (face != null)
                    {
                        element.Faces[dir] = face;
                    }
                }
            }

            return element;
        }
    }

    internal record Rotation(
        [property: JsonProperty("origin")] float[] Origin,
        [property: JsonProperty("axis")] string Axis,
        [property: JsonProperty("angle")] float Angle,
        [property: JsonProperty("rescale")] bool? Rescale
    )
    {
        internal ElementRotation ToRotation()
        {
            var rotation = new ElementRotation();
            if (Origin != null && Origin.Length >= 3)
            {
                rotation.Origin = new Vector3(Origin[0], Origin[1], Origin[2]) / 16f;
            }

            if (!string.IsNullOrEmpty(Axis))
            {
                rotation.AxisVector = Axis switch
                {
                    "x" => Vector3.right,
                    "y" => Vector3.up,
                    "z" => Vector3.forward,
                    _ => Vector3.up
                };
            }

            rotation.Angle = Angle;
            rotation.Rescale = Rescale ?? false;
            return rotation;
        }
    }

    internal record Face(
        [property: JsonProperty("uv")] float[] Uv,
        [property: JsonProperty("texture")] string Texture,
        [property: JsonProperty("cullface")] string CullFace,
        [property: JsonProperty("rotation")] int Rotation,
        [property: JsonProperty("tintindex")] int? TintIndex
    )
    {
        internal ModelFace ToFace()
        {
            return new ModelFace
            {
                Uv = Uv,
                Texture = Texture,
                CullFace = CullFace,
                Rotation = Rotation
            };
        }
    }
}