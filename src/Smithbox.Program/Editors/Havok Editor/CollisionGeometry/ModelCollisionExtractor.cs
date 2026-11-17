using SoulsFormats;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;

namespace StudioCore.Editors.HavokEditor;

/// <summary>
/// Options for configuring model-to-collision geometry extraction.
/// </summary>
public sealed class ModelCollisionExtractionOptions
{
    /// <summary>Whether to evaluate node/bone transforms for FLVER models.</summary>
    public bool ApplyNodeTransforms { get; set; } = true;

    /// <summary>Additional object / world transformation to apply to the extracted vertices.</summary>
    public Matrix4x4 ObjectTransform { get; set; } = Matrix4x4.Identity;

    /// <summary>Whether to deduplicate / weld vertices within tolerance.</summary>
    public bool WeldVertices { get; set; } = true;

    /// <summary>Distance tolerance for vertex welding (default 0.0001f / 0.1mm).</summary>
    public float WeldTolerance { get; set; } = 0.0001f;

    /// <summary>Whether vertices can be welded across different materials/sections.</summary>
    public bool WeldAcrossMaterials { get; set; } = true;

    /// <summary>Whether to detect and discard degenerate triangles (zero area, collinear, collapsed edges, needles).</summary>
    public bool RemoveDegenerateTriangles { get; set; } = true;

    /// <summary>Epsilon for degenerate triangle cross-product length squared test.</summary>
    public float DegenerateAreaEpsilon { get; set; } = 1e-10f;

    /// <summary>Whether to validate vertices (discard NaN, Infinity, extreme coordinates).</summary>
    public bool RemoveInvalidGeometry { get; set; } = true;

    /// <summary>Whether to invert triangle winding.</summary>
    public bool FlipWinding { get; set; } = false;

    /// <summary>Default collision material ID when none is specified or recognized.</summary>
    public uint DefaultMaterialId { get; set; } = 1;

    /// <summary>Whether a Color layout normal's W is a node index (true for games after DS1).</summary>
    public bool ColorNormalIsNode { get; set; } = true;

    /// <summary>Optional subset of mesh section indices to extract (null = all meshes).</summary>
    public IReadOnlyList<int> SelectedMeshIndices { get; set; }

    /// <summary>Custom material resolver function: (meshIndex, materialName) => materialId</summary>
    public Func<int, string, uint> CustomMaterialResolver { get; set; }
}

/// <summary>
/// Source input for an individual model part in a multi-part collision model assembly.
/// </summary>
public sealed class ModelPartSource
{
    public string Name { get; set; } = "";
    public FLVER2 Flver2 { get; set; }
    public FLVER0 Flver0 { get; set; }
    public ObjCollisionSource Obj { get; set; }
    public Matrix4x4 Transform { get; set; } = Matrix4x4.Identity;
    public IReadOnlyList<int> SelectedSectionIndices { get; set; }
    public uint? OverrideMaterialId { get; set; }
}

/// <summary>
/// Model-to-Collision extraction engine. Extracts, validates, transforms, deduplicates,
/// and builds collision geometry independent of rendering representation.
/// </summary>
public static class ModelCollisionExtractor
{
    /// <summary>
    /// Extracts collision geometry from a FLVER2 model.
    /// </summary>
    public static ModelCollisionGeometry ExtractFromFlver(FLVER2 model, ModelCollisionExtractionOptions options = null)
    {
        options ??= new ModelCollisionExtractionOptions();
        var result = new ModelCollisionGeometry();

        if (model == null || model.Meshes == null || model.Meshes.Count == 0)
            return result;

        var nodes = options.ApplyNodeTransforms && model.Nodes != null ? NodeWorlds(model.Nodes) : Array.Empty<Matrix4x4>();
        var targetMeshIndices = options.SelectedMeshIndices ?? Enumerable.Range(0, model.Meshes.Count).ToList();
        result.Stats.SourceVerticesCount = targetMeshIndices
            .Where(index => index >= 0 && index < model.Meshes.Count)
            .Sum(index => model.Meshes[index].Vertices.Count);

        var builder = new CollisionGeometryBuilder(options, result);

        for (int i = 0; i < targetMeshIndices.Count; i++)
        {
            int meshIndex = targetMeshIndices[i];
            if (meshIndex < 0 || meshIndex >= model.Meshes.Count)
                continue;

            var mesh = model.Meshes[meshIndex];
            var material = mesh.MaterialIndex >= 0 && mesh.MaterialIndex < model.Materials.Count
                ? model.Materials[mesh.MaterialIndex]
                : null;
            var materialName = material?.Name ?? "";
            uint materialId = ResolveMaterialId(meshIndex, materialName, options);

            builder.BeginSection(meshIndex, $"Mesh_{meshIndex}", materialName, materialId);

            // Compute static node transform for this mesh
            var perVertexNormalW = false;
            var perVertexBone = false;
            var meshMatrix = Matrix4x4.Identity;

            if (options.ApplyNodeTransforms && mesh.Dynamic == 0 && nodes.Length > 0)
            {
                var members = mesh.VertexBuffers
                    .Where(b => b.LayoutIndex >= 0 && b.LayoutIndex < model.BufferLayouts.Count)
                    .SelectMany(b => model.BufferLayouts[b.LayoutIndex])
                    .ToList();

                perVertexNormalW = members.Any(m => m.Semantic == FLVER.LayoutSemantic.Normal &&
                    (m.Type is FLVER.LayoutType.UByte4 or FLVER.LayoutType.Byte4E ||
                     (options.ColorNormalIsNode && m.Type == FLVER.LayoutType.Color)));

                perVertexBone = !perVertexNormalW && mesh.BoneIndices.Count > 1
                    && members.Any(m => m.Semantic == FLVER.LayoutSemantic.BoneIndices);

                if (mesh.NodeIndex >= 0 && mesh.NodeIndex < nodes.Length)
                    meshMatrix = nodes[mesh.NodeIndex];
            }

            var faces = mesh.GetFaces();
            foreach (var face in faces)
            {
                if (face.Length < 3)
                    continue;

                result.Stats.SourceTrianglesCount++;

                Vector3 TransformVertex(FLVER.Vertex v)
                {
                    var matrix = perVertexNormalW ? Matrix4x4.Identity : meshMatrix;

                    if (perVertexNormalW && (v.NormalW & 0xFF) is var node && node < nodes.Length)
                    {
                        matrix = nodes[node];
                    }
                    else if (perVertexBone)
                    {
                        var slot = v.BoneIndices[0];
                        if (slot >= 0 && slot < mesh.BoneIndices.Count && mesh.BoneIndices[slot] is var boneNode && boneNode >= 0 && boneNode < nodes.Length)
                            matrix = nodes[boneNode];
                    }

                    var pos = Vector3.Transform(v.Position, matrix);
                    return Vector3.Transform(pos, options.ObjectTransform);
                }

                var p0 = TransformVertex(face[0]);
                var p1 = TransformVertex(face[1]);
                var p2 = TransformVertex(face[2]);

                builder.AddTriangle(p0, p1, p2, materialId);
            }

            builder.EndSection();
        }

        result.UpdateBounds();
        return result;
    }

    /// <summary>
    /// Extracts collision geometry from a FLVER0 model (e.g. Demon's Souls).
    /// </summary>
    public static ModelCollisionGeometry ExtractFromFlver0(FLVER0 model, ModelCollisionExtractionOptions options = null)
    {
        options ??= new ModelCollisionExtractionOptions();
        var result = new ModelCollisionGeometry();

        if (model == null || model.Meshes == null || model.Meshes.Count == 0)
            return result;

        var nodes = options.ApplyNodeTransforms && model.Nodes != null ? NodeWorlds(model.Nodes) : Array.Empty<Matrix4x4>();
        var targetMeshIndices = options.SelectedMeshIndices ?? Enumerable.Range(0, model.Meshes.Count).ToList();
        result.Stats.SourceVerticesCount = targetMeshIndices
            .Where(index => index >= 0 && index < model.Meshes.Count)
            .Sum(index => model.Meshes[index].Vertices.Count);

        var builder = new CollisionGeometryBuilder(options, result);

        for (int i = 0; i < targetMeshIndices.Count; i++)
        {
            int meshIndex = targetMeshIndices[i];
            if (meshIndex < 0 || meshIndex >= model.Meshes.Count)
                continue;

            var mesh = model.Meshes[meshIndex];
            var material = mesh.MaterialIndex >= 0 && mesh.MaterialIndex < model.Materials.Count
                ? model.Materials[mesh.MaterialIndex]
                : null;
            var materialName = material?.Name ?? "";
            uint materialId = ResolveMaterialId(meshIndex, materialName, options);

            builder.BeginSection(meshIndex, $"Mesh_{meshIndex}", materialName, materialId);

            var indices = mesh.Triangulate(model.Header.Version, false, false);
            var layouts = material?.Layouts;
            var hasBoneIndices = layouts != null && mesh.LayoutIndex < layouts.Count
                && layouts[mesh.LayoutIndex].Any(m => m.Semantic == FLVER.LayoutSemantic.BoneIndices);
            var usedBones = mesh.UsedBoneCount;

            for (var t = 0; t + 2 < indices.Count; t += 3)
            {
                result.Stats.SourceTrianglesCount++;

                int i0 = indices[t];
                int i1 = indices[t + 1];
                int i2 = indices[t + 2];

                if (i0 < 0 || i0 >= mesh.Vertices.Count ||
                    i1 < 0 || i1 >= mesh.Vertices.Count ||
                    i2 < 0 || i2 >= mesh.Vertices.Count)
                {
                    result.Stats.InvalidTrianglesCount++;
                    continue;
                }

                Vector3 TransformVertex(FLVER.Vertex v)
                {
                    var matrix = Matrix4x4.Identity;
                    if (options.ApplyNodeTransforms && mesh.Dynamic == 0 && nodes.Length > 0)
                    {
                        var slot = v.BoneIndices[0];
                        var node = !hasBoneIndices ? mesh.NodeIndex
                            : slot >= 0 && slot < usedBones && slot < mesh.BoneIndices.Length ? mesh.BoneIndices[slot] : -1;

                        if (node >= 0 && node < nodes.Length)
                            matrix = nodes[node];
                    }

                    var pos = Vector3.Transform(v.Position, matrix);
                    return Vector3.Transform(pos, options.ObjectTransform);
                }

                var p0 = TransformVertex(mesh.Vertices[i0]);
                var p1 = TransformVertex(mesh.Vertices[i1]);
                var p2 = TransformVertex(mesh.Vertices[i2]);

                builder.AddTriangle(p0, p1, p2, materialId);
            }

            builder.EndSection();
        }

        result.UpdateBounds();
        return result;
    }

    /// <summary>
    /// Extracts collision geometry from raw model bytes (detects FLVER2 / FLVER0, decompresses DCX if necessary).
    /// </summary>
    public static ModelCollisionGeometry ExtractFromBytes(byte[] bytes, ModelCollisionExtractionOptions options = null)
    {
        if (bytes == null || bytes.Length == 0)
            return new ModelCollisionGeometry();

        if (DCX.Is(bytes))
            bytes = DCX.Decompress(bytes).ToArray();

        if (!IsFlverBytes(bytes))
            return new ModelCollisionGeometry();

        var bigEndian = bytes[6] == 'B';
        var version = bigEndian
            ? (bytes[8] << 24) | (bytes[9] << 16) | (bytes[10] << 8) | bytes[11]
            : (bytes[11] << 24) | (bytes[10] << 16) | (bytes[9] << 8) | bytes[8];

        return version >= 0x20000
            ? ExtractFromFlver(FLVER2.Read(bytes), options)
            : ExtractFromFlver0(FLVER0.Read(bytes), options);
    }

    /// <summary>
    /// Extracts collision geometry from an OBJ source.
    /// </summary>
    public static ModelCollisionGeometry ExtractFromObj(ObjCollisionSource obj, ModelCollisionExtractionOptions options = null)
    {
        options ??= new ModelCollisionExtractionOptions();
        var result = new ModelCollisionGeometry();

        if (obj == null || obj.Groups.Count == 0)
            return result;

        var targetGroupIndices = options.SelectedMeshIndices ?? Enumerable.Range(0, obj.Groups.Count).ToList();
        result.Stats.SourceVerticesCount = obj.Positions.Count;
        var builder = new CollisionGeometryBuilder(options, result);

        for (int i = 0; i < targetGroupIndices.Count; i++)
        {
            int groupIndex = targetGroupIndices[i];
            if (groupIndex < 0 || groupIndex >= obj.Groups.Count)
                continue;

            var group = obj.Groups[groupIndex];
            var materialName = group.Material ?? "";
            uint materialId = ResolveMaterialId(groupIndex, materialName, options);

            builder.BeginSection(groupIndex, string.IsNullOrEmpty(group.Name) ? $"Group_{groupIndex}" : group.Name, materialName, materialId);

            for (int t = 0; t + 2 < group.Triangles.Count; t += 3)
            {
                result.Stats.SourceTrianglesCount++;

                int i0 = group.Triangles[t];
                int i1 = group.Triangles[t + 1];
                int i2 = group.Triangles[t + 2];

                if (i0 < 0 || i0 >= obj.Positions.Count ||
                    i1 < 0 || i1 >= obj.Positions.Count ||
                    i2 < 0 || i2 >= obj.Positions.Count)
                {
                    result.Stats.InvalidTrianglesCount++;
                    continue;
                }

                var p0 = Vector3.Transform(obj.Positions[i0], options.ObjectTransform);
                var p1 = Vector3.Transform(obj.Positions[i1], options.ObjectTransform);
                var p2 = Vector3.Transform(obj.Positions[i2], options.ObjectTransform);

                builder.AddTriangle(p0, p1, p2, materialId);
            }

            builder.EndSection();
        }

        result.UpdateBounds();
        return result;
    }

    /// <summary>
    /// Extracts and merges collision geometry from multiple model parts.
    /// Each part can have its own model, transform, mesh selection, and material override.
    /// </summary>
    public static ModelCollisionGeometry ExtractFromParts(IEnumerable<ModelPartSource> parts, ModelCollisionExtractionOptions baseOptions = null)
    {
        baseOptions ??= new ModelCollisionExtractionOptions();
        var result = new ModelCollisionGeometry();

        if (parts == null)
            return result;

        var builder = new CollisionGeometryBuilder(baseOptions, result);

        int partIndex = 0;
        foreach (var part in parts)
        {
            if (part == null)
                continue;

            partIndex++;
            result.Stats.PartsProcessedCount++;

            var partOptions = new ModelCollisionExtractionOptions
            {
                ApplyNodeTransforms = baseOptions.ApplyNodeTransforms,
                ObjectTransform = part.Transform * baseOptions.ObjectTransform,
                WeldVertices = baseOptions.WeldVertices,
                WeldTolerance = baseOptions.WeldTolerance,
                WeldAcrossMaterials = baseOptions.WeldAcrossMaterials,
                RemoveDegenerateTriangles = baseOptions.RemoveDegenerateTriangles,
                DegenerateAreaEpsilon = baseOptions.DegenerateAreaEpsilon,
                RemoveInvalidGeometry = baseOptions.RemoveInvalidGeometry,
                FlipWinding = baseOptions.FlipWinding,
                DefaultMaterialId = part.OverrideMaterialId ?? baseOptions.DefaultMaterialId,
                ColorNormalIsNode = baseOptions.ColorNormalIsNode,
                SelectedMeshIndices = part.SelectedSectionIndices,
                CustomMaterialResolver = part.OverrideMaterialId != null
                    ? (_, _) => part.OverrideMaterialId.Value
                    : baseOptions.CustomMaterialResolver
            };

            ModelCollisionGeometry partGeom = null;

            if (part.Flver2 != null)
                partGeom = ExtractFromFlver(part.Flver2, partOptions);
            else if (part.Flver0 != null)
                partGeom = ExtractFromFlver0(part.Flver0, partOptions);
            else if (part.Obj != null)
                partGeom = ExtractFromObj(part.Obj, partOptions);

            if (partGeom == null || partGeom.Indices.Count == 0)
                continue;

            // Merge part geometry into builder
            for (int s = 0; s < partGeom.Sections.Count; s++)
            {
                var sec = partGeom.Sections[s];
                builder.BeginSection(result.Sections.Count, $"{part.Name}_{sec.Name}", sec.MaterialName, sec.MaterialId);

                for (int t = sec.TriangleStart; t < sec.TriangleStart + sec.TriangleCount; t++)
                {
                    var tri = partGeom.Triangles[t];
                    var p0 = partGeom.Vertices[tri.A];
                    var p1 = partGeom.Vertices[tri.B];
                    var p2 = partGeom.Vertices[tri.C];
                    builder.AddTriangle(p0, p1, p2, tri.MaterialId);
                }

                builder.EndSection();
            }

            // Accumulate stats
            result.Stats.SourceVerticesCount += partGeom.Stats.SourceVerticesCount;
            result.Stats.SourceTrianglesCount += partGeom.Stats.SourceTrianglesCount;
            result.Stats.DegenerateTrianglesCount += partGeom.Stats.DegenerateTrianglesCount;
            result.Stats.InvalidVerticesCount += partGeom.Stats.InvalidVerticesCount;
            result.Stats.InvalidTrianglesCount += partGeom.Stats.InvalidTrianglesCount;
        }

        result.UpdateBounds();
        return result;
    }

    /// <summary>
    /// Computes world matrices for each node in a FLVER node hierarchy by walking parent chains.
    /// </summary>
    public static Matrix4x4[] NodeWorlds(List<FLVER.Node> nodes)
    {
        if (nodes == null || nodes.Count == 0)
            return Array.Empty<Matrix4x4>();

        var worlds = new Matrix4x4[nodes.Count];

        for (var i = 0; i < nodes.Count; i++)
        {
            var matrix = nodes[i].ComputeLocalTransform();
            var parent = nodes[i].ParentIndex;
            var guard = 0;

            while (parent >= 0 && parent < nodes.Count && guard++ < nodes.Count)
            {
                matrix *= nodes[parent].ComputeLocalTransform();
                parent = nodes[parent].ParentIndex;
            }

            worlds[i] = matrix;
        }

        return worlds;
    }

    /// <summary>
    /// Tests if a vertex has valid numerical coordinates (not NaN, not Infinity, and within sane coordinate limits).
    /// </summary>
    public static bool IsValidVertex(Vector3 v)
    {
        if (float.IsNaN(v.X) || float.IsNaN(v.Y) || float.IsNaN(v.Z))
            return false;

        if (float.IsInfinity(v.X) || float.IsInfinity(v.Y) || float.IsInfinity(v.Z))
            return false;

        // Discard extreme coordinates that indicate uninitialized or corrupted vertex data
        const float maxCoord = 100000.0f;
        if (MathF.Abs(v.X) > maxCoord || MathF.Abs(v.Y) > maxCoord || MathF.Abs(v.Z) > maxCoord)
            return false;

        return true;
    }

    /// <summary>
    /// Tests if a triangle is degenerate (coincident corners, collinear vertices, zero area, or extreme needle ratio).
    /// </summary>
    public static bool IsDegenerateTriangle(Vector3 a, Vector3 b, Vector3 c, float areaEpsilon = 1e-10f)
    {
        float dAB = Vector3.DistanceSquared(a, b);
        float dBC = Vector3.DistanceSquared(b, c);
        float dCA = Vector3.DistanceSquared(c, a);

        // Coincident points
        const float distEpsSq = 1e-12f;
        if (dAB < distEpsSq || dBC < distEpsSq || dCA < distEpsSq)
            return true;

        // Collinear / zero area test via cross product
        Vector3 cross = Vector3.Cross(b - a, c - a);
        float crossLenSq = cross.LengthSquared();
        if (crossLenSq < areaEpsilon)
            return true;

        // Needle triangle test: ratio of altitude squared to longest edge squared
        float maxEdgeSq = MathF.Max(dAB, MathF.Max(dBC, dCA));
        if (maxEdgeSq > 0f && (crossLenSq / maxEdgeSq) < 1e-10f)
            return true;

        return false;
    }

    /// <summary>
    /// Checks if a byte buffer starts with the FLVER magic header.
    /// </summary>
    public static bool IsFlverBytes(byte[] bytes)
    {
        if (bytes == null || bytes.Length < 12)
            return false;

        return bytes[0] == (byte)'F' &&
               bytes[1] == (byte)'L' &&
               bytes[2] == (byte)'V' &&
               bytes[3] == (byte)'E' &&
               bytes[4] == (byte)'R';
    }

    private static uint ResolveMaterialId(int meshIndex, string materialName, ModelCollisionExtractionOptions options)
    {
        if (options.CustomMaterialResolver != null)
            return options.CustomMaterialResolver(meshIndex, materialName);

        if (HavokCollisionMaterials.TryParse(materialName, out var id))
            return id;

        return options.DefaultMaterialId;
    }

    /// <summary>
    /// Helper builder that manages spatial hashing, deduplication, section tracking, and geometry validation.
    /// </summary>
    private sealed class CollisionGeometryBuilder
    {
        private readonly ModelCollisionExtractionOptions _options;
        private readonly ModelCollisionGeometry _target;
        private readonly bool _reverseWinding;
        private readonly float _invWeldTol;
        private readonly Dictionary<(int X, int Y, int Z, uint Mat), int> _weldMap = new();

        private ModelCollisionSection _currentSection;
        private int _sectionVertStart;
        private int _sectionTriStart;
        private Vector3 _secMin;
        private Vector3 _secMax;

        public CollisionGeometryBuilder(ModelCollisionExtractionOptions options, ModelCollisionGeometry target)
        {
            _options = options;
            _target = target;
            _reverseWinding = options.FlipWinding ^ (options.ObjectTransform.GetDeterminant() < 0);
            _invWeldTol = 1.0f / MathF.Max(options.WeldTolerance, 1e-6f);
        }

        public void BeginSection(int index, string name, string matName, uint matId)
        {
            _sectionVertStart = _target.Vertices.Count;
            _sectionTriStart = _target.Triangles.Count;
            _secMin = new Vector3(float.MaxValue);
            _secMax = new Vector3(float.MinValue);

            _currentSection = new ModelCollisionSection
            {
                SectionIndex = index,
                Name = name,
                MaterialName = matName,
                MaterialId = matId,
                VertexStart = _sectionVertStart,
                TriangleStart = _sectionTriStart
            };
        }

        public void EndSection()
        {
            if (_currentSection == null)
                return;

            _currentSection.VertexCount = _target.Vertices.Count - _sectionVertStart;
            _currentSection.TriangleCount = _target.Triangles.Count - _sectionTriStart;
            _currentSection.MinBounds = _secMin == new Vector3(float.MaxValue) ? Vector3.Zero : _secMin;
            _currentSection.MaxBounds = _secMax == new Vector3(float.MinValue) ? Vector3.Zero : _secMax;

            _target.Sections.Add(_currentSection);
            _target.Stats.SectionsProcessedCount++;
            _currentSection = null;
        }

        public void AddTriangle(Vector3 p0, Vector3 p1, Vector3 p2, uint materialId)
        {
            // Invalid geometry check
            if (_options.RemoveInvalidGeometry)
            {
                if (!IsValidVertex(p0) || !IsValidVertex(p1) || !IsValidVertex(p2))
                {
                    _target.Stats.InvalidVerticesCount += 3;
                    _target.Stats.InvalidTrianglesCount++;
                    return;
                }
            }

            // Degenerate triangle check before welding
            if (_options.RemoveDegenerateTriangles)
            {
                if (IsDegenerateTriangle(p0, p1, p2, _options.DegenerateAreaEpsilon))
                {
                    _target.Stats.DegenerateTrianglesCount++;
                    return;
                }
            }

            int i0 = GetOrAddVertex(p0, materialId);
            int i1 = GetOrAddVertex(p1, materialId);
            int i2 = GetOrAddVertex(p2, materialId);

            // Re-check after welding (vertices might have welded to same point)
            if (i0 == i1 || i1 == i2 || i2 == i0)
            {
                _target.Stats.DegenerateTrianglesCount++;
                return;
            }

            if (_reverseWinding)
            {
                (i1, i2) = (i2, i1);
                (p1, p2) = (p2, p1);
            }

            Vector3 normal = Vector3.Normalize(Vector3.Cross(p1 - p0, p2 - p0));
            if (float.IsNaN(normal.X) || float.IsNaN(normal.Y) || float.IsNaN(normal.Z))
                normal = Vector3.UnitY;

            _target.Indices.Add(i0);
            _target.Indices.Add(i1);
            _target.Indices.Add(i2);
            _target.TriangleMaterials.Add(materialId);

            _target.Triangles.Add(new ModelCollisionTriangle(
                i0, i1, i2, materialId, normal, _currentSection?.SectionIndex ?? 0));

            _target.Stats.ExtractedTrianglesCount++;
        }

        private int GetOrAddVertex(Vector3 pos, uint matId)
        {
            if (_options.WeldVertices)
            {
                var key = (
                    (int)MathF.Round(pos.X * _invWeldTol),
                    (int)MathF.Round(pos.Y * _invWeldTol),
                    (int)MathF.Round(pos.Z * _invWeldTol),
                    _options.WeldAcrossMaterials ? 0u : matId
                );

                if (_weldMap.TryGetValue(key, out int existingIndex))
                {
                    if (Vector3.DistanceSquared(_target.Vertices[existingIndex], pos) <= (_options.WeldTolerance * _options.WeldTolerance))
                    {
                        _target.Stats.DuplicateVerticesWeldedCount++;
                        return existingIndex;
                    }
                }

                int newIndex = _target.Vertices.Count;
                _target.Vertices.Add(pos);
                _weldMap[key] = newIndex;
                UpdateBounds(pos);
                _target.Stats.ExtractedVerticesCount++;
                return newIndex;
            }
            else
            {
                int newIndex = _target.Vertices.Count;
                _target.Vertices.Add(pos);
                UpdateBounds(pos);
                _target.Stats.ExtractedVerticesCount++;
                return newIndex;
            }
        }

        private void UpdateBounds(Vector3 pos)
        {
            _secMin = Vector3.Min(_secMin, pos);
            _secMax = Vector3.Max(_secMax, pos);
        }
    }
}
