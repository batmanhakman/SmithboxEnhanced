using Havok;
using Havok.Files;
using Havok.hk2014;
using Havok.Shared;
using SoulsFormats;
using StudioCore.Application;
using StudioCore.Editors.Common;
using StudioCore.Editors.HavokEditor;
using StudioCore.Utilities;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;

namespace StudioCore.Editors.MapEditor.DS3Creator;

/// <summary>
/// Detailed structural and round-trip validation report for DS3 Havok 2014 collisions and maps (M1.8).
/// </summary>
public sealed class DS3CollisionValidationReport
{
    public string TargetName { get; set; } = "";
    public List<string> Errors { get; } = new();
    public List<string> Warnings { get; } = new();

    public int VertexCount { get; set; }
    public int TriangleCount { get; set; }
    public int SectionCount { get; set; }
    public int TreeNodeCount { get; set; }
    public int RawBytes { get; set; }
    public int CompressedBytes { get; set; }
    public int DecodedSubmeshes { get; set; }
    public int DecodedTriangles { get; set; }
    public Vector3 DomainMin { get; set; }
    public Vector3 DomainMax { get; set; }

    public bool IsValid => Errors.Count == 0;

    public string FormatSummary()
    {
        var status = IsValid ? "PASS" : $"FAIL ({Errors.Count} errors)";
        return $"[{status}] {TargetName}: {TriangleCount} tris, {VertexCount} verts, " +
               $"{SectionCount} sections, {TreeNodeCount} BVH nodes, " +
               $"decoded {DecodedTriangles} tris ({RawBytes} B -> {CompressedBytes} B DCX)";
    }
}

/// <summary>
/// Validates source triangle meshes, DS3 Havok 2014 packfile structures, spatial trees, hit-material tables,
/// DCX compression round-trips, and active MSB3 collision registrations (M1.8).
/// </summary>
public static class DS3CollisionValidator
{
    public const float DefaultDegenerateAreaEpsilon = 1e-10f;
    public const float DomainDecodeTolerance = 0.25f;

    public static void ValidateSourceGeometry(
        IReadOnlyList<Vector3> vertices,
        IReadOnlyList<int> indices,
        DS3CollisionValidationReport report,
        float degenerateAreaEpsilon = DefaultDegenerateAreaEpsilon)
    {
        ArgumentNullException.ThrowIfNull(report);

        if (vertices == null || vertices.Count == 0)
        {
            report.Errors.Add("Source geometry has no vertices.");
            return;
        }

        if (indices == null || indices.Count == 0)
        {
            report.Errors.Add("Source geometry has no triangle indices.");
            return;
        }

        if (indices.Count % 3 != 0)
        {
            report.Errors.Add($"Index count ({indices.Count}) is not divisible by 3.");
            return;
        }

        report.VertexCount = vertices.Count;
        report.TriangleCount = indices.Count / 3;

        var invalidVertices = 0;
        for (int i = 0; i < vertices.Count; i++)
        {
            var v = vertices[i];
            if (!float.IsFinite(v.X) || !float.IsFinite(v.Y) || !float.IsFinite(v.Z))
                invalidVertices++;
        }

        if (invalidVertices > 0)
            report.Errors.Add($"Source geometry contains {invalidVertices} non-finite (NaN/Infinity) vertices.");

        var outOfRange = 0;
        var degenerate = 0;

        for (int i = 0; i + 2 < indices.Count; i += 3)
        {
            int ia = indices[i];
            int ib = indices[i + 1];
            int ic = indices[i + 2];

            if ((uint)ia >= (uint)vertices.Count || (uint)ib >= (uint)vertices.Count || (uint)ic >= (uint)vertices.Count)
            {
                outOfRange++;
                continue;
            }

            if (ia == ib || ib == ic || ic == ia)
            {
                degenerate++;
                continue;
            }

            var a = vertices[ia];
            var b = vertices[ib];
            var c = vertices[ic];
            var crossSq = Vector3.Cross(b - a, c - a).LengthSquared();
            if (!float.IsFinite(crossSq) || crossSq <= degenerateAreaEpsilon)
                degenerate++;
        }

        if (outOfRange > 0)
            report.Errors.Add($"Source geometry contains {outOfRange} triangles with out-of-range vertex indices.");

        if (degenerate > 0)
            report.Errors.Add($"Source geometry contains {degenerate} degenerate (zero-area or collapsed) triangles.");
    }

    public static DS3CollisionValidationReport ValidateDocument(
        HavokDocument document,
        string targetName = "CollisionDocument",
        int? expectedTriangleCount = null)
    {
        var report = new DS3CollisionValidationReport { TargetName = targetName };

        if (document == null)
        {
            report.Errors.Add("HavokDocument is null.");
            return report;
        }

        // 1. Serialize to Havok 2014 packfile + compress as DS3 DCX + decompress & reload
        byte[] rawBytes;
        byte[] compressedBytes;
        HavokFileInfo info;
        HavokDocument reloaded;

        try
        {
            rawBytes = HavokFile.Write(document);
            report.RawBytes = rawBytes.Length;

            compressedBytes = DCX.Compress(rawBytes, DS3MapCreatorConstants.Ds3DcxType);
            report.CompressedBytes = compressedBytes.Length;

            var decompressed = DCX.Decompress(compressedBytes);
            info = HavokFile.Sniff(decompressed.Span);
            reloaded = HavokFile.Read(decompressed.ToArray(), HavokProfile.DS3);
        }
        catch (Exception ex)
        {
            report.Errors.Add($"Havok 2014 packfile round-trip serialization failed: {ex.Message}");
            return report;
        }

        if (info.Container != HavokContainer.Packfile || info.Sdk != HavokSdk.Hk2014 || info.PointerSize != 8 || info.BigEndian)
        {
            report.Errors.Add($"Unexpected DS3 Havok header '{info}'. Expected Packfile hk2014 64-bit LE.");
        }

        // 2. Verify hkRootLevelContainer -> hknpPhysicsSceneData -> hknpPhysicsSystemData -> body -> shape hierarchy
        if (reloaded.Root is not hkRootLevelContainer root)
        {
            report.Errors.Add($"Root object is {reloaded.Root?.GetType().Name ?? "null"}, expected hkRootLevelContainer.");
            return report;
        }

        var sceneData = root.m_namedVariants
            .Select(v => v.m_variant)
            .OfType<hknpPhysicsSceneData>()
            .FirstOrDefault();

        if (sceneData == null)
        {
            report.Errors.Add("Missing hknpPhysicsSceneData named variant in hkRootLevelContainer.");
            return report;
        }

        if (sceneData.m_systemDatas == null || sceneData.m_systemDatas.Count == 0 || sceneData.m_systemDatas[0] == null)
        {
            report.Errors.Add("hknpPhysicsSceneData.m_systemDatas is empty.");
            return report;
        }

        var systemData = sceneData.m_systemDatas[0]!;
        if (systemData.m_materials == null || systemData.m_materials.Count == 0)
            report.Errors.Add("hknpPhysicsSystemData.m_materials is empty.");

        if (systemData.m_bodyCinfos == null || systemData.m_bodyCinfos.Count == 0)
        {
            report.Errors.Add("hknpPhysicsSystemData.m_bodyCinfos is empty.");
            return report;
        }

        var validatedCompressedShapes = 0;

        foreach (var body in systemData.m_bodyCinfos)
        {
            if (body?.m_shape == null)
            {
                report.Errors.Add($"Body '{body?.m_name ?? "(null)"}' has null m_shape.");
                continue;
            }

            if (body.m_shape is not fsnpCustomParamCompressedMeshShape meshShape)
            {
                report.Warnings.Add($"Body '{body.m_name}' uses shape '{body.m_shape.GetType().Name}' rather than fsnpCustomParamCompressedMeshShape.");
                continue;
            }

            validatedCompressedShapes++;
            ValidateCompressedMeshShape(meshShape, report);
        }

        if (validatedCompressedShapes == 0 && report.Errors.Count == 0)
            report.Errors.Add("No fsnpCustomParamCompressedMeshShape found inside collision bodies.");

        // 3. Verify Smithbox's shared CollisionGeometry decoder produces valid world-space triangles within domain bounds
        try
        {
            var decoderErrors = new List<string>();
            var submeshes = CollisionGeometry.Submeshes(reloaded.Objects, (shape, ex) =>
                decoderErrors.Add($"{shape?.GetType().Name}: {ex.Message}"));

            foreach (var err in decoderErrors)
                report.Errors.Add($"CollisionGeometry decode error: {err}");

            report.DecodedSubmeshes = submeshes.Count;

            var totalVerts = 0;
            var degDecoded = 0;
            var outOfDomain = 0;
            var hasDomain = report.TreeNodeCount > 0 && IsValidAabb(report.DomainMin, report.DomainMax);

            foreach (var tris in submeshes)
            {
                totalVerts += tris.Count;
                for (int i = 0; i + 2 < tris.Count; i += 3)
                {
                    var a = tris[i];
                    var b = tris[i + 1];
                    var c = tris[i + 2];

                    if (!IsFinite(a) || !IsFinite(b) || !IsFinite(c))
                    {
                        report.Errors.Add("Decoded Havok mesh contains non-finite vertex coordinates.");
                        break;
                    }

                    if (hasDomain &&
                        (!InBounds(a, report.DomainMin, report.DomainMax, DomainDecodeTolerance) ||
                         !InBounds(b, report.DomainMin, report.DomainMax, DomainDecodeTolerance) ||
                         !InBounds(c, report.DomainMin, report.DomainMax, DomainDecodeTolerance)))
                    {
                        outOfDomain++;
                    }

                    if (Vector3.Cross(b - a, c - a).LengthSquared() <= DefaultDegenerateAreaEpsilon)
                        degDecoded++;
                }
            }

            report.DecodedTriangles = totalVerts / 3;
            report.TriangleCount = Math.Max(report.TriangleCount, report.DecodedTriangles);

            if (report.DecodedTriangles == 0)
                report.Errors.Add("CollisionGeometry decoded 0 triangles after Havok packfile round-trip.");

            if (expectedTriangleCount.HasValue && report.DecodedTriangles != expectedTriangleCount.Value)
                report.Errors.Add($"Decoded triangle count ({report.DecodedTriangles}) != expected ({expectedTriangleCount.Value}).");

            if (outOfDomain > 0)
                report.Errors.Add($"{outOfDomain} decoded triangles fell outside the shape's BVH domain bounds.");

            if (degDecoded > 0)
                report.Errors.Add($"{degDecoded} decoded triangles are degenerate after 21-bit quantization.");
        }
        catch (Exception ex)
        {
            report.Errors.Add($"CollisionGeometry.Submeshes failed: {ex.Message}");
        }

        return report;
    }

    private static void ValidateCompressedMeshShape(fsnpCustomParamCompressedMeshShape shape, DS3CollisionValidationReport report)
    {
        if (shape.m_data?.m_meshTree == null)
        {
            report.Errors.Add("fsnpCustomParamCompressedMeshShape.m_data.m_meshTree is missing.");
            return;
        }

        var tree = shape.m_data.m_meshTree;
        var simdNodes = shape.m_data.m_simdTree?.m_nodes?.Count ?? 0;
        if (simdNodes == 0)
            report.Errors.Add("m_data.m_simdTree.m_nodes is empty (DS3 expects 2 SIMD tree stub nodes).");

        if (tree.m_nodes == null || tree.m_nodes.Count == 0)
            report.Errors.Add("Compressed mesh BVH m_nodes is empty.");
        else
            report.TreeNodeCount += tree.m_nodes.Count;

        if (tree.m_sections == null || tree.m_sections.Count == 0)
            report.Errors.Add("Compressed mesh m_sections is empty.");
        else
            report.SectionCount += tree.m_sections.Count;

        if (tree.m_primitives == null || tree.m_primitives.Count == 0)
            report.Errors.Add("Compressed mesh m_primitives is empty.");

        if (tree.m_primitiveDataRuns == null || tree.m_primitiveDataRuns.Count == 0)
            report.Errors.Add("Compressed mesh m_primitiveDataRuns is empty.");

        var dMin = AsVec3(tree.m_domain.m_min);
        var dMax = AsVec3(tree.m_domain.m_max);
        if (!IsValidAabb(dMin, dMax))
        {
            report.Errors.Add($"Invalid tree m_domain AABB: min={dMin}, max={dMax}.");
        }
        else
        {
            report.DomainMin = dMin;
            report.DomainMax = dMax;
        }

        var vertexCapacity = (tree.m_packedVertices?.Count ?? 0) + (tree.m_sharedVertices?.Count ?? 0);
        report.VertexCount = Math.Max(report.VertexCount, vertexCapacity);
        if (vertexCapacity == 0)
            report.Errors.Add("Compressed mesh contains neither packed nor shared vertices.");

        if (tree.m_sections != null)
        {
            for (int sIdx = 0; sIdx < tree.m_sections.Count; sIdx++)
            {
                var sec = tree.m_sections[sIdx];
                var sMin = AsVec3(sec.m_domain.m_min);
                var sMax = AsVec3(sec.m_domain.m_max);
                if (!IsValidAabb(sMin, sMax))
                    report.Errors.Add($"Section {sIdx} has invalid m_domain AABB: min={sMin}, max={sMax}.");

                if (sec.m_nodes == null || sec.m_nodes.Count == 0)
                    report.Errors.Add($"Section {sIdx} has empty BVH m_nodes.");
            }
        }

        if (shape.m_pParam == null)
        {
            report.Errors.Add("fsnpCustomParamCompressedMeshShape.m_pParam (hit material parameter table) is missing.");
        }
        else
        {
            var groupCount = shape.m_pParam.m_primitiveDataArray?.Count ?? 0;
            var triCount = shape.m_pParam.m_triangleDataArray?.Count ?? 0;

            if (groupCount == 0)
                report.Errors.Add("m_pParam.m_primitiveDataArray (material groups) is empty.");

            if (triCount == 0)
                report.Errors.Add("m_pParam.m_triangleDataArray is empty.");

            if (shape.m_triangleIndexToShapeKey == null || shape.m_triangleIndexToShapeKey.Count != triCount)
                report.Errors.Add($"m_triangleIndexToShapeKey count ({shape.m_triangleIndexToShapeKey?.Count ?? 0}) does not match m_pParam triangles ({triCount}).");

            if (shape.m_pParam.m_triangleDataArray != null)
            {
                var badGroupRefs = shape.m_pParam.m_triangleDataArray.Count(t => t.m_primitiveDataIndex >= (uint)groupCount);
                if (badGroupRefs > 0)
                    report.Errors.Add($"{badGroupRefs} triangles reference out-of-range m_primitiveDataArray material group indices.");
            }
        }
    }

    public static List<DS3CollisionValidationReport> ValidateActiveMap(MapEditorView view, MapContainer map)
    {
        var reports = new List<DS3CollisionValidationReport>();
        if (view == null || map == null)
            return reports;

        var bank = view.HavokCollisionBank;
        var mapPieces = map.Objects.OfType<MsbEntity>().Where(EntityHelper.IsPartMapPiece).ToList();
        var collisions = map.Objects.OfType<MsbEntity>().Where(EntityHelper.IsPartCollision).ToList();

        if (collisions.Count == 0)
        {
            var emptyReport = new DS3CollisionValidationReport { TargetName = $"{map.Name} MSB3" };
            emptyReport.Warnings.Add("Map has no MSB3 Collision parts.");
            reports.Add(emptyReport);
            return reports;
        }

        foreach (var col in collisions)
        {
            var colModelName = DS3CollisionBuilder.ResolvePartModelName(col);
            var partReport = new DS3CollisionValidationReport { TargetName = $"MSB Part {col.Name} ({colModelName})" };

            if (col.WrappedObject is MSB3.Part.Collision msbCol)
            {
                if (msbCol.DrawGroups.All(g => g == 0))
                    partReport.Errors.Add("Collision DrawGroups are all 0 (map geometry will be hidden when standing here).");

                if (msbCol.DispGroups.All(g => g == 0))
                    partReport.Errors.Add("Collision DispGroups are all 0.");

                if (Vector3.DistanceSquared(msbCol.Scale, Vector3.One) > 1e-4f)
                    partReport.Warnings.Add($"Collision part has non-unit Scale {msbCol.Scale}; Havok ignores MSB part scale.");

                if (mapPieces.Any(p => p.WrappedObject is MSB3.Part.MapPiece mp && !GroupsOverlap(mp.DrawGroups, msbCol.DrawGroups)))
                    partReport.Errors.Add("Collision DrawGroups do not overlap with MapPiece DrawGroups.");
            }

            if (string.IsNullOrEmpty(colModelName) || colModelName.Length < 2)
            {
                partReport.Errors.Add("Collision part has empty/invalid ModelName.");
                reports.Add(partReport);
                continue;
            }

            var idPart = colModelName.Substring(1);
            foreach (var layer in new[] { "h", "l" })
            {
                var fileName = $"{layer}{map.Name.Substring(1)}_{idPart}.hkx.dcx";
                var doc = bank.GetEditableDocument(fileName);
                if (doc == null)
                {
                    var miss = new DS3CollisionValidationReport { TargetName = fileName };
                    miss.Errors.Add($"Collision file '{fileName}' was not found or failed to load from '{layer}{map.Name.Substring(1)}.hkxbhd/.hkxbdt'.");
                    reports.Add(miss);
                }
                else
                {
                    var docReport = ValidateDocument(doc, fileName);
                    docReport.Warnings.AddRange(partReport.Warnings);
                    docReport.Errors.AddRange(partReport.Errors);
                    reports.Add(docReport);
                }
            }
        }

        return reports;
    }

    private static bool GroupsOverlap(uint[] a, uint[] b)
    {
        if (a == null || b == null) return false;
        int len = Math.Min(a.Length, b.Length);
        for (int i = 0; i < len; i++)
        {
            if ((a[i] & b[i]) != 0)
                return true;
        }
        return false;
    }

    private static Vector3 AsVec3(Vector4 v) => new(v.X, v.Y, v.Z);
    private static bool IsFinite(Vector3 v) => float.IsFinite(v.X) && float.IsFinite(v.Y) && float.IsFinite(v.Z);
    private static bool IsValidAabb(Vector3 min, Vector3 max) =>
        IsFinite(min) && IsFinite(max) && min.X <= max.X + 1e-4f && min.Y <= max.Y + 1e-4f && min.Z <= max.Z + 1e-4f;

    private static bool InBounds(Vector3 p, Vector3 min, Vector3 max, float tol) =>
        p.X >= min.X - tol && p.X <= max.X + tol &&
        p.Y >= min.Y - tol && p.Y <= max.Y + tol &&
        p.Z >= min.Z - tol && p.Z <= max.Z + tol;
}
