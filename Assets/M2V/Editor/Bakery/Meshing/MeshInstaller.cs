#nullable enable

using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace M2V.Editor.Bakery.Meshing
{
    public static class MeshInstaller
    {
        private const string CutoutShaderPath = "Assets/M2V/Editor/UnlitCutout.shader";
        private const string DoubleSidedCutoutShaderPath = "Assets/M2V/Editor/UnlitDoubleSided.shader";
        private const string DoubleSidedTransparentShaderPath = "Assets/M2V/Editor/UnlitDoubleSidedTransparent.shader";

        private static readonly int CullId = Shader.PropertyToID("_Cull");
        private static readonly int SurfaceId = Shader.PropertyToID("_Surface");
        private static readonly int AlphaClipId = Shader.PropertyToID("_AlphaClip");
        private static readonly int ZWriteId = Shader.PropertyToID("_ZWrite");
        private static readonly int CutoffId = Shader.PropertyToID("_Cutoff");

        public static GameObject InstallMesh(string rootName, Mesh mesh, Texture2D? atlasTexture, AtlasAnimation? animation)
        {
            var gameObject = GameObject.Find(rootName) ?? new GameObject(rootName);
            var filter = EnsureMeshFilter(gameObject, rootName);
            var renderer = EnsureMeshRenderer(gameObject, rootName);

            if (filter != null)
            {
                filter.sharedMesh = mesh;
            }
            ApplyAtlasMaterial(renderer, atlasTexture, mesh);

            var colliderChild = EnsureColliderChild(gameObject.transform);
            var collider = colliderChild.GetComponent<MeshCollider>();
            if (collider == null)
            {
                collider = colliderChild.gameObject.AddComponent<MeshCollider>();
            }

            collider.sharedMesh = null;
            collider.sharedMesh = mesh;
            collider.convex = false;

            if (animation != null && atlasTexture != null)
            {
                var animator = gameObject.GetComponent<AtlasAnimator>();
                if (animator == null)
                {
                    animator = gameObject.AddComponent<AtlasAnimator>();
                }

                if (animator != null)
                {
                    animator.Initialize(atlasTexture, animation);
                }
            }

            return gameObject;
        }

        private static MeshFilter? EnsureMeshFilter(GameObject gameObject, string rootName)
        {
            if (!gameObject.TryGetComponent(out MeshFilter filter))
            {
                filter = gameObject.AddComponent<MeshFilter>();
            }

            if (filter != null)
            {
                return filter;
            }

            Object.DestroyImmediate(gameObject);
            var fallback = new GameObject(rootName);
            return fallback.AddComponent<MeshFilter>();
        }

        private static MeshRenderer EnsureMeshRenderer(GameObject gameObject, string rootName)
        {
            if (!gameObject.TryGetComponent(out MeshRenderer renderer))
            {
                renderer = gameObject.AddComponent<MeshRenderer>();
            }

            if (renderer != null)
            {
                return renderer;
            }

            Object.DestroyImmediate(gameObject);
            var fallback = new GameObject(rootName);
            return fallback.AddComponent<MeshRenderer>();
        }

        private static Shader GetCutoutShader()
        {
            var shader = AssetDatabase.LoadAssetAtPath<Shader>(CutoutShaderPath);
            if (shader != null && shader.isSupported)
            {
                return shader;
            }

            shader = Shader.Find("World/UnlitCutout");
            if (shader != null && shader.isSupported)
            {
                return shader;
            }

            return FindSupportedShader("Unlit/Texture", "Universal Render Pipeline/Unlit", "HDRP/Unlit")
                   ?? Shader.Find("Unlit/Texture")
                   ?? Shader.Find("Sprites/Default")!;
        }

        private static Shader GetDoubleSidedCutoutShader()
        {
            var shader = AssetDatabase.LoadAssetAtPath<Shader>(DoubleSidedCutoutShaderPath);
            if (shader != null && shader.isSupported)
            {
                return shader;
            }

            shader = Shader.Find("World/UnlitDoubleSided");
            if (shader != null && shader.isSupported)
            {
                return shader;
            }

            return FindSupportedShader("Unlit/Texture", "Universal Render Pipeline/Unlit", "HDRP/Unlit")
                   ?? Shader.Find("Unlit/Texture")
                   ?? Shader.Find("Sprites/Default")!;
        }

        private static Shader GetDoubleSidedTransparentShader()
        {
            var shader = AssetDatabase.LoadAssetAtPath<Shader>(DoubleSidedTransparentShaderPath);
            if (shader != null && shader.isSupported)
            {
                return shader;
            }

            shader = Shader.Find("World/UnlitDoubleSidedTransparent");
            if (shader != null && shader.isSupported)
            {
                return shader;
            }

            return FindSupportedShader("Unlit/Transparent", "Universal Render Pipeline/Unlit", "HDRP/Unlit")
                   ?? Shader.Find("Unlit/Transparent")
                   ?? Shader.Find("Sprites/Default")!;
        }

        private static Shader? FindSupportedShader(params string[] names)
        {
            foreach (var name in names)
            {
                var shader = Shader.Find(name);
                if (shader != null && shader.isSupported)
                {
                    return shader;
                }
            }

            return null;
        }

        private static Transform EnsureColliderChild(Transform parent)
        {
            var child = parent.Find("MeshCollider");
            if (child != null)
            {
                return child;
            }

            var childObject = new GameObject("MeshCollider");
            childObject.transform.SetParent(parent, false);
            return childObject.transform;
        }

        private static bool IsUsingScriptableRenderPipeline() =>
            GraphicsSettings.currentRenderPipeline != null;

        private static Material CreateCutoutMaterial(Texture2D? texture)
        {
            return IsUsingScriptableRenderPipeline()
                ? CreateUrpUnlitMaterial(texture, transparent: false)
                : new Material(GetCutoutShader()) { mainTexture = texture };
        }

        private static Material CreateDoubleSidedCutoutMaterial(Texture2D? texture)
        {
            if (IsUsingScriptableRenderPipeline())
            {
                var material = CreateUrpUnlitMaterial(texture, transparent: false);
                if (material.HasProperty(CullId))
                {
                    material.SetInt(CullId, (int)CullMode.Off);
                }

                return material;
            }

            return new Material(GetDoubleSidedCutoutShader()) { mainTexture = texture };
        }

        private static Material CreateTransparentMaterial(Texture2D? texture)
        {
            return IsUsingScriptableRenderPipeline()
                ? CreateUrpUnlitMaterial(texture, transparent: true)
                : new Material(GetDoubleSidedTransparentShader()) { mainTexture = texture };
        }

        private static Material CreateUrpUnlitMaterial(Texture2D? texture, bool transparent)
        {
            var shader = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("HDRP/Unlit");
            if (shader == null)
            {
                return new Material(transparent ? GetDoubleSidedTransparentShader() : GetCutoutShader())
                {
                    mainTexture = texture
                };
            }

            var material = new Material(shader) { mainTexture = texture };
            if (material.HasProperty(CullId))
            {
                material.SetInt(CullId, transparent ? (int)CullMode.Off : (int)CullMode.Back);
            }

            if (transparent)
            {
                if (material.HasProperty(SurfaceId))
                {
                    material.SetFloat(SurfaceId, 1f);
                }

                if (material.HasProperty(AlphaClipId))
                {
                    material.SetFloat(AlphaClipId, 0f);
                }

                if (material.HasProperty(ZWriteId))
                {
                    material.SetFloat(ZWriteId, 0f);
                }

                material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                material.renderQueue = (int)RenderQueue.Transparent;
            }
            else
            {
                if (material.HasProperty(SurfaceId))
                {
                    material.SetFloat(SurfaceId, 0f);
                }

                if (material.HasProperty(AlphaClipId))
                {
                    material.SetFloat(AlphaClipId, 1f);
                }

                if (material.HasProperty(CutoffId))
                {
                    material.SetFloat(CutoffId, 0.5f);
                }

                material.EnableKeyword("_ALPHATEST_ON");
                material.renderQueue = (int)RenderQueue.AlphaTest;
            }

            return material;
        }

        private static void ApplyAtlasMaterial(MeshRenderer renderer, Texture2D? atlasTexture, Mesh? mesh)
        {
            if (atlasTexture == null)
            {
                ApplyFallbackMaterial(renderer, mesh);
                return;
            }

            atlasTexture.filterMode = FilterMode.Point;
            atlasTexture.wrapMode = TextureWrapMode.Repeat;
            ApplyMaterials(renderer, atlasTexture, mesh);
        }

        private static void ApplyFallbackMaterial(MeshRenderer renderer, Mesh? mesh)
        {
            if (renderer == null) return;
            
            ApplyMaterials(renderer, null, mesh);
        }

        private static void ApplyMaterials(MeshRenderer renderer, Texture2D? atlasTexture, Mesh? mesh)
        {
            var subMeshCount = mesh?.subMeshCount ?? 1;
            var materials = GetMaterialsForSubMeshes(atlasTexture, subMeshCount);
            if (materials.Length == 1)
            {
                renderer.sharedMaterial = materials[0];
            }
            else
            {
                renderer.sharedMaterials = materials;
            }
        }

        private static Material[] GetMaterialsForSubMeshes(Texture2D? atlasTexture, int subMeshCount)
        {
            if (subMeshCount <= 1)
            {
                return new[] { CreateCutoutMaterial(atlasTexture) };
            }

            if (subMeshCount == 2)
            {
                return new[]
                {
                    CreateCutoutMaterial(atlasTexture),
                    CreateDoubleSidedCutoutMaterial(atlasTexture)
                };
            }

            return new[]
            {
                CreateCutoutMaterial(atlasTexture),
                CreateDoubleSidedCutoutMaterial(atlasTexture),
                CreateTransparentMaterial(atlasTexture)
            };
        }
    }
}