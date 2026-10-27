using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace ShapeMerge
{
    /// <summary>A dynamic mesh that is cleared and rebuilt from coloured triangles every frame.</summary>
    public sealed class MeshLayer
    {
        public readonly MeshRenderer Renderer;

        readonly Mesh mesh;
        readonly List<Vector3> positions = new List<Vector3>(4096);
        readonly List<Color> colors = new List<Color>(4096);
        readonly List<Vector2> uvs = new List<Vector2>(4096);
        readonly List<int> indices = new List<int>(8192);

        public MeshLayer(string name, Material material, int sortingOrder, Transform parent)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            mesh = new Mesh { name = name, indexFormat = IndexFormat.UInt32 };
            mesh.MarkDynamic();
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            Renderer = go.AddComponent<MeshRenderer>();
            Renderer.sharedMaterial = material;
            Renderer.sortingOrder = sortingOrder;
            Renderer.shadowCastingMode = ShadowCastingMode.Off;
            Renderer.receiveShadows = false;
            Renderer.lightProbeUsage = LightProbeUsage.Off;
            Renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
        }

        public int VertexCount => positions.Count;

        public void Clear()
        {
            positions.Clear();
            colors.Clear();
            uvs.Clear();
            indices.Clear();
        }

        public int Vertex(Vector2 p, Color c)
        {
            positions.Add(new Vector3(p.x, p.y, 0f));
            colors.Add(c);
            uvs.Add(Vector2.zero);
            return positions.Count - 1;
        }

        public int Vertex(Vector2 p, Color c, Vector2 uv)
        {
            positions.Add(new Vector3(p.x, p.y, 0f));
            colors.Add(c);
            uvs.Add(uv);
            return positions.Count - 1;
        }

        public void Triangle(int a, int b, int c)
        {
            indices.Add(a);
            indices.Add(b);
            indices.Add(c);
        }

        public void Quad(int a, int b, int c, int d)
        {
            Triangle(a, b, c);
            Triangle(a, c, d);
        }

        public void Apply()
        {
            mesh.Clear();
            if (positions.Count == 0) return;
            mesh.SetVertices(positions);
            mesh.SetColors(colors);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(indices, 0, false);
            mesh.bounds = new Bounds(Vector3.zero, new Vector3(10000f, 10000f, 10f));
        }
    }

    /// <summary>The three stacked passes every soft shape is painted in.</summary>
    public sealed class ShapeLayers
    {
        public readonly MeshLayer Shadow, Outline, Fill;

        public ShapeLayers(string name, Material material, int baseOrder, Transform parent)
        {
            Shadow = new MeshLayer(name + " Shadow", material, baseOrder, parent);
            Outline = new MeshLayer(name + " Outline", material, baseOrder + 1, parent);
            Fill = new MeshLayer(name + " Fill", material, baseOrder + 2, parent);
        }

        public void Clear()
        {
            Shadow.Clear();
            Outline.Clear();
            Fill.Clear();
        }

        public void Apply()
        {
            Shadow.Apply();
            Outline.Apply();
            Fill.Apply();
        }
    }
}
