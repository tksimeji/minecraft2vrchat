#nullable enable

using System;
using System.Collections.Generic;
using System.Text.Json;

namespace M2V.Editor.Minecraft.Model
{
    public static class ModelLoader
    {
        public static bool TryLoadBlockState(
            AssetReader reader,
            ResourceLocation blockName,
            out BlockModelDefinition? definition
        )
        {
            definition = null;
            if (blockName.IsEmpty) return false;

            var path = $"assets/{blockName.Namespace}/blockstates/{blockName.Path}.json";
            return TryReadJson(reader, path, out definition);
        }

        private static bool TryLoadModel(AssetReader reader, ResourceLocation modelLocation, out BlockModel? model)
        {
            model = null;
            if (modelLocation.IsEmpty)
            {
                return false;
            }

            var path = $"assets/{modelLocation.Namespace}/models/{modelLocation.Path}.json";
            return TryReadJson(reader, path, out model);
        }

        public static BlockModel? ResolveModelWithParents(
            AssetReader reader,
            ResourceLocation modelLocation,
            Dictionary<ResourceLocation, BlockModel> modelCache,
            Dictionary<ResourceLocation, BlockModel> resolvedModelCache,
            HashSet<ResourceLocation> visiting
        )
        {
            if (modelLocation.IsEmpty) return null;
            
            if (resolvedModelCache.TryGetValue(modelLocation, out var cached))
            {
                return cached;
            }

            if (!visiting.Add(modelLocation))
            {
                return null;
            }

            if (modelCache.TryGetValue(modelLocation, out var cachedModel))
            {
                return ResolveParentsIfNeeded(reader, modelLocation, cachedModel, modelCache, resolvedModelCache,
                    visiting);
            }

            if (!TryLoadModel(reader, modelLocation, out var model) || model == null)
            {
                visiting.Remove(modelLocation);
                return null;
            }

            modelCache[modelLocation] = model;

            return ResolveParentsIfNeeded(reader, modelLocation, model, modelCache, resolvedModelCache, visiting);
        }

        private static BlockModel ResolveParentsIfNeeded(
            AssetReader reader,
            ResourceLocation modelLocation,
            BlockModel model,
            Dictionary<ResourceLocation, BlockModel> modelCache,
            Dictionary<ResourceLocation, BlockModel> resolvedModelCache,
            HashSet<ResourceLocation> visiting
        )
        {
            var resolved = model;
            if (!model.Parent.IsEmpty)
            {
                var parent = ResolveModelWithParents(reader, model.Parent, modelCache, resolvedModelCache, visiting);
                if (parent != null)
                {
                    resolved = model.WithParentApplied(parent);
                }
            }

            resolvedModelCache[modelLocation] = resolved;

            visiting.Remove(modelLocation);
            return resolved;
        }

        private static bool TryReadJson<T>(AssetReader reader, string path, out T? result)
            where T : class
        {
            result = null;
            if (string.IsNullOrEmpty(path)) return false;
            if (!reader.TryReadText(path, out var json) || string.IsNullOrEmpty(json))
                return false;

            try
            {
                result = JsonSerializer.Deserialize<T>(json, BlockModel.JsonOptions);
                return result != null;
            }
            catch (Exception)
            {
                return false;
            }
        }
    }
}