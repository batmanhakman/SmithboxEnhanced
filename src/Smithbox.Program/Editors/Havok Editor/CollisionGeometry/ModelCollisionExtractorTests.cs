using SoulsFormats;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Numerics;

namespace StudioCore.Editors.HavokEditor;

/// <summary>
/// Comprehensive automated test suite for Phase 2 Model-to-Collision Geometry extraction.
/// Validates extraction, transformations, mesh sections, multi-part assemblies,
/// invalid geometry rejection, degenerate detection, vertex deduplication, and surface assignments.
/// </summary>
public static class ModelCollisionExtractorTests
{
    public static void RunAllTests()
    {
        Console.WriteLine("[Phase 2 Tests] Starting ModelCollisionExtractor test suite...");

        TestVertexAndIndexExtraction();
        TestNodeTransformations();
        TestObjectTransformAndWindingFlip();
        TestMultipleMeshSections();
        TestMultipleModelParts();
        TestInvalidGeometryRemoval();
        TestDegenerateTriangleDetection();
        TestDuplicateVertexWelding();
        TestSurfaceMaterialAssignments();
        TestFlver0Extraction();
        TestSourceStatisticsAndInvalidMaterialIndex();

        Console.WriteLine("[Phase 2 Tests] All Phase 2 tests passed successfully!");
    }

    /// <summary>
    /// Requirement 2 & 3: Extract vertex positions and triangle indices.
    /// </summary>
    public static void TestVertexAndIndexExtraction()
    {
        var flver = CreateTestFlver2();
        var options = new ModelCollisionExtractionOptions
        {
            ApplyNodeTransforms = false,
            WeldVertices = false
        };

        var geom = ModelCollisionExtractor.ExtractFromFlver(flver, options);

        Debug.Assert(geom.Vertices.Count == 4, $"Expected 4 vertices, got {geom.Vertices.Count}");
        Debug.Assert(geom.Indices.Count == 6, $"Expected 6 indices (2 triangles), got {geom.Indices.Count}");
        Debug.Assert(geom.TriangleCount == 2, $"Expected 2 triangles, got {geom.TriangleCount}");
        Console.WriteLine("  ✓ TestVertexAndIndexExtraction passed");
    }

    /// <summary>
    /// Requirement 4: Apply correct node / bone transformations.
    /// </summary>
    public static void TestNodeTransformations()
    {
        var flver = CreateTestFlver2();
        // Add a node with translation (10, 20, 30)
        var node = new FLVER.Node
        {
            Name = "Bone_Test",
            Translation = new Vector3(10, 20, 30),
            Rotation = Vector3.Zero,
            Scale = Vector3.One,
            ParentIndex = -1
        };
        flver.Nodes.Add(node);
        flver.Meshes[0].NodeIndex = 0;

        var options = new ModelCollisionExtractionOptions
        {
            ApplyNodeTransforms = true,
            WeldVertices = false
        };

        var geom = ModelCollisionExtractor.ExtractFromFlver(flver, options);

        // Vertex 0 was (0, 0, 0), after transform it must be (10, 20, 30)
        var v0 = geom.Vertices[0];
        Debug.Assert(MathF.Abs(v0.X - 10f) < 0.001f &&
                     MathF.Abs(v0.Y - 20f) < 0.001f &&
                     MathF.Abs(v0.Z - 30f) < 0.001f,
                     $"Vertex 0 was not transformed correctly by node matrix: {v0}");

        Console.WriteLine("  ✓ TestNodeTransformations passed");
    }

    /// <summary>
    /// Requirement 4: Apply correct object transformations and winding flip on reflection.
    /// </summary>
    public static void TestObjectTransformAndWindingFlip()
    {
        var flver = CreateTestFlver2();
        // Reflection transform (Scale X by -1) -> determinant < 0
        var mirrorTransform = Matrix4x4.CreateScale(-1f, 1f, 1f);

        var options = new ModelCollisionExtractionOptions
        {
            ApplyNodeTransforms = false,
            ObjectTransform = mirrorTransform,
            WeldVertices = false
        };

        var geom = ModelCollisionExtractor.ExtractFromFlver(flver, options);

        // Vertex X positions must be negated
        var v1 = geom.Vertices[1]; // was (1, 0, 0)
        Debug.Assert(MathF.Abs(v1.X - (-1f)) < 0.001f, $"Expected X = -1 after reflection, got {v1.X}");

        // Normal must still point in positive Y direction due to automatic winding flip
        var normal = geom.Triangles[0].Normal;
        Debug.Assert(normal.Y > 0f, $"Expected positive normal Y after winding flip, got {normal.Y}");

        Console.WriteLine("  ✓ TestObjectTransformAndWindingFlip passed");
    }

    /// <summary>
    /// Requirement 5: Handle multiple mesh sections and selective extraction.
    /// </summary>
    public static void TestMultipleMeshSections()
    {
        var flver = CreateTestFlver2();

        // Add second mesh
        var mesh2 = new FLVER2.Mesh
        {
            MaterialIndex = 0,
            NodeIndex = -1
        };
        mesh2.Vertices.Add(new FLVER.Vertex { Position = new Vector3(0, 5, 0) });
        mesh2.Vertices.Add(new FLVER.Vertex { Position = new Vector3(1, 5, 0) });
        mesh2.Vertices.Add(new FLVER.Vertex { Position = new Vector3(0, 5, 1) });
        mesh2.FaceSets.Add(new FLVER2.FaceSet
        {
            Flags = FLVER2.FaceSet.FSFlags.None,
            Indices = new List<int> { 0, 1, 2 }
        });
        flver.Meshes.Add(mesh2);

        // Test extracting all sections
        var allGeom = ModelCollisionExtractor.ExtractFromFlver(flver);
        Debug.Assert(allGeom.Sections.Count == 2, $"Expected 2 sections, got {allGeom.Sections.Count}");
        Debug.Assert(allGeom.TriangleCount == 3, $"Expected 3 triangles total, got {allGeom.TriangleCount}");

        // Test selective extraction (only mesh 1)
        var sec1Options = new ModelCollisionExtractionOptions
        {
            SelectedMeshIndices = new List<int> { 1 }
        };
        var sec1Geom = ModelCollisionExtractor.ExtractFromFlver(flver, sec1Options);
        Debug.Assert(sec1Geom.Sections.Count == 1, $"Expected 1 section, got {sec1Geom.Sections.Count}");
        Debug.Assert(sec1Geom.TriangleCount == 1, $"Expected 1 triangle, got {sec1Geom.TriangleCount}");

        Console.WriteLine("  ✓ TestMultipleMeshSections passed");
    }

    /// <summary>
    /// Requirement 6: Handle multiple model parts with individual transforms.
    /// </summary>
    public static void TestMultipleModelParts()
    {
        var part1Flver = CreateTestFlver2();
        var part2Flver = CreateTestFlver2();

        var parts = new List<ModelPartSource>
        {
            new ModelPartSource
            {
                Name = "PartA",
                Flver2 = part1Flver,
                Transform = Matrix4x4.Identity
            },
            new ModelPartSource
            {
                Name = "PartB",
                Flver2 = part2Flver,
                Transform = Matrix4x4.CreateTranslation(0, 10, 0),
                OverrideMaterialId = 42
            }
        };

        var geom = ModelCollisionExtractor.ExtractFromParts(parts);

        Debug.Assert(geom.Stats.PartsProcessedCount == 2, $"Expected 2 parts processed, got {geom.Stats.PartsProcessedCount}");
        Debug.Assert(geom.TriangleCount == 4, $"Expected 4 triangles, got {geom.TriangleCount}");

        // Verify part B override material
        Debug.Assert(geom.TriangleMaterials[2] == 42, $"Expected material 42 for PartB, got {geom.TriangleMaterials[2]}");
        Debug.Assert(geom.TriangleMaterials[3] == 42, $"Expected material 42 for PartB, got {geom.TriangleMaterials[3]}");

        Console.WriteLine("  ✓ TestMultipleModelParts passed");
    }

    /// <summary>
    /// Requirement 7: Remove invalid geometry (NaN, Infinity, out-of-bounds).
    /// </summary>
    public static void TestInvalidGeometryRemoval()
    {
        var flver = CreateTestFlver2();

        // Add a triangle with NaN coordinate
        flver.Meshes[0].Vertices.Add(new FLVER.Vertex { Position = new Vector3(float.NaN, 0, 0) });
        flver.Meshes[0].Vertices.Add(new FLVER.Vertex { Position = new Vector3(1, 0, 0) });
        flver.Meshes[0].Vertices.Add(new FLVER.Vertex { Position = new Vector3(0, 0, 1) });
        flver.Meshes[0].FaceSets[0].Indices.AddRange(new[] { 4, 5, 6 });

        // Add a triangle with Infinity coordinate
        flver.Meshes[0].Vertices.Add(new FLVER.Vertex { Position = new Vector3(float.PositiveInfinity, 0, 0) });
        flver.Meshes[0].Vertices.Add(new FLVER.Vertex { Position = new Vector3(1, 0, 0) });
        flver.Meshes[0].Vertices.Add(new FLVER.Vertex { Position = new Vector3(0, 0, 1) });
        flver.Meshes[0].FaceSets[0].Indices.AddRange(new[] { 7, 8, 9 });

        var options = new ModelCollisionExtractionOptions
        {
            ApplyNodeTransforms = false,
            RemoveInvalidGeometry = true
        };

        var geom = ModelCollisionExtractor.ExtractFromFlver(flver, options);

        // The 2 invalid triangles must be rejected; only the original 2 valid triangles remain
        Debug.Assert(geom.TriangleCount == 2, $"Expected 2 valid triangles, got {geom.TriangleCount}");
        Debug.Assert(geom.Stats.InvalidTrianglesCount == 2, $"Expected 2 invalid triangles recorded, got {geom.Stats.InvalidTrianglesCount}");

        Console.WriteLine("  ✓ TestInvalidGeometryRemoval passed");
    }

    /// <summary>
    /// Requirement 8: Detect and remove degenerate triangles.
    /// </summary>
    public static void TestDegenerateTriangleDetection()
    {
        // 1. Coincident corners test
        var p0 = new Vector3(0, 0, 0);
        var p1 = new Vector3(0, 0, 0);
        var p2 = new Vector3(1, 0, 0);
        Debug.Assert(ModelCollisionExtractor.IsDegenerateTriangle(p0, p1, p2), "Coincident corner triangle was not detected as degenerate!");

        // 2. Collinear points test
        var c0 = new Vector3(0, 0, 0);
        var c1 = new Vector3(1, 0, 0);
        var c2 = new Vector3(2, 0, 0);
        Debug.Assert(ModelCollisionExtractor.IsDegenerateTriangle(c0, c1, c2), "Collinear triangle was not detected as degenerate!");

        // 3. Valid triangle test
        var v0 = new Vector3(0, 0, 0);
        var v1 = new Vector3(1, 0, 0);
        var v2 = new Vector3(0, 1, 0);
        Debug.Assert(!ModelCollisionExtractor.IsDegenerateTriangle(v0, v1, v2), "Valid triangle was falsely marked as degenerate!");

        // 4. In extraction pipeline
        var flver = CreateTestFlver2();
        // Add degenerate face (all 3 indices point to vertex 0)
        flver.Meshes[0].FaceSets[0].Indices.AddRange(new[] { 0, 0, 0 });
        // Add collinear face
        flver.Meshes[0].Vertices.Add(new FLVER.Vertex { Position = new Vector3(10, 0, 0) });
        flver.Meshes[0].Vertices.Add(new FLVER.Vertex { Position = new Vector3(20, 0, 0) });
        flver.Meshes[0].Vertices.Add(new FLVER.Vertex { Position = new Vector3(30, 0, 0) });
        flver.Meshes[0].FaceSets[0].Indices.AddRange(new[] { 4, 5, 6 });

        var options = new ModelCollisionExtractionOptions
        {
            ApplyNodeTransforms = false,
            RemoveDegenerateTriangles = true
        };

        var geom = ModelCollisionExtractor.ExtractFromFlver(flver, options);
        Debug.Assert(geom.TriangleCount == 2, $"Expected 2 valid triangles, got {geom.TriangleCount}");
        Debug.Assert(geom.Stats.DegenerateTrianglesCount >= 2, $"Expected at least 2 degenerates removed, got {geom.Stats.DegenerateTrianglesCount}");

        Console.WriteLine("  ✓ TestDegenerateTriangleDetection passed");
    }

    /// <summary>
    /// Requirement 9: Deduplicate vertices within tolerance.
    /// </summary>
    public static void TestDuplicateVertexWelding()
    {
        var flver = CreateTestFlver2();

        // Original flver has 4 vertices for 2 triangles sharing an edge (verts 0 and 2)
        // With welding enabled, vertex count must be 4
        var weldedOptions = new ModelCollisionExtractionOptions
        {
            ApplyNodeTransforms = false,
            WeldVertices = true,
            WeldTolerance = 0.001f
        };
        var weldedGeom = ModelCollisionExtractor.ExtractFromFlver(flver, weldedOptions);
        Debug.Assert(weldedGeom.Vertices.Count == 4, $"Expected 4 vertices with welding, got {weldedGeom.Vertices.Count}");
        Debug.Assert(weldedGeom.Stats.DuplicateVerticesWeldedCount == 2, $"Expected 2 duplicate corners welded, got {weldedGeom.Stats.DuplicateVerticesWeldedCount}");

        // With welding disabled, all 6 triangle corners are separate vertices
        var unweldedOptions = new ModelCollisionExtractionOptions
        {
            ApplyNodeTransforms = false,
            WeldVertices = false
        };
        var unweldedGeom = ModelCollisionExtractor.ExtractFromFlver(flver, unweldedOptions);
        Debug.Assert(unweldedGeom.Vertices.Count == 6, $"Expected 6 vertices without welding, got {unweldedGeom.Vertices.Count}");

        Console.WriteLine("  ✓ TestDuplicateVertexWelding passed");
    }

    /// <summary>
    /// Requirement 10: Preserve relevant surface assignments.
    /// </summary>
    public static void TestSurfaceMaterialAssignments()
    {
        var flver = CreateTestFlver2();
        flver.Materials[0].Name = "hkm_Stone_Slide1"; // Stone (2) + Slide1 (400) = 402

        var options = new ModelCollisionExtractionOptions
        {
            ApplyNodeTransforms = false
        };

        var geom = ModelCollisionExtractor.ExtractFromFlver(flver, options);

        Debug.Assert(geom.TriangleMaterials.Count == 2, "Expected 2 triangle materials");
        Debug.Assert(geom.TriangleMaterials[0] == 402, $"Expected material 402, got {geom.TriangleMaterials[0]}");
        Debug.Assert(geom.TriangleMaterials[1] == 402, $"Expected material 402, got {geom.TriangleMaterials[1]}");
        Debug.Assert(geom.Sections[0].MaterialName == "hkm_Stone_Slide1", "Expected section material name preserved");

        Console.WriteLine("  ✓ TestSurfaceMaterialAssignments passed");
    }

    /// <summary>
    /// FLVER0 model extraction support.
    /// </summary>
    public static void TestFlver0Extraction()
    {
        var flver0 = new FLVER0();
        flver0.Header.Version = 0x15;
        flver0.Materials.Add(new FLVER0.Material { Name = "hkm_Wood" }); // Wood = 4

        var mesh = new FLVER0.Mesh
        {
            MaterialIndex = 0,
            NodeIndex = -1
        };
        mesh.Vertices.Add(new FLVER.Vertex { Position = new Vector3(0, 0, 0) });
        mesh.Vertices.Add(new FLVER.Vertex { Position = new Vector3(1, 0, 0) });
        mesh.Vertices.Add(new FLVER.Vertex { Position = new Vector3(0, 1, 0) });
        mesh.Indices = new List<int> { 0, 1, 2 };
        flver0.Meshes.Add(mesh);

        var geom = ModelCollisionExtractor.ExtractFromFlver0(flver0);

        Debug.Assert(geom.Vertices.Count == 3, $"Expected 3 vertices from FLVER0, got {geom.Vertices.Count}");
        Debug.Assert(geom.TriangleCount == 1, $"Expected 1 triangle from FLVER0, got {geom.TriangleCount}");
        Debug.Assert(geom.TriangleMaterials[0] == 4, $"Expected material 4 (Wood) from FLVER0, got {geom.TriangleMaterials[0]}");

        Console.WriteLine("  ✓ TestFlver0Extraction passed");
    }

    /// <summary>
    /// Source statistics must describe the selected source meshes, even when vertices are welded,
    /// and an invalid material index must fall back to the configured default material.
    /// </summary>
    public static void TestSourceStatisticsAndInvalidMaterialIndex()
    {
        var flver = CreateTestFlver2();
        flver.Meshes[0].MaterialIndex = -1;

        var geom = ModelCollisionExtractor.ExtractFromFlver(flver, new ModelCollisionExtractionOptions
        {
            ApplyNodeTransforms = false,
            DefaultMaterialId = 99
        });

        Debug.Assert(geom.Stats.SourceVerticesCount == 4,
            $"Expected 4 source vertices, got {geom.Stats.SourceVerticesCount}");
        Debug.Assert(geom.TriangleMaterials.TrueForAll(material => material == 99),
            "Invalid material index did not use the configured default material");

        Console.WriteLine("  Source statistics and invalid material index test passed");
    }

    private static FLVER2 CreateTestFlver2()
    {
        var flver = new FLVER2();
        flver.Materials.Add(new FLVER2.Material { Name = "hkm_Cobblestone" }); // ID 1

        var mesh = new FLVER2.Mesh
        {
            MaterialIndex = 0,
            NodeIndex = -1
        };

        // Quad in XZ plane (0, 0, 0) to (1, 0, 1)
        mesh.Vertices.Add(new FLVER.Vertex { Position = new Vector3(0, 0, 0) }); // 0
        mesh.Vertices.Add(new FLVER.Vertex { Position = new Vector3(1, 0, 0) }); // 1
        mesh.Vertices.Add(new FLVER.Vertex { Position = new Vector3(1, 0, 1) }); // 2
        mesh.Vertices.Add(new FLVER.Vertex { Position = new Vector3(0, 0, 1) }); // 3

        mesh.FaceSets.Add(new FLVER2.FaceSet
        {
            Flags = FLVER2.FaceSet.FSFlags.None,
            Indices = new List<int>
            {
                0, 1, 2, // Triangle 1
                0, 2, 3  // Triangle 2
            }
        });

        flver.Meshes.Add(mesh);
        return flver;
    }
}
