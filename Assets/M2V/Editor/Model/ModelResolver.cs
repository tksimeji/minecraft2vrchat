using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using fNbt;
using Newtonsoft.Json;
using M2V.Editor.World.Block;

namespace M2V.Editor.Model
{
    internal sealed class ModelResolver
    {
        private readonly IAssetReader _assets;

        private readonly Dictionary<string, BlockStateDefinitions> _blockStateCache =
            new Dictionary<string, BlockStateDefinitions>(StringComparer.Ordinal);

        private readonly Dictionary<string, BlockModel> _modelCache =
            new Dictionary<string, BlockModel>(StringComparer.Ordinal);

        internal ModelResolver(IAssetReader assets)
        {
            _assets = assets;
        }

        internal List<List<ModelPlacement>> BuildBlockModels(List<BlockState> states)
        {
            var result = new List<List<ModelPlacement>>(states.Count) { new List<ModelPlacement>() };
            for (var i = 1; i < states.Count; i++)
            {
                result.Add(ResolvePlacements(states[i]));
            }

            return result;
        }

        internal List<bool> BuildFullCubeFlags(List<List<ModelPlacement>> modelCache)
        {
            var result = new List<bool>(modelCache.Count) { false };
            for (var i = 1; i < modelCache.Count; i++)
            {
                result.Add(IsFullCubeList(modelCache[i]));
            }

            return result;
        }

        internal HashSet<string> CollectTexturePaths(List<List<ModelPlacement>> modelCache)
        {
            var textures = new HashSet<string>(StringComparer.Ordinal);
            foreach (var list in modelCache)
            {
                if (list == null) continue;
                foreach (var instance in list)
                {
                    CollectTextures(instance?.Model, textures);
                }
            }

            return textures;
        }

        private List<ModelPlacement> ResolvePlacements(BlockState state)
        {
            var blockName = StripMinecraftNamespace(state?.Name);
            var blockState = LoadBlockState(blockName);
            if (blockState == null)
            {
                return new List<ModelPlacement> { CreatePlacement(ResolveModel($"minecraft:block/{blockName}")) };
            }

            var properties = ConvertProperties(state?.Properties);
            var placements = blockState.ResolvePlacements(blockName, properties, ResolveModel);
            if (placements.Count == 0)
            {
                placements.Add(CreatePlacement(ResolveModel($"minecraft:block/{blockName}")));
            }

            return placements;
        }

        private BlockStateDefinitions LoadBlockState(string blockName)
        {
            if (_blockStateCache.TryGetValue(blockName, out var cached))
            {
                return cached;
            }

            var path = $"assets/minecraft/blockstates/{blockName}.json";
            if (!_assets.TryReadText(path, out var json))
            {
                _blockStateCache[blockName] = null;
                return null;
            }

            var data = JsonConvert.DeserializeObject<BlockStateDefinitions>(json, ModelJsonSerializer.Settings);
            _blockStateCache[blockName] = data;
            return data;
        }

        private BlockModel ResolveModel(string modelName)
        {
            var normalized = NormalizeModelName(modelName);
            if (_modelCache.TryGetValue(normalized, out var cached))
            {
                return cached;
            }

            var path = $"assets/minecraft/models/{normalized}.json";
            if (!_assets.TryReadText(path, out var json))
            {
                var fallback = new BlockModel();
                _modelCache[normalized] = fallback;
                return fallback;
            }

            var data = JsonConvert.DeserializeObject<Model>(json, ModelJsonSerializer.Settings);
            var model = data?.ToModel() ?? new BlockModel();
            _modelCache[normalized] = model;

            if (!string.IsNullOrEmpty(model.Parent))
            {
                var parent = ResolveModel(model.Parent);
                model.ApplyParent(parent);
            }

            return model;
        }

        private static string NormalizeModelName(string modelName)
        {
            var name = modelName ?? string.Empty;
            if (name.StartsWith("minecraft:", StringComparison.Ordinal))
            {
                name = name.Substring("minecraft:".Length);
            }

            if (!name.Contains("/", StringComparison.Ordinal))
            {
                name = "block/" + name;
            }

            return name;
        }

        private static bool IsFullCubeList(List<ModelPlacement> instances)
        {
            if (instances == null || instances.Count == 0)
            {
                return false;
            }

            foreach (var instance in instances)
            {
                if (instance?.Model == null || !ModelUtil.IsModelFullCube(instance.Model))
                {
                    return false;
                }
            }

            return true;
        }

        private static void CollectTextures(BlockModel model, HashSet<string> textures)
        {
            if (model == null)
            {
                return;
            }

            foreach (var tex in model.Textures.Values)
            {
                var resolved = model.ResolveTexture(tex);
                if (!string.IsNullOrEmpty(resolved))
                {
                    textures.Add(resolved);
                }
            }

            foreach (var element in model.Elements)
            {
                if (element?.Faces == null)
                {
                    continue;
                }

                foreach (var face in element.Faces.Values)
                {
                    var resolved = model.ResolveTexture(face.Texture);
                    if (!string.IsNullOrEmpty(resolved))
                    {
                        textures.Add(resolved);
                    }
                }
            }
        }

        private static string StripMinecraftNamespace(string name)
        {
            if (string.IsNullOrEmpty(name))
            {
                return string.Empty;
            }

            return name.StartsWith("minecraft:", StringComparison.Ordinal)
                ? name.Substring("minecraft:".Length)
                : name;
        }

        private static Dictionary<string, string> ConvertProperties(IReadOnlyDictionary<string, NbtTag> properties)
        {
            if (properties == null || properties.Count == 0)
            {
                return new Dictionary<string, string>(StringComparer.Ordinal);
            }

            var result = new Dictionary<string, string>(properties.Count, StringComparer.Ordinal);
            foreach (var kvp in properties)
            {
                result[kvp.Key] = FormatNbtValue(kvp.Value);
            }

            return result;
        }

        private static string FormatNbtValue(NbtTag tag)
        {
            if (tag == null)
            {
                return string.Empty;
            }

            return tag switch
            {
                NbtString str => str.Value ?? string.Empty,
                NbtByte b => b.Value == 0 ? "false" :
                    b.Value == 1 ? "true" : b.Value.ToString(CultureInfo.InvariantCulture),
                NbtShort s => s.Value.ToString(CultureInfo.InvariantCulture),
                NbtInt i => i.Value.ToString(CultureInfo.InvariantCulture),
                NbtLong l => l.Value.ToString(CultureInfo.InvariantCulture),
                NbtFloat f => f.Value.ToString(CultureInfo.InvariantCulture),
                NbtDouble d => d.Value.ToString(CultureInfo.InvariantCulture),
                _ => tag.ToString()
            };
        }

        private static ModelPlacement CreatePlacement(BlockModel model)
        {
            if (model == null)
            {
                return null;
            }

            return new ModelPlacement { Model = model, RotateX = 0, RotateY = 0, RotateZ = 0, UvLock = false };
        }
    }
}
