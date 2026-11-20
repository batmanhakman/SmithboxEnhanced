using Havok;
using Havok.Files;
using Havok.hk2014;
using Havok.Shared;
using SoulsFormats;
using StudioCore.Editors.HavokEditor;
using StudioCore.Editors.MapEditor;
using StudioCore.Editors.MapEditor.DS3Creator;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using Xunit;

namespace Smithbox.Tests.DS3Creator;

public class DS3Milestone1Tests
{
    [Fact]
    public void Scaffold_MinimalMsb3_RoundTripsWithPlayerSpawnAndModel()
    {
        var spawn = new Vector3(2.5f, 1.25f, -4.0f);
        var msb = DS3MapScaffolder.BuildMinimalMsb3(spawn);

        var bytes = msb.Write();
        Assert.NotEmpty(bytes);

        DCX.Decompress(bytes, out var comp);
        Assert.Equal(DS3MapCreatorConstants.Ds3DcxType, comp);

        var reloaded = MSB3.Read(bytes);
        var playerModel = Assert.Single(reloaded.Models.Players);
        Assert.Equal("c0000", playerModel.Name);
        Assert.EndsWith("c0000.sib", playerModel.SibPath, StringComparison.OrdinalIgnoreCase);

        var playerPart = Assert.Single(reloaded.Parts.Players);
        Assert.Equal("c0000_0000", playerPart.Name);
        Assert.Equal("c0000", playerPart.ModelName);
        Assert.Equal(spawn, playerPart.Position);
        Assert.Equal(Vector3.One, playerPart.Scale);
    }

    [Fact]
    public void Scaffold_EmptyBtl_RoundTripsWithDs3Header()
    {
        var btl = DS3MapScaffolder.BuildEmptyBtl();
        var bytes = btl.Write();
        Assert.NotEmpty(bytes);

        DCX.Decompress(bytes, out var comp);
        Assert.Equal(DS3MapCreatorConstants.Ds3DcxType, comp);

        var reloaded = BTL.Read(bytes);
        Assert.Equal(DS3MapCreatorConstants.Ds3BtlVersion, reloaded.Version);
        Assert.Equal(DS3MapCreatorConstants.Ds3BtlLightSize, reloaded.LightSize);
        Assert.True(reloaded.LongOffsets);
        Assert.Empty(reloaded.Lights);
    }

    [Fact]
    public void Scaffold_BaselineGparam_RoundTripsWithVisibleLighting()
    {
        var gparam = DS3MapScaffolder.BuildBaselineGparam(null);
        var bytes = gparam.Write();
        Assert.NotEmpty(bytes);

        DCX.Decompress(bytes, out var comp);
        Assert.Equal(DS3MapCreatorConstants.Ds3DcxType, comp);

        var reloaded = GPARAM.Read(bytes);
        Assert.Equal(GPARAM.GparamVersion.V3, reloaded.Version);
        Assert.True(reloaded.WideStrings);
        Assert.True(reloaded.Unk0D);

        Assert.Contains(reloaded.Params, p => p.Key == "LightSet ParamEditor" && p.Fields.Count >= 5);
        Assert.Contains(reloaded.Params, p => p.Key == "Tone Map[Yebis]" && p.Fields.Count >= 7);
        Assert.Contains(reloaded.Params, p => p.Key == "Setting Info[Yebis]" && p.Fields.Count >= 1);

        foreach (var param in reloaded.Params)
        {
            foreach (var field in param.Fields)
            {
                Assert.NotEmpty(field.Values);
            }
        }
    }

    [Fact]
    public void Scaffold_CollisionBinder_EmptyAndPopulatedBxf4_RoundTrip()
    {
        var (emptyBhd, emptyBdt) = DS3MapScaffolder.BuildCollisionBinder();
        var emptyReloaded = BXF4.Read(emptyBhd, emptyBdt);
        Assert.Empty(emptyReloaded.Files);
        Assert.True(emptyReloaded.Unicode);
        Assert.Equal(4, emptyReloaded.Extended);

        var dummyPayload = new byte[] { 0x48, 0x4B, 0x58, 0x00, 0x11, 0x22, 0x33, 0x44 };
        var innerName = @"h31_90_00_00\h31_90_00_00_000100.hkx.dcx";
        var (bhd, bdt) = DS3MapScaffolder.BuildCollisionBinder((innerName, dummyPayload));

        var reloaded = BXF4.Read(bhd, bdt);
        var entry = Assert.Single(reloaded.Files);
        Assert.Equal(innerName, entry.Name);

        var decompressed = DCX.Decompress(entry.Bytes, out var comp).ToArray();
        Assert.Equal(DS3MapCreatorConstants.Ds3DcxType, comp);
        Assert.Equal(dummyPayload, decompressed);
    }

    [Theory]
    [InlineData(DS3PrimitiveType.BoxFloor, true)]
    [InlineData(DS3PrimitiveType.Wall, false)]
    [InlineData(DS3PrimitiveType.Ramp, false)]
    [InlineData(DS3PrimitiveType.Stairs, false)]
    public void PrimitiveFlverBuilder_AllFourPrimitives_RoundTripAndHaveOutwardWinding(
        DS3PrimitiveType primitiveType,
        bool anchorTopAtOrigin)
    {
        var spec = new DS3PrimitiveSpec
        {
            Type = primitiveType,
            Width = 10.0f,
            Height = 3.0f,
            Depth = 8.0f,
            StairSteps = 6,
            AnchorTopAtOrigin = anchorTopAtOrigin
        };

        var stem = "m31_90_00_00_000100";
        var flver = DS3PrimitiveFlverBuilder.BuildFlver2(stem, spec);
        var bndBytes = DS3PrimitiveFlverBuilder.PackMapBnd("m31_90_00_00", "000100", flver);

        DCX.Decompress(bndBytes, out var bndComp);
        Assert.Equal(DS3MapCreatorConstants.Ds3DcxType, bndComp);

        var bnd = BND4.Read(bndBytes);
        var file = Assert.Single(bnd.Files);
        Assert.EndsWith($"{stem}.flver", file.Name, StringComparison.OrdinalIgnoreCase);

        var reloaded = FLVER2.Read(file.Bytes);
        Assert.Equal(DS3MapCreatorConstants.Ds3FlverVersion, reloaded.Header.Version);
        Assert.True(reloaded.Header.Unicode);
        Assert.Single(reloaded.Nodes);
        Assert.Single(reloaded.Materials);
        Assert.NotEmpty(reloaded.Materials[0].Textures);
        Assert.Contains(reloaded.Materials[0].Textures,
            t => string.Equals(t.Path, DS3MapCreatorConstants.DefaultDiffuseTexture, StringComparison.OrdinalIgnoreCase));

        var mesh = Assert.Single(reloaded.Meshes);
        var faceSet = Assert.Single(mesh.FaceSets);
        Assert.NotEmpty(mesh.Vertices);
        Assert.NotEmpty(faceSet.Indices);
        Assert.Equal(0, faceSet.Indices.Count % 3);

        Assert.True(reloaded.Header.BoundingBoxMin.X < reloaded.Header.BoundingBoxMax.X);
        Assert.True(reloaded.Header.BoundingBoxMin.Y < reloaded.Header.BoundingBoxMax.Y);
        Assert.True(reloaded.Header.BoundingBoxMin.Z < reloaded.Header.BoundingBoxMax.Z);

        if (anchorTopAtOrigin)
        {
            Assert.InRange(reloaded.Header.BoundingBoxMax.Y, -1e-4f, 1e-4f);
        }

        // Verify DS3 FLVER2 LH CCW outward face winding for every triangle: Dot(Cross(C - A, B - A), avgNormal) > 0
        for (int i = 0; i < faceSet.Indices.Count; i += 3)
        {
            var va = mesh.Vertices[faceSet.Indices[i]];
            var vb = mesh.Vertices[faceSet.Indices[i + 1]];
            var vc = mesh.Vertices[faceSet.Indices[i + 2]];

            var avgNormal = Vector3.Normalize(va.Normal + vb.Normal + vc.Normal);
            var lhCross = Vector3.Cross(vc.Position - va.Position, vb.Position - va.Position);
            Assert.True(Vector3.Dot(lhCross, avgNormal) > 0.0f,
                $"Triangle {i / 3} in {primitiveType} failed outward LH CCW winding check.");
        }
    }

    [Fact]
    public void CollisionExtractor_WeldsDuplicateVerticesAndStripsDegenerateTriangles()
    {
        var spec = DS3PrimitiveFlverBuilder.CreateStarterFloorSpec(12.0f, 1.0f, 12.0f);
        var flver = DS3PrimitiveFlverBuilder.BuildFlver2("m31_90_00_00_000100", spec);

        // BoxFloor builds 6 quads (2 triangles x 3 unshared verts each) -> 36 vertices, 12 triangles (36 indices)
        Assert.Equal(36, flver.Meshes[0].Vertices.Count);
        Assert.Equal(36, flver.Meshes[0].FaceSets[0].Indices.Count);

        // Inject one collapsed-index degenerate triangle and one collinear zero-area triangle
        var vBase = flver.Meshes[0].Vertices.Count;
        var n = Vector3.UnitY;
        var t = new Vector4(1, 0, 0, 1);
        var c = new FLVER.VertexColor(255, 255, 255, 255);
        static FLVER.Vertex MakeCollinear(Vector3 pos, Vector3 nrm, Vector4 tan, FLVER.VertexColor col)
        {
            var v = new FLVER.Vertex(1, 1, 1) { Position = pos, Normal = nrm, NormalW = 0 };
            v.UVs.Add(Vector3.Zero);
            v.Tangents.Add(tan);
            v.Colors.Add(col);
            return v;
        }
        flver.Meshes[0].Vertices.Add(MakeCollinear(new Vector3(0, 0, 0), n, t, c));
        flver.Meshes[0].Vertices.Add(MakeCollinear(new Vector3(1, 0, 0), n, t, c));
        flver.Meshes[0].Vertices.Add(MakeCollinear(new Vector3(2, 0, 0), n, t, c));

        // Collapsed index triangle (0, 0, 1) + collinear zero-area triangle
        flver.Meshes[0].FaceSets[0].Indices.AddRange([0, 0, 1, vBase, vBase + 1, vBase + 2]);

        var geom = DS3CollisionBuilder.ExtractFromFlver2(
            flver,
            Matrix4x4.Identity,
            defaultMaterialId: 1,
            flipWinding: true,
            weldVertices: true);

        Assert.Equal(8, geom.Vertices.Count);
        Assert.Equal(12, geom.TriangleCount);
        Assert.True(geom.Stats.DuplicateVerticesWeldedCount > 0);
        Assert.True(geom.Stats.DegenerateTrianglesCount >= 2);

        // Verify flipped winding vs non-flipped extraction matches opposite vertex ordering per triangle
        var unflipped = DS3CollisionBuilder.ExtractFromFlver2(
            flver,
            Matrix4x4.Identity,
            defaultMaterialId: 1,
            flipWinding: false,
            weldVertices: true);

        Assert.Equal(geom.TriangleCount, unflipped.TriangleCount);
        Assert.Equal(geom.Indices[1], unflipped.Indices[2]);
        Assert.Equal(geom.Indices[2], unflipped.Indices[1]);
    }

    [Fact]
    public void StandaloneDs3CollisionBuilder_RoundTripsHavok2014PackfileWithMaterialAndBvh()
    {
        var spec = new DS3PrimitiveSpec
        {
            Type = DS3PrimitiveType.Stairs,
            Width = 6.0f,
            Height = 4.0f,
            Depth = 10.0f,
            StairSteps = 8
        };

        var flver = DS3PrimitiveFlverBuilder.BuildFlver2("m31_90_00_00_000101", spec);
        const uint soilMaterialId = 3;
        var geom = DS3CollisionBuilder.ExtractFromFlver2(flver, Matrix4x4.Identity, defaultMaterialId: soilMaterialId);

        var (hiDoc, loDoc) = DS3CollisionBuilder.BuildHiAndLoDocuments(geom, "000101", defaultMaterialId: soilMaterialId);

        var rawHi = HavokFile.Write(hiDoc);
        var info = HavokFile.Sniff(rawHi);
        Assert.Equal(HavokContainer.Packfile, info.Container);
        Assert.Equal(HavokSdk.Hk2014, info.Sdk);
        Assert.Equal(8, info.PointerSize);
        Assert.False(info.BigEndian);

        var reloadedHi = HavokFile.Read(rawHi, HavokProfile.DS3);
        var submeshes = CollisionGeometry.Submeshes(reloadedHi.Objects);
        var decoded = Assert.Single(submeshes);
        Assert.Equal(geom.TriangleCount * 3, decoded.Count);

        var shape = reloadedHi.Objects.OfType<fsnpCustomParamCompressedMeshShape>().FirstOrDefault();
        Assert.NotNull(shape);
        Assert.NotNull(shape.m_pParam);
        Assert.Contains(shape.m_pParam.m_primitiveDataArray, pd => pd.m_materialNameData == soilMaterialId);
        Assert.NotNull(shape.m_data?.m_meshTree);
        Assert.NotEmpty(shape.m_data.m_meshTree.m_sections);
        Assert.NotEmpty(shape.m_data.m_meshTree.m_nodes);

        var hiReport = DS3CollisionValidator.ValidateDocument(hiDoc, "h31_90_00_00_000101.hkx.dcx", geom.TriangleCount);
        var loReport = DS3CollisionValidator.ValidateDocument(loDoc, "l31_90_00_00_000101.hkx.dcx", geom.TriangleCount);
        Assert.True(hiReport.IsValid, string.Join("; ", hiReport.Errors));
        Assert.True(loReport.IsValid, string.Join("; ", loReport.Errors));
    }

    [Fact]
    public void CollisionValidator_DetectsDegenerateInvalidIndicesAndCorruptShape()
    {
        var geomReport = new DS3CollisionValidationReport { TargetName = "BadGeometry" };
        var badVerts = new List<Vector3>
        {
            new(0, 0, 0),
            new(1, 0, 0),
            new(2, 0, 0),          // Collinear with 0,1 -> degenerate
            new(float.NaN, 0, 0),  // Non-finite
        };
        var badIndices = new List<int>
        {
            0, 1, 2,   // Zero-area triangle
            0, 1, 99   // Out-of-range vertex index
        };

        DS3CollisionValidator.ValidateSourceGeometry(badVerts, badIndices, geomReport);
        Assert.False(geomReport.IsValid);
        Assert.Contains(geomReport.Errors, e => e.Contains("non-finite", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(geomReport.Errors, e => e.Contains("out-of-range", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(geomReport.Errors, e => e.Contains("degenerate", StringComparison.OrdinalIgnoreCase));

        // Build valid doc then corrupt m_pParam and m_meshTree.m_sections
        var flver = DS3PrimitiveFlverBuilder.BuildFlver2("m31_90_00_00_000102", DS3PrimitiveFlverBuilder.CreateStarterFloorSpec());
        var geom = DS3CollisionBuilder.ExtractFromFlver2(flver, Matrix4x4.Identity);
        var (doc, _) = DS3CollisionBuilder.BuildHiAndLoDocuments(geom, "000102");

        var shape = doc.Objects.OfType<fsnpCustomParamCompressedMeshShape>().Single();
        shape.m_data.m_meshTree.m_sections.Clear();
        shape.m_pParam.m_triangleDataArray.Clear();

        var corruptReport = DS3CollisionValidator.ValidateDocument(doc, "CorruptTest");
        Assert.False(corruptReport.IsValid);
        Assert.NotEmpty(corruptReport.Errors);
    }

    [Fact]
    public void StandingBoxBundle_WritesToTempDiskAndReloadsCleanly()
    {
        const string mapId = "m31_90_00_00";
        var options = new DS3MapScaffoldOptions
        {
            MapId = mapId,
            PlayerSpawnPosition = new Vector3(0f, 1f, 0f),
            IncludeStarterBox = true,
            StarterBoxWidth = 24f,
            StarterBoxHeight = 1f,
            StarterBoxDepth = 24f,
            StarterPieceId6 = "000100",
            StarterCollisionMaterialId = DS3MapCreatorConstants.DefaultWalkableMaterialId
        };

        var bundle = DS3MapScaffolder.BuildStandingBoxBundle(options);
        Assert.Equal(mapId, bundle.MapId);
        Assert.NotNull(bundle.HiValidation);
        Assert.NotNull(bundle.LoValidation);
        Assert.True(bundle.HiValidation.IsValid, string.Join("; ", bundle.HiValidation.Errors));
        Assert.True(bundle.LoValidation.IsValid, string.Join("; ", bundle.LoValidation.Errors));

        var tempRoot = Path.Combine(Path.GetTempPath(), $"SmithboxDS3Tests_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempRoot);

        try
        {
            foreach (var f in bundle.Files)
            {
                var rel = DS3MapCreatorConstants.ToProjectRelativePath(f.VfsPath);
                var fullPath = Path.Combine(tempRoot, rel);
                Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
                File.WriteAllBytes(fullPath, f.Bytes);
            }

            // 1. MSB3 reload + Standing Box structural checks
            var msbPath = Path.Combine(tempRoot, DS3MapCreatorConstants.ToProjectRelativePath(DS3MapCreatorConstants.GetMsbVfsPath(mapId)));
            var msb = MSB3.Read(File.ReadAllBytes(msbPath));
            var player = Assert.Single(msb.Parts.Players);
            Assert.Equal("c0000_0000", player.Name);
            Assert.Equal(options.PlayerSpawnPosition, player.Position);

            var piece = Assert.Single(msb.Parts.MapPieces);
            Assert.Equal("m000100", piece.ModelName);

            var col = Assert.Single(msb.Parts.Collisions);
            Assert.Equal("h000100", col.ModelName);
            Assert.Equal(8, col.HitFilterID);
            Assert.Equal(Vector3.One, col.Scale);
            Assert.Contains(col.DrawGroups, g => g != 0);
            Assert.Contains(col.DispGroups, g => g != 0);
            Assert.Contains(Enumerable.Range(0, 8), i => (col.DrawGroups[i] & piece.DrawGroups[i]) != 0);

            // 2. BTL reload
            var btlPath = Path.Combine(tempRoot, DS3MapCreatorConstants.ToProjectRelativePath(DS3MapCreatorConstants.GetBtlVfsPath(mapId)));
            var btl = BTL.Read(File.ReadAllBytes(btlPath));
            Assert.Equal(6, btl.Version);

            // 3. GPARAM reload
            var gparamPath = Path.Combine(tempRoot, DS3MapCreatorConstants.ToProjectRelativePath(DS3MapCreatorConstants.GetGparamVfsPath(mapId)));
            var gparam = GPARAM.Read(File.ReadAllBytes(gparamPath));
            Assert.Equal(GPARAM.GparamVersion.V3, gparam.Version);

            // 4. MAPBND -> FLVER2 reload
            var mapBndPath = Path.Combine(tempRoot, DS3MapCreatorConstants.ToProjectRelativePath(
                DS3MapCreatorConstants.GetMapBndVfsPath(bundle.AssetMapId, "000100")));
            var mapBnd = BND4.Read(File.ReadAllBytes(mapBndPath));
            var flver = FLVER2.Read(Assert.Single(mapBnd.Files).Bytes);
            Assert.Equal(DS3MapCreatorConstants.Ds3FlverVersion, flver.Header.Version);
            Assert.InRange(flver.Header.BoundingBoxMax.Y, -1e-4f, 1e-4f);

            // 5. Hi and Lo BXF4 -> DCX -> HavokFile reload & full validator re-check
            foreach (var layer in new[] { "h", "l" })
            {
                var bhdPath = Path.Combine(tempRoot, DS3MapCreatorConstants.ToProjectRelativePath(DS3MapCreatorConstants.GetCollisionBhdVfsPath(mapId, layer)));
                var bdtPath = Path.Combine(tempRoot, DS3MapCreatorConstants.ToProjectRelativePath(DS3MapCreatorConstants.GetCollisionBdtVfsPath(mapId, layer)));
                var bxf = BXF4.Read(File.ReadAllBytes(bhdPath), File.ReadAllBytes(bdtPath));

                var hkxEntry = Assert.Single(bxf.Files);
                Assert.StartsWith($@"{layer}31_90_00_00\{layer}31_90_00_00_000100.hkx.dcx", hkxEntry.Name, StringComparison.OrdinalIgnoreCase);

                var rawHkx = DCX.Decompress(hkxEntry.Bytes).ToArray();
                var reloadedDoc = HavokFile.Read(rawHkx, HavokProfile.DS3);
                var valReport = DS3CollisionValidator.ValidateDocument(reloadedDoc, hkxEntry.Name, expectedTriangleCount: 12);
                Assert.True(valReport.IsValid, string.Join("; ", valReport.Errors));
            }
        }
        finally
        {
            if (Directory.Exists(tempRoot))
                Directory.Delete(tempRoot, recursive: true);
        }
    }

    [Fact]
    public void PrimitiveMapBnd_ReadableViaCollisionFlverReader_AndBuildsCollision()
    {
        var spec = new DS3PrimitiveSpec
        {
            Type = DS3PrimitiveType.BoxFloor,
            Width = 8.0f,
            Height = 1.0f,
            Depth = 8.0f,
            AnchorTopAtOrigin = false
        };

        const string assetMapId = "m31_90_00_00";
        const string id6 = "000101";
        var stem = $"{assetMapId}_{id6}";

        var flver = DS3PrimitiveFlverBuilder.BuildFlver2(stem, spec);
        var mapBndBytes = DS3PrimitiveFlverBuilder.PackMapBnd(assetMapId, id6, flver);

        var rawFlver = CollisionFlverReader.FindModelBytes(
            _ => mapBndBytes,
            $@"map\{assetMapId}\{stem}.mapbnd.dcx",
            stem);

        Assert.NotNull(rawFlver);
        var readFlver = FLVER2.Read(rawFlver);

        var geom = DS3CollisionBuilder.ExtractFromFlver2(readFlver, Matrix4x4.Identity);
        Assert.Equal(8, geom.Vertices.Count);
        Assert.Equal(12, geom.TriangleCount);

        var (hiDoc, loDoc) = DS3CollisionBuilder.BuildHiAndLoDocuments(geom, id6);
        var hiReport = DS3CollisionValidator.ValidateDocument(hiDoc, $"h31_90_00_00_{id6}.hkx.dcx", geom.TriangleCount);
        var loReport = DS3CollisionValidator.ValidateDocument(loDoc, $"l31_90_00_00_{id6}.hkx.dcx", geom.TriangleCount);

        Assert.True(hiReport.IsValid, string.Join("; ", hiReport.Errors));
        Assert.True(loReport.IsValid, string.Join("; ", loReport.Errors));
    }
}
