#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using M2V.Editor.Minecraft;
using UnityEngine;

namespace M2V.Editor.Bakery.Meshing
{
    public sealed class AtlasBuilder
    {
        private const int DefaultTileSize = 16;

        private static readonly ResourceLocation FallbackTexture = ResourceLocation.Of("block/dirt");

        public static Texture2D BuildAtlas(
            AssetReader assetReader,
            HashSet<ResourceLocation> texturePaths,
            out Dictionary<ResourceLocation, RectF> uvByTexture,
            out Dictionary<ResourceLocation, TextureAlphaMode> alphaByTexture,
            out AtlasAnimation? atlasAnimation,
            Action<float>? reportProgress = null
        )
        {
            uvByTexture = new Dictionary<ResourceLocation, RectF>();
            alphaByTexture = new Dictionary<ResourceLocation, TextureAlphaMode>();
            atlasAnimation = null;

            var names = new List<ResourceLocation>(texturePaths);
            names.Sort((a, b) => string.CompareOrdinal(a.ToString(), b.ToString()));

            var textures = new List<Texture2D>(names.Count);
            var animatedEntries = new List<AnimatedTexture>();
            var tileSize = DefaultTileSize;

            foreach (var name in names)
            {
                reportProgress?.Invoke(names.Count == 0 ? 1f : (float)textures.Count / names.Count);
                var loaded = TryLoadTexture(assetReader, name);
                var resolved = loaded
                               ?? TryLoadTexture(assetReader, FallbackTexture)
                               ?? CreateFallbackTexture(DefaultTileSize);

                var animation = TryLoadAnimation(assetReader, name, resolved);
                var frameSize = animation?.FrameSize ?? resolved.width;
                tileSize = Mathf.Max(tileSize, frameSize);

                if (animation != null)
                {
                    animatedEntries.Add(animation);
                }

                textures.Add(resolved);
            }

            var count = textures.Count;
            if (count == 0)
            {
                var fallback = new Texture2D(DefaultTileSize, DefaultTileSize, TextureFormat.RGBA32, false)
                {
                    filterMode = FilterMode.Point,
                    wrapMode = TextureWrapMode.Repeat
                };
                fallback.SetPixels(CreateFallbackPixels(DefaultTileSize, DefaultTileSize));
                fallback.Apply();
                return FinalizeAtlas(fallback);
            }

            var columns = Mathf.CeilToInt(Mathf.Sqrt(count));
            var rows = Mathf.CeilToInt((float)count / columns);
            var atlasSize = Mathf.NextPowerOfTwo(Mathf.Max(1, columns * tileSize));
            atlasSize = Mathf.Max(atlasSize, Mathf.NextPowerOfTwo(rows * tileSize));

            var atlas = new Texture2D(atlasSize, atlasSize, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Repeat
            };
            atlas.SetPixels(CreateFallbackPixels(atlasSize, atlasSize));

            for (var i = 0; i < textures.Count; i++)
            {
                reportProgress?.Invoke(textures.Count == 0 ? 1f : (float)i / textures.Count);
                var col = i % columns;
                var row = i / columns;
                var x = col * tileSize;
                var y = row * tileSize;
                var texture = textures[i];
                var name = names[i];
                var animated = animatedEntries.FirstOrDefault(entry => entry.Id == name);
                if (animated != null)
                {
                    var framePixels = animated.GetFramePixels(0, tileSize);
                    atlas.SetPixels32(x, y, tileSize, tileSize, framePixels);
                    animated.SetAtlasRect(x, y, tileSize);
                }
                else
                {
                    DrawTile(texture, atlas, x, y, tileSize);
                }

                var rect = new RectF(
                    (float)x / atlasSize,
                    (float)y / atlasSize,
                    (float)tileSize / atlasSize,
                    (float)tileSize / atlasSize
                );

                uvByTexture[name] = rect;
                alphaByTexture[name] = GetAlphaMode(texture);
            }

            atlas.Apply();
            atlasAnimation = animatedEntries.Count > 0 ? new AtlasAnimation(animatedEntries) : null;
            reportProgress?.Invoke(1f);
            return FinalizeAtlas(atlas);
        }

        private static AnimatedTexture? TryLoadAnimation(AssetReader assetReader, ResourceLocation texturePath, Texture2D texture)
        {
            var metaPath = texturePath.ToAssetPath() + ".mcmeta";
            if (!assetReader.TryReadText(metaPath, out var json) || string.IsNullOrEmpty(json))
            {
                return null;
            }

            try
            {
                using var document = JsonDocument.Parse(json);
                if (!document.RootElement.TryGetProperty("animation", out var animation))
                {
                    return null;
                }

                var frameSize = texture.width;
                if (frameSize <= 0) return null;

                var frameCount = texture.height / frameSize;
                if (frameCount <= 1) return null;

                var frameTime = animation.TryGetProperty("frametime", out var ft) && ft.ValueKind == JsonValueKind.Number
                    ? Math.Max(1, ft.GetInt32())
                    : 1;
                var interpolate = animation.TryGetProperty("interpolate", out var interp) &&
                                  interp.ValueKind == JsonValueKind.True;

                var frames = new List<AnimatedFrame>();
                if (animation.TryGetProperty("frames", out var framesToken) &&
                    framesToken.ValueKind == JsonValueKind.Array)
                {
                    foreach (var frame in framesToken.EnumerateArray())
                    {
                        if (frame.ValueKind == JsonValueKind.Number)
                        {
                            frames.Add(new AnimatedFrame(frame.GetInt32(), frameTime));
                            continue;
                        }

                        if (frame.ValueKind == JsonValueKind.Object)
                        {
                            var index = frame.TryGetProperty("index", out var indexToken) &&
                                        indexToken.ValueKind == JsonValueKind.Number
                                ? indexToken.GetInt32()
                                : 0;
                            var time = frame.TryGetProperty("time", out var timeToken) &&
                                       timeToken.ValueKind == JsonValueKind.Number
                                ? Math.Max(1, timeToken.GetInt32())
                                : frameTime;
                            frames.Add(new AnimatedFrame(index, time));
                        }
                    }
                }
                else
                {
                    for (var i = 0; i < frameCount; i++)
                    {
                        frames.Add(new AnimatedFrame(i, frameTime));
                    }
                }

                if (frames.Count == 0)
                {
                    return null;
                }

                return new AnimatedTexture(texturePath, texture, frameSize, frames, interpolate);
            }
            catch
            {
                return null;
            }
        }

        private static Texture2D? TryLoadTexture(AssetReader assetReader, ResourceLocation texturePath)
        {
            if (texturePath.IsEmpty) return null;

            var fullPath = texturePath.ToAssetPath();
            if (!assetReader.TryReadBytes(fullPath, out var bytes))
                return null;

            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            if (!texture.LoadImage(bytes)) return null;

            texture.filterMode = FilterMode.Point;
            texture.wrapMode = TextureWrapMode.Repeat;
            return texture;
        }

        private static Texture2D CreateFallbackTexture(int size)
        {
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
            texture.SetPixels(CreateFallbackPixels(size, size));
            texture.Apply();
            return texture;
        }

        private static Color[] CreateFallbackPixels(int width, int height)
        {
            var colors = new Color[width * height];
            Array.Fill(colors, new Color(1f, 0f, 1f, 1f));
            return colors;
        }

        private static void DrawTile(Texture2D source, Texture2D atlas, int destX, int destY, int tileSize)
        {
            if (source == null || atlas == null) return;

            var width = source.width;
            var height = source.height;
            if (width == tileSize && height == tileSize)
            {
                var pixels = source.GetPixels();
                atlas.SetPixels(destX, destY, tileSize, tileSize, pixels);
                return;
            }

            var scaled = new Color[tileSize * tileSize];
            for (var y = 0; y < tileSize; y++)
            {
                var srcY = y * height / tileSize;
                for (var x = 0; x < tileSize; x++)
                {
                    var srcX = x * width / tileSize;
                    scaled[x + y * tileSize] = source.GetPixel(srcX, srcY);
                }
            }

            atlas.SetPixels(destX, destY, tileSize, tileSize, scaled);
        }

        private static TextureAlphaMode GetAlphaMode(Texture2D texture)
        {
            var pixels = texture.GetPixels32();
            var hasAlpha = false;
            var hasPartial = false;
            foreach (var pixel in pixels)
            {
                if (pixel.a == 255)
                {
                    continue;
                }

                hasAlpha = true;
                if (pixel.a == 0)
                {
                    continue;
                }

                hasPartial = true;
                break;
            }

            if (!hasAlpha)
            {
                return TextureAlphaMode.Opaque;
            }

            return hasPartial ? TextureAlphaMode.Translucent : TextureAlphaMode.Cutout;
        }

        private static Texture2D FinalizeAtlas(Texture2D atlas)
        {
            atlas.filterMode = FilterMode.Point;
            atlas.wrapMode = TextureWrapMode.Repeat;
            return atlas;
        }
    }

    public enum TextureAlphaMode
    {
        Opaque,
        Cutout,
        Translucent
    }

    public sealed class AtlasAnimation
    {
        private readonly List<AnimatedTexture> _textures;

        public AtlasAnimation(List<AnimatedTexture> textures)
        {
            _textures = textures;
        }

        public void Update(Texture2D atlas, double timeSeconds)
        {
            if (_textures.Count == 0)
            {
                return;
            }

            var tick = (long)Math.Floor(timeSeconds * 20.0);
            var changed = false;

            foreach (var texture in _textures)
            {
                if (texture.ApplyFrame(atlas, tick))
                {
                    changed = true;
                }
            }

            if (changed)
            {
                atlas.Apply(false);
            }
        }
    }

    public sealed class AnimatedTexture
    {
        private readonly List<AnimatedFrame> _frames;
        private readonly Color32[][] _framePixels;
        private readonly int _frameSize;
        private readonly bool _interpolate;
        private int _totalTicks;
        private int _lastFrame = -1;
        private RectInt _atlasRect;
        private Color32[]? _scratch;

        public AnimatedTexture(ResourceLocation id, Texture2D source, int frameSize, List<AnimatedFrame> frames, bool interpolate)
        {
            Id = id;
            _frames = frames;
            _frameSize = frameSize;
            _interpolate = interpolate;
            _framePixels = new Color32[_frames.Count][];

            for (var i = 0; i < _frames.Count; i++)
            {
                var frameIndex = Mathf.Clamp(_frames[i].Index, 0, source.height / frameSize - 1);
                _framePixels[i] = ExtractFramePixels(source, frameIndex, frameSize);
                _totalTicks += _frames[i].Time;
            }
        }

        public ResourceLocation Id { get; }

        public int FrameSize => _frameSize;

        public void SetAtlasRect(int x, int y, int size)
        {
            _atlasRect = new RectInt(x, y, size, size);
        }

        public Color32[] GetFramePixels(int frameIndex, int tileSize)
        {
            var clamped = Mathf.Clamp(frameIndex, 0, _framePixels.Length - 1);
            var pixels = _framePixels[clamped];
            if (_frameSize == tileSize)
            {
                return pixels;
            }

            return ScalePixels(pixels, _frameSize, tileSize);
        }

        public bool ApplyFrame(Texture2D atlas, long tick)
        {
            if (_totalTicks <= 0 || _frames.Count == 0)
            {
                return false;
            }

            var localTick = (int)(tick % _totalTicks);
            var current = 0;
            var accumulated = 0;
            for (var i = 0; i < _frames.Count; i++)
            {
                var duration = _frames[i].Time;
                if (localTick < accumulated + duration)
                {
                    current = i;
                    break;
                }
                accumulated += duration;
            }

            if (!_interpolate)
            {
                if (current == _lastFrame)
                {
                    return false;
                }

                _lastFrame = current;
                var pixels = GetFramePixels(current, _atlasRect.width);
                atlas.SetPixels32(_atlasRect.x, _atlasRect.y, _atlasRect.width, _atlasRect.height, pixels);
                return true;
            }

            var currentFrame = _frames[current];
            var currentStart = accumulated;
            var frameProgress = Mathf.Clamp01((localTick - currentStart) / (float)currentFrame.Time);
            var next = (current + 1) % _frames.Count;

            _scratch ??= new Color32[_atlasRect.width * _atlasRect.height];
            var a = GetFramePixels(current, _atlasRect.width);
            var b = GetFramePixels(next, _atlasRect.width);
            for (var i = 0; i < _scratch.Length; i++)
            {
                var ca = a[i];
                var cb = b[i];
                _scratch[i] = new Color32(
                    (byte)Mathf.Lerp(ca.r, cb.r, frameProgress),
                    (byte)Mathf.Lerp(ca.g, cb.g, frameProgress),
                    (byte)Mathf.Lerp(ca.b, cb.b, frameProgress),
                    (byte)Mathf.Lerp(ca.a, cb.a, frameProgress)
                );
            }

            atlas.SetPixels32(_atlasRect.x, _atlasRect.y, _atlasRect.width, _atlasRect.height, _scratch);
            return true;
        }

        private static Color32[] ExtractFramePixels(Texture2D source, int frameIndex, int frameSize)
        {
            var all = source.GetPixels32();
            var width = source.width;
            var startY = frameIndex * frameSize;
            var result = new Color32[frameSize * frameSize];

            for (var y = 0; y < frameSize; y++)
            {
                var srcY = startY + y;
                if (srcY < 0 || srcY >= source.height)
                {
                    continue;
                }

                var srcRow = srcY * width;
                var dstRow = y * frameSize;
                for (var x = 0; x < frameSize; x++)
                {
                    var srcX = x;
                    if (srcX < 0 || srcX >= width)
                    {
                        continue;
                    }

                    result[dstRow + x] = all[srcRow + srcX];
                }
            }

            return result;
        }

        private static Color32[] ScalePixels(Color32[] source, int sourceSize, int targetSize)
        {
            var scaled = new Color32[targetSize * targetSize];
            for (var y = 0; y < targetSize; y++)
            {
                var srcY = y * sourceSize / targetSize;
                for (var x = 0; x < targetSize; x++)
                {
                    var srcX = x * sourceSize / targetSize;
                    scaled[x + y * targetSize] = source[srcX + srcY * sourceSize];
                }
            }

            return scaled;
        }
    }

    public readonly struct AnimatedFrame
    {
        public readonly int Index;
        public readonly int Time;

        public AnimatedFrame(int index, int time)
        {
            Index = index;
            Time = time;
        }
    }
}