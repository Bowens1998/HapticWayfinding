using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace HapticWayfinding.EditorTools
{
    /// <summary>
    /// Generates everything the realistic scene needs without downloading assets:
    /// tileable PBR textures (albedo + normal) from procedural noise, URP Lit materials,
    /// and box meshes with world-scale UVs (textures keep their real size on any wall)
    /// plus lightmap UVs for baked lighting.
    /// </summary>
    public static class AssetFactory
    {
        public const string Root = "Assets/HapticWayfinding/Generated";
        const string TexDir = Root + "/Textures";
        const string MatDir = Root + "/Materials";
        const string MeshDir = Root + "/Meshes";

        public static void EnsureFolders()
        {
            foreach (var d in new[] { Root, TexDir, MatDir, MeshDir }) Directory.CreateDirectory(d);
            AssetDatabase.Refresh();
        }

        // ------------------------------------------------------------------ noise
        static float Hash(int x, int y, int seed)
        {
            unchecked
            {
                uint h = (uint)(x * 374761393 + y * 668265263 + seed * 2147483647);
                h = (h ^ (h >> 13)) * 1274126177u;
                h ^= h >> 16;
                return (h & 0xFFFFFF) / (float)0xFFFFFF;
            }
        }

        /// <summary>Tileable value noise: lattice wraps every 'period' cells.</summary>
        static float ValueNoise(float u, float v, int period, int seed)
        {
            float x = u * period, y = v * period;
            int x0 = Mathf.FloorToInt(x), y0 = Mathf.FloorToInt(y);
            float fx = x - x0, fy = y - y0;
            fx = fx * fx * (3 - 2 * fx);
            fy = fy * fy * (3 - 2 * fy);
            int Wrap(int a) => ((a % period) + period) % period;
            float a00 = Hash(Wrap(x0), Wrap(y0), seed), a10 = Hash(Wrap(x0 + 1), Wrap(y0), seed);
            float a01 = Hash(Wrap(x0), Wrap(y0 + 1), seed), a11 = Hash(Wrap(x0 + 1), Wrap(y0 + 1), seed);
            return Mathf.Lerp(Mathf.Lerp(a00, a10, fx), Mathf.Lerp(a01, a11, fx), fy);
        }

        static float Fbm(float u, float v, int basePeriod, int octaves, int seed, float gain = 0.5f)
        {
            float sum = 0, amp = 1, norm = 0;
            int p = basePeriod;
            for (int o = 0; o < octaves; o++)
            {
                sum += ValueNoise(u, v, p, seed + o * 31) * amp;
                norm += amp;
                amp *= gain;
                p *= 2;
            }
            return sum / norm;
        }

        // ------------------------------------------------------------------ texture output
        static Texture2D SaveTexture(string name, int size, Func<float, float, Color> albedo, bool srgb = true)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            var px = new Color[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                    px[y * size + x] = albedo((x + 0.5f) / size, (y + 0.5f) / size);
            tex.SetPixels(px);
            return WritePng(name, tex, false, srgb);
        }

        static Texture2D SaveNormalFromHeight(string name, int size, Func<float, float, float> height, float strength)
        {
            var h = new float[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                    h[y * size + x] = height((x + 0.5f) / size, (y + 0.5f) / size);
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            var px = new Color[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float hl = h[y * size + (x + size - 1) % size], hr = h[y * size + (x + 1) % size];
                    float hd = h[((y + size - 1) % size) * size + x], hu = h[((y + 1) % size) * size + x];
                    var n = new Vector3((hl - hr) * strength, (hd - hu) * strength, 1f).normalized;
                    px[y * size + x] = new Color(n.x * 0.5f + 0.5f, n.y * 0.5f + 0.5f, n.z * 0.5f + 0.5f, 1f);
                }
            }
            tex.SetPixels(px);
            return WritePng(name, tex, true, false);
        }

        static Texture2D WritePng(string name, Texture2D tex, bool normal, bool srgb)
        {
            string path = TexDir + "/" + name + ".png";
            File.WriteAllBytes(path, tex.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            var imp = (TextureImporter)AssetImporter.GetAtPath(path);
            imp.textureType = normal ? TextureImporterType.NormalMap : TextureImporterType.Default;
            imp.sRGBTexture = srgb;
            imp.wrapMode = TextureWrapMode.Repeat;
            imp.mipmapEnabled = true;
            imp.anisoLevel = 8;
            imp.filterMode = FilterMode.Trilinear;
            imp.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        // ------------------------------------------------------------------ texture set
        public class TexSet { public Texture2D albedo, normal; }
        public static readonly Dictionary<string, TexSet> Tex = new Dictionary<string, TexSet>();

        public static void GenerateTextures()
        {
            EnsureFolders();
            Tex.Clear();

            // Polished concrete floor (one texture = 2 m): broad mottling, fine grain, sparse aggregate flecks.
            Func<float, float, float> concreteH = (u, v) =>
                Fbm(u, v, 8, 5, 11) * 0.6f + Fbm(u, v, 64, 3, 12) * 0.4f + (Hash((int)(u * 512), (int)(v * 512), 13) > 0.985f ? 0.5f : 0f);
            Tex["concrete"] = new TexSet
            {
                albedo = SaveTexture("concrete_albedo", 1024, (u, v) =>
                {
                    float m = Fbm(u, v, 4, 5, 1);
                    float g = Fbm(u, v, 64, 2, 2);
                    float fleck = Hash((int)(u * 1024), (int)(v * 1024), 3);
                    float k = 0.50f + (m - 0.5f) * 0.16f + (g - 0.5f) * 0.05f;
                    if (fleck > 0.992f) k += 0.12f; else if (fleck < 0.006f) k -= 0.10f;
                    return new Color(k * 1.0f, k * 0.985f, k * 0.955f);
                }),
                normal = SaveNormalFromHeight("concrete_normal", 1024, concreteH, 1.2f)
            };

            // Painted plaster wall (1 m): almost flat, faint roller texture.
            Tex["plaster"] = new TexSet
            {
                albedo = SaveTexture("plaster_albedo", 512, (u, v) =>
                {
                    float n = Fbm(u, v, 8, 4, 21);
                    float k = 0.86f + (n - 0.5f) * 0.03f;
                    return new Color(k, k * 0.985f, k * 0.95f);
                }),
                normal = SaveNormalFromHeight("plaster_normal", 512, (u, v) => Fbm(u, v, 32, 4, 22) + Fbm(u, v, 128, 2, 23) * 0.5f, 2.5f)
            };

            // Acoustic ceiling tile (one texture = one 0.6 m tile): fissured surface + recessed grid edge.
            Func<float, float, float> tileH = (u, v) =>
            {
                float edge = Mathf.Min(Mathf.Min(u, 1 - u), Mathf.Min(v, 1 - v));
                float groove = edge < 0.012f ? -1f : 0f;
                float pits = Hash((int)(u * 256), (int)(v * 256), 31) > 0.93f ? -0.6f : 0f;
                return groove + pits + Fbm(u, v, 16, 3, 32) * 0.3f;
            };
            Tex["ceiling"] = new TexSet
            {
                albedo = SaveTexture("ceiling_albedo", 256, (u, v) =>
                {
                    float edge = Mathf.Min(Mathf.Min(u, 1 - u), Mathf.Min(v, 1 - v));
                    float k = edge < 0.012f ? 0.72f : 0.90f + (Fbm(u, v, 16, 3, 33) - 0.5f) * 0.04f;
                    if (Hash((int)(u * 256), (int)(v * 256), 31) > 0.93f) k -= 0.08f;
                    return new Color(k, k, k * 0.98f);
                }),
                normal = SaveNormalFromHeight("ceiling_normal", 256, tileH, 1.5f)
            };

            // Oak veneer (1 m): grain lines along U with warped rings.
            Func<float, float, float> grain = (u, v) =>
            {
                float warp = Fbm(u, v, 4, 4, 41) * 6f;
                float rings = Mathf.Sin((v * 40f + warp) * Mathf.PI);
                return rings * 0.5f + 0.5f;
            };
            Tex["wood"] = new TexSet
            {
                albedo = SaveTexture("wood_albedo", 512, (u, v) =>
                {
                    float r = grain(u, v);
                    float fine = Fbm(u * 0.25f, v * 4f, 64, 2, 42);
                    float k = 0.55f + r * 0.12f + (fine - 0.5f) * 0.1f;
                    return new Color(k * 0.80f, k * 0.58f, k * 0.36f);
                }),
                normal = SaveNormalFromHeight("wood_normal", 512, (u, v) => grain(u, v) * 0.3f, 1.0f)
            };

            // Woven fabric (one texture = 0.1 m): clothes, chair upholstery.
            Func<float, float, float> weave = (u, v) =>
            {
                float a = Mathf.Sin(u * Mathf.PI * 2f * 48f), b = Mathf.Sin(v * Mathf.PI * 2f * 48f);
                return a * b * 0.5f + Fbm(u, v, 32, 2, 51) * 0.5f;
            };
            Tex["fabric"] = new TexSet
            {
                albedo = SaveTexture("fabric_albedo", 256, (u, v) =>
                {
                    float k = 0.85f + weave(u, v) * 0.1f;
                    return new Color(k, k, k);
                }),
                normal = SaveNormalFromHeight("fabric_normal", 256, weave, 3f)
            };

            // Brushed metal (0.5 m): streaks along U.
            Tex["brushed"] = new TexSet
            {
                albedo = SaveTexture("brushed_albedo", 256, (u, v) =>
                {
                    float k = 0.92f + (ValueNoise(u * 0.02f, v, 128, 61) - 0.5f) * 0.025f;
                    return new Color(k, k, k);
                }),
                normal = SaveNormalFromHeight("brushed_normal", 256, (u, v) => ValueNoise(u * 0.02f, v, 128, 62), 0.25f)
            };
        }

        // ------------------------------------------------------------------ materials
        static Shader litShader;

        public static Material Lit(string name, Color color, string tex = null, float tileMeters = 1f,
            float smoothness = 0.3f, float metallic = 0f, float normalScale = 1f, Color? emission = null)
        {
            if (litShader == null) litShader = Shader.Find("Universal Render Pipeline/Lit");
            string path = MatDir + "/" + name + ".mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null)
            {
                m = new Material(litShader);
                AssetDatabase.CreateAsset(m, path);
            }
            m.shader = litShader;
            m.SetColor("_BaseColor", color);
            m.SetFloat("_Smoothness", smoothness);
            m.SetFloat("_Metallic", metallic);
            if (tex != null && Tex.TryGetValue(tex, out var set))
            {
                m.SetTexture("_BaseMap", set.albedo);
                m.SetTexture("_BumpMap", set.normal);
                m.SetFloat("_BumpScale", normalScale);
                var scale = new Vector2(1f / tileMeters, 1f / tileMeters);
                m.SetTextureScale("_BaseMap", scale);
                m.SetTextureScale("_BumpMap", scale);
            }
            if (emission.HasValue)
            {
                m.SetColor("_EmissionColor", emission.Value);
                m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.BakedEmissive;
            }
            else
            {
                m.SetColor("_EmissionColor", Color.black);
                m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.EmissiveIsBlack;
            }
            m.SetFloat("_Surface", 0f);
            m.enableInstancing = true;
            BaseShaderGUI.SetMaterialKeywords(m, UnityEditor.Rendering.Universal.ShaderGUI.LitGUI.SetMaterialKeywords);
            EditorUtility.SetDirty(m);
            return m;
        }

        public static Material Glass(string name, Color tint, float alpha, float smoothness = 0.96f)
        {
            var m = Lit(name, new Color(tint.r, tint.g, tint.b, alpha), null, 1f, smoothness, 0f);
            m.SetFloat("_Surface", 1f); // transparent
            m.SetFloat("_Blend", 0f);   // alpha
            m.SetFloat("_Cull", (float)CullMode.Off);
            // Reflections fade with alpha, otherwise thin glass reads as a grey veil from inside the booth.
            m.SetFloat("_BlendModePreserveSpecular", 0f);
            BaseShaderGUI.SetMaterialKeywords(m, UnityEditor.Rendering.Universal.ShaderGUI.LitGUI.SetMaterialKeywords);
            m.renderQueue = (int)RenderQueue.Transparent;
            EditorUtility.SetDirty(m);
            return m;
        }

        public static Material Unlit(string name, Color color)
        {
            string path = MatDir + "/" + name + ".mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            var shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (m == null)
            {
                m = new Material(shader);
                AssetDatabase.CreateAsset(m, path);
            }
            m.shader = shader;
            m.SetColor("_BaseColor", color);
            EditorUtility.SetDirty(m);
            return m;
        }

        // ------------------------------------------------------------------ meshes
        static readonly Dictionary<string, Mesh> meshCache = new Dictionary<string, Mesh>();

        /// <summary>Box mesh of the given size (m) with UVs in metres and generated lightmap UVs.</summary>
        public static Mesh BoxMesh(Vector3 size)
        {
            string key = $"box_{size.x:0.###}x{size.y:0.###}x{size.z:0.###}".Replace(',', '.');
            if (meshCache.TryGetValue(key, out var cached) && cached != null) return cached;
            string path = MeshDir + "/" + key + ".asset";
            var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (existing != null) { meshCache[key] = existing; return existing; }

            Vector3 h = size * 0.5f;
            var verts = new List<Vector3>();
            var norms = new List<Vector3>();
            var uvs = new List<Vector2>();
            var tris = new List<int>();
            void Face(Vector3 n, Vector3 u, Vector3 v, float du, float dv)
            {
                int i = verts.Count;
                Vector3 c = Vector3.Scale(n, h);
                Vector3 hu = u * (du * 0.5f), hv = v * (dv * 0.5f);
                verts.Add(c - hu - hv); verts.Add(c + hu - hv); verts.Add(c + hu + hv); verts.Add(c - hu + hv);
                for (int k = 0; k < 4; k++) norms.Add(n);
                uvs.Add(new Vector2(0, 0)); uvs.Add(new Vector2(du, 0)); uvs.Add(new Vector2(du, dv)); uvs.Add(new Vector2(0, dv));
                // u x v == n for every face, so (0,1,2)(0,2,3) is clockwise seen from outside (Unity front face).
                tris.AddRange(new[] { i, i + 1, i + 2, i, i + 2, i + 3 });
            }
            Face(Vector3.up, Vector3.right, Vector3.back, size.x, size.z);
            Face(Vector3.down, Vector3.right, Vector3.forward, size.x, size.z);
            Face(Vector3.forward, Vector3.right, Vector3.up, size.x, size.y);
            Face(Vector3.back, Vector3.left, Vector3.up, size.x, size.y);
            Face(Vector3.right, Vector3.back, Vector3.up, size.z, size.y);
            Face(Vector3.left, Vector3.forward, Vector3.up, size.z, size.y);

            var mesh = new Mesh { name = key };
            mesh.SetVertices(verts);
            mesh.SetNormals(norms);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateTangents();
            mesh.RecalculateBounds();
            // Wider margin between charts than the default, so bilinear lookups never bleed into a neighbour chart.
            UnwrapParam.SetDefaults(out var unwrap);
            unwrap.packMargin = 0.03f;
            Unwrapping.GenerateSecondaryUVSet(mesh, unwrap);
            AssetDatabase.CreateAsset(mesh, path);
            meshCache[key] = mesh;
            return mesh;
        }

        public static void ClearMeshCache() => meshCache.Clear();
    }
}
