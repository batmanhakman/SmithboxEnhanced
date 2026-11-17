using System;
using System.Collections.Generic;
using System.Numerics;

namespace StudioCore.Editors.HavokEditor;

/// <summary>
/// Detailed collision triangle data extracted from a 3D model.
/// </summary>
public struct ModelCollisionTriangle
{
    public int A;
    public int B;
    public int C;
    public uint MaterialId;
    public Vector3 Normal;
    public int SectionIndex;

    public ModelCollisionTriangle(int a, int b, int c, uint materialId, Vector3 normal, int sectionIndex = 0)
    {
        A = a;
        B = b;
        C = c;
        MaterialId = materialId;
        Normal = normal;
        SectionIndex = sectionIndex;
    }
}

/// <summary>
/// Represents an extracted mesh section or submesh within the collision geometry.
/// </summary>
public sealed class ModelCollisionSection
{
    public int SectionIndex { get; set; }
    public string Name { get; set; } = "";
    public string MaterialName { get; set; } = "";
    public uint MaterialId { get; set; }
    public int VertexStart { get; set; }
    public int VertexCount { get; set; }
    public int TriangleStart { get; set; }
    public int TriangleCount { get; set; }
    public Vector3 MinBounds { get; set; }
    public Vector3 MaxBounds { get; set; }
}

/// <summary>
/// Statistics and validation reporting from the model-to-collision extraction.
/// </summary>
public sealed class ModelCollisionStats
{
    public int SourceVerticesCount { get; set; }
    public int SourceTrianglesCount { get; set; }
    public int ExtractedVerticesCount { get; set; }
    public int ExtractedTrianglesCount { get; set; }
    public int DegenerateTrianglesCount { get; set; }
    public int DuplicateVerticesWeldedCount { get; set; }
    public int InvalidVerticesCount { get; set; }
    public int InvalidTrianglesCount { get; set; }
    public int SectionsProcessedCount { get; set; }
    public int PartsProcessedCount { get; set; }

    public override string ToString()
    {
        return $"Vertices: {ExtractedVerticesCount} (welded: {DuplicateVerticesWeldedCount}), " +
               $"Triangles: {ExtractedTrianglesCount} (degenerates removed: {DegenerateTrianglesCount}, invalid removed: {InvalidTrianglesCount})";
    }
}

/// <summary>
/// Fully independent geometric collision representation extracted from a 3D model.
/// Contains clean, validated vertices, triangle indices, surface materials, section definitions,
/// and spatial bounding information.
/// </summary>
public sealed class ModelCollisionGeometry
{
    public List<Vector3> Vertices { get; set; } = new();
    public List<int> Indices { get; set; } = new();
    public List<uint> TriangleMaterials { get; set; } = new();
    public List<ModelCollisionTriangle> Triangles { get; set; } = new();
    public List<ModelCollisionSection> Sections { get; set; } = new();
    public ModelCollisionStats Stats { get; set; } = new();

    public Vector3 MinBounds { get; set; } = new(float.MaxValue);
    public Vector3 MaxBounds { get; set; } = new(float.MinValue);

    public int VertexCount => Vertices.Count;
    public int TriangleCount => Indices.Count / 3;

    public void UpdateBounds()
    {
        if (Vertices.Count == 0)
        {
            MinBounds = Vector3.Zero;
            MaxBounds = Vector3.Zero;
            return;
        }

        var min = new Vector3(float.MaxValue);
        var max = new Vector3(float.MinValue);

        foreach (var v in Vertices)
        {
            min = Vector3.Min(min, v);
            max = Vector3.Max(max, v);
        }

        MinBounds = min;
        MaxBounds = max;
    }
}
