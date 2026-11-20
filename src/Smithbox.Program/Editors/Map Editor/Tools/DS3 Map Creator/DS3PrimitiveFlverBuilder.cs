using SoulsFormats;
using StudioCore.Application;
using StudioCore.Editors.Common;
using StudioCore.Editors.MaterialEditor;
using StudioCore.Renderer;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;

namespace StudioCore.Editors.MapEditor.DS3Creator;

public enum DS3PrimitiveType
{
    BoxFloor = 0,
    Wall = 1,
    Ramp = 2,
    Stairs = 3
}

public sealed class DS3PrimitiveSpec
{
    public DS3PrimitiveType Type { get; set; } = DS3PrimitiveType.BoxFloor;
    public float Width { get; set; } = 20.0f;
    public float Height { get; set; } = 1.0f;
    public float Depth { get; set; } = 20.0f;
    public int StairSteps { get; set; } = 10;
    public float UvScale { get; set; } = 0.25f;

    /// <summary>
    /// When true (default for floors), anchors top surface at Y = 0 and extends down to Y = -Height so a player
    /// spawned at Y = 0..1 stands directly on top. When false (walls, ramps, stairs), rests bottom at Y = 0.
    /// </summary>
    public bool AnchorTopAtOrigin { get; set; } = false;

    public string MaterialName { get; set; } = "ds3_blockout_mat";
    public string MtdPath { get; set; } = DS3MapCreatorConstants.DefaultMtdPath;
    public string DiffuseTexture { get; set; } = DS3MapCreatorConstants.DefaultDiffuseTexture;
    public string NormalTexture { get; set; } = DS3MapCreatorConstants.DefaultNormalTexture;
    public string SpecularTexture { get; set; } = DS3MapCreatorConstants.DefaultSpecularTexture;
}

public sealed class DS3SpawnPrimitiveResult
{
    public bool Success { get; set; }
    public string MapPieceModelName { get; set; } = "";
    public string MapPiecePartName { get; set; } = "";
    public string MapBndVfsPath { get; set; } = "";
    public string CollisionModelName { get; set; } = "";
    public string Error { get; set; }
    public DS3CollisionValidationReport HiValidation { get; set; }
    public DS3CollisionValidationReport LoValidation { get; set; }
}

/// <summary>
/// Procedural DS3 blockout primitive generator producing watertight FLVER2 meshes, .mapbnd.dcx binders,
/// MSB3 MapPiece registrations, and optional companion Havok 2014 collisions (M1.3).
/// </summary>
public static class DS3PrimitiveFlverBuilder
{
    private readonly record struct FaceQuad(Vector3 A, Vector3 B, Vector3 C, Vector3 D, Vector3 Normal, Vector2 UvA, Vector2 UvB, Vector2 UvC, Vector2 UvD);

    public static DS3PrimitiveSpec CreateStarterFloorSpec(float width = 20.0f, float height = 1.0f, float depth = 20.0f)
    {
        return new DS3PrimitiveSpec
        {
            Type = DS3PrimitiveType.BoxFloor,
            Width = width,
            Height = height,
            Depth = depth,
            AnchorTopAtOrigin = true,
            UvScale = 0.25f
        };
    }

    public static (List<FLVER.Vertex> Vertices, List<int> Indices, Vector3 Min, Vector3 Max) BuildPrimitiveGeometry(DS3PrimitiveSpec spec)
    {
        ArgumentNullException.ThrowIfNull(spec);

        float w = MathF.Max(spec.Width, 0.2f);
        float h = MathF.Max(spec.Height, 0.2f);
        float d = MathF.Max(spec.Depth, 0.2f);
        float uvScale = MathF.Max(spec.UvScale, 0.001f);

        float yMin = spec.AnchorTopAtOrigin ? -h : 0.0f;
        float yMax = spec.AnchorTopAtOrigin ? 0.0f : h;

        var vertices = new List<FLVER.Vertex>();
        var indices = new List<int>();

        switch (spec.Type)
        {
            case DS3PrimitiveType.BoxFloor:
            case DS3PrimitiveType.Wall:
                AppendBoxQuads(-w * 0.5f, w * 0.5f, yMin, yMax, -d * 0.5f, d * 0.5f, uvScale, vertices, indices);
                break;

            case DS3PrimitiveType.Ramp:
                AppendRamp(-w * 0.5f, w * 0.5f, yMin, yMax, -d * 0.5f, d * 0.5f, uvScale, vertices, indices);
                break;

            case DS3PrimitiveType.Stairs:
                int steps = Math.Clamp(spec.StairSteps, 2, 64);
                AppendStairs(-w * 0.5f, w * 0.5f, yMin, yMax, -d * 0.5f, d * 0.5f, steps, uvScale, vertices, indices);
                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(spec.Type));
        }

        ComputeBounds(vertices, out var min, out var max);
        return (vertices, indices, min, max);
    }

    public static FLVER2 BuildFlver2(string rootNodeName, DS3PrimitiveSpec spec, MaterialBank materialBank = null)
    {
        ArgumentNullException.ThrowIfNull(spec);

        var (vertices, indices, min, max) = BuildPrimitiveGeometry(spec);
        bool requiresFloatUv = vertices.Any(v =>
            v.UVs.Count > 0 && (MathF.Abs(v.UVs[0].X) >= 15.0f || MathF.Abs(v.UVs[0].Y) >= 15.0f));

        var layout = new FLVER2.BufferLayout
        {
            new FLVER.LayoutMember(FLVER.LayoutType.Float3, FLVER.LayoutSemantic.Position, 0),
            new FLVER.LayoutMember(FLVER.LayoutType.UByte4Norm, FLVER.LayoutSemantic.Normal, 0),
            new FLVER.LayoutMember(FLVER.LayoutType.UByte4Norm, FLVER.LayoutSemantic.Tangent, 0),
            new FLVER.LayoutMember(FLVER.LayoutType.Color, FLVER.LayoutSemantic.VertexColor, 0),
            new FLVER.LayoutMember(
                requiresFloatUv ? FLVER.LayoutType.Float2 : FLVER.LayoutType.Short2,
                FLVER.LayoutSemantic.UV, 0)
        };

        var material = BuildMaterial(spec, materialBank);

        var flver = new FLVER2
        {
            Header = new FLVER2.FLVERHeader
            {
                BigEndian = false,
                Version = DS3MapCreatorConstants.Ds3FlverVersion,
                Unicode = true,
                Unk4C = 0xFFFF,
                BoundingBoxMin = min,
                BoundingBoxMax = max
            }
        };

        flver.Nodes.Add(new FLVER.Node
        {
            Name = rootNodeName,
            ParentIndex = -1,
            FirstChildIndex = -1,
            NextSiblingIndex = -1,
            PreviousSiblingIndex = -1,
            Scale = Vector3.One,
            BoundingBoxMin = min,
            BoundingBoxMax = max
        });

        flver.Materials.Add(material);
        flver.BufferLayouts.Add(layout);

        var mesh = new FLVER2.Mesh
        {
            UseBoneWeights = false,
            MaterialIndex = 0,
            NodeIndex = 0,
            BoundingBox = new FLVER2.Mesh.BoundingBoxes
            {
                Min = min,
                Max = max
            }
        };

        mesh.BoneIndices.Add(0);
        mesh.Vertices.AddRange(vertices);
        mesh.VertexBuffers.Add(new FLVER2.VertexBuffer(0));
        mesh.FaceSets.Add(new FLVER2.FaceSet(
            FLVER2.FaceSet.FSFlags.None,
            triangleStrip: false,
            cullBackfaces: true,
            unk06: 0,
            indices: indices));

        flver.Meshes.Add(mesh);
        return flver;
    }

    public static byte[] PackMapBnd(string assetMapId, string id6, FLVER2 flver)
    {
        ArgumentNullException.ThrowIfNull(flver);

        var stem = $"{assetMapId}_{id6}";
        var flverBytes = flver.Write(DCX.Type.None);

        var binder = new BND4
        {
            Version = DS3MapCreatorConstants.Ds3BinderVersion,
            Format = Binder.Format.IDs | Binder.Format.Names1 | Binder.Format.Names2 | Binder.Format.Compression,
            Unicode = true,
            Extended = 4,
            Compression = DS3MapCreatorConstants.Ds3DcxType
        };

        binder.Files.Add(new BinderFile(
            Binder.FileFlags.Flag1,
            200,
            $@"N:\FDP\data\Model\map\{assetMapId}\MapPiece\{stem}.flver",
            flverBytes)
        {
            CompressionType = DCX.Type.None
        });

        return binder.Write();
    }

    public static DS3SpawnPrimitiveResult SpawnPrimitiveInMap(
        MapEditorView view,
        MapContainer map,
        DS3PrimitiveSpec spec,
        Vector3 worldPosition,
        bool alsoCreateCollision = true,
        uint collisionMaterialId = DS3MapCreatorConstants.DefaultWalkableMaterialId)
    {
        var result = new DS3SpawnPrimitiveResult();

        if (view?.Project == null || map == null || spec == null)
        {
            result.Error = "No active map or project loaded.";
            return result;
        }

        if (view.Project.Descriptor.ProjectType is not ProjectType.DS3)
        {
            result.Error = "DS3 Primitive Spawner only targets Dark Souls 3 projects.";
            return result;
        }

        try
        {
            var project = view.Project;
            var assetMapId = DS3MapCreatorConstants.GetAssetMapId(map.Name);
            var id6 = AllocateNextMapPieceId6(project, map, assetMapId);
            var stem = $"{assetMapId}_{id6}";
            var modelName = $"m{id6}";
            var partName = AllocateUniquePartName(map, modelName);

            var materialBank = project.Handler.MaterialData?.PrimaryBank;
            var flver = BuildFlver2(stem, spec, materialBank);
            var mapBndBytes = PackMapBnd(assetMapId, id6, flver);

            var vfsPath = DS3MapCreatorConstants.GetMapBndVfsPath(assetMapId, id6);
            var relPath = DS3MapCreatorConstants.ToProjectRelativePath(vfsPath);

            project.VFS.ProjectFS.WriteFile(relPath, mapBndBytes);
            RegisterMapPieceEntry(project, vfsPath, stem);

            // Request immediate GPU reload of the new MapPiece FLVER resource
            var assetDesc = ModelLocator.GetMapModel(project, assetMapId, stem, stem);
            if (assetDesc.IsValid())
            {
                ResourceManager.DefaultPool.LoadAsset<FlverResource>(project, assetDesc, new LoadOptions
                {
                    JobName = $"DS3 Primitive ({stem})",
                    Types = ResourceType.Flver,
                    AccessLevel = AccessLevel.AccessGPUOptimizedOnly,
                    Reload = true
                });
            }

            // Force map SYSTEX load in case it wasn't loaded yet
            var systex = TextureLocator.GetSystexTextureVirtualPath(project, "systex");
            if (systex.IsValid())
            {
                ResourceManager.DefaultPool.LoadAsset<TextureResource>(project, systex, new LoadOptions
                {
                    JobName = "DS3 System Textures",
                    Types = ResourceType.Texture,
                    AccessLevel = AccessLevel.AccessGPUOptimizedOnly
                });
            }

            var msbPart = BuildDs3MapPiecePart(partName, modelName, worldPosition);
            var entity = new MsbEntity(view.Universe, map, msbPart, MsbEntityType.Part)
            {
                ModelName = modelName
            };

            view.ViewportActionManager.ExecuteAction(
                new EntAddAction(view, map, new List<MsbEntity> { entity }, map.RootObject));

            result.MapPieceModelName = modelName;
            result.MapPiecePartName = partName;
            result.MapBndVfsPath = vfsPath;

            if (alsoCreateCollision)
            {
                var colResult = DS3CollisionBuilder.BuildAndRegisterForMapPieces(
                    view,
                    map,
                    [entity],
                    requestedId6: id6,
                    materialId: collisionMaterialId,
                    buildHi: true,
                    buildLo: true,
                    registerInMsb: true,
                    flipWinding: true);

                if (!colResult.Success)
                {
                    result.Error = colResult.Error ?? "Primitive spawned, but collision generation failed.";
                    return result;
                }

                result.CollisionModelName = colResult.CollisionModelName;
                result.HiValidation = colResult.HiValidation;
                result.LoValidation = colResult.LoValidation;
            }

            result.Success = true;
            return result;
        }
        catch (Exception ex)
        {
            result.Error = $"Failed to spawn primitive: {ex.Message}";
            Smithbox.LogError(typeof(DS3PrimitiveFlverBuilder), result.Error, ex);
            return result;
        }
    }

    public static MSB3.Part.MapPiece BuildDs3MapPiecePart(string partName, string modelName, Vector3 position)
    {
        return new MSB3.Part.MapPiece
        {
            Name = partName,
            ModelName = modelName,
            SibPath = "",
            Position = position,
            Rotation = Vector3.Zero,
            Scale = Vector3.One,
            EntityID = -1,
            GparamConfig = new MSB3.Part.GparamConfig()
        };
    }

    public static string AllocateNextMapPieceId6(ProjectEntry project, MapContainer map, string assetMapId, int startNumber = 100)
    {
        var used = new HashSet<string>(MapPiecePort.ExistingStems(project, assetMapId), StringComparer.OrdinalIgnoreCase);

        if (map != null)
        {
            foreach (var obj in map.Objects.OfType<MsbEntity>().Where(EntityHelper.IsPartMapPiece))
            {
                var mName = DS3CollisionBuilder.ResolvePartModelName(obj);
                if (!string.IsNullOrEmpty(mName) && mName.Length == 7 && mName[0] is 'm' or 'M')
                    used.Add($"{assetMapId}_{mName.Substring(1)}");
            }
        }

        for (int n = Math.Max(0, startNumber); n < 900000; n++)
        {
            var id6 = n.ToString("D6");
            if (!used.Contains($"{assetMapId}_{id6}"))
                return id6;
        }

        return "899999";
    }

    public static string AllocateUniquePartName(MapContainer map, string modelName)
    {
        var names = map.Objects.Select(o => o.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < 10000; i++)
        {
            var candidate = $"{modelName}_{i:D4}";
            if (!names.Contains(candidate))
                return candidate;
        }

        return $"{modelName}_9999";
    }

    public static void RegisterMapPieceEntry(ProjectEntry project, string vfsPath, string stem)
    {
        var normalized = vfsPath.StartsWith('/') ? vfsPath : "/" + vfsPath;
        var entries = project.Locator.MapPieceFiles.Entries;
        if (entries.Any(e => string.Equals(e.Path, normalized, StringComparison.OrdinalIgnoreCase)))
            return;

        var lastSlash = normalized.LastIndexOf('/');
        entries.Add(new FileDictionaryEntry
        {
            Archive = "",
            Path = normalized,
            Folder = lastSlash > 0 ? normalized[..lastSlash] : "",
            Filename = stem,
            Extension = "mapbnd",
            Validated = true
        });
    }

    private static FLVER2.Material BuildMaterial(DS3PrimitiveSpec spec, MaterialBank materialBank)
    {
        var mtdPath = string.IsNullOrWhiteSpace(spec.MtdPath) ? DS3MapCreatorConstants.DefaultMtdPath : spec.MtdPath.Trim();
        var material = new FLVER2.Material(spec.MaterialName, mtdPath, 0)
        {
            GXIndex = -1,
            Index = 0
        };

        var mtdShortName = Path.GetFileNameWithoutExtension(mtdPath.Replace('\\', '/'));
        MTD resolvedMtd = materialBank?.GetMaterial(mtdShortName);

        if (resolvedMtd != null && resolvedMtd.Textures != null && resolvedMtd.Textures.Count > 0)
        {
            foreach (var texSlot in resolvedMtd.Textures)
            {
                var param = texSlot.Type ?? "";
                if (param.Contains("DOL", StringComparison.OrdinalIgnoreCase))
                    continue;

                var chosenTex = ResolveDefaultTextureForParam(param, spec);
                material.Textures.Add(new FLVER2.Texture
                {
                    ParamName = param,
                    Path = chosenTex,
                    TilingScale = Vector2.One,
                    TilingTypeU = FLVER2.Texture.TilingType.Repeat,
                    TilingTypeV = FLVER2.Texture.TilingType.Repeat
                });
            }
        }

        if (material.Textures.Count == 0)
        {
            material.Textures.Add(new FLVER2.Texture
            {
                ParamName = "g_DiffuseTexture",
                Path = spec.DiffuseTexture,
                TilingScale = Vector2.One,
                TilingTypeU = FLVER2.Texture.TilingType.Repeat,
                TilingTypeV = FLVER2.Texture.TilingType.Repeat
            });
            material.Textures.Add(new FLVER2.Texture
            {
                ParamName = "g_BumpmapTexture",
                Path = spec.NormalTexture,
                TilingScale = Vector2.One,
                TilingTypeU = FLVER2.Texture.TilingType.Repeat,
                TilingTypeV = FLVER2.Texture.TilingType.Repeat
            });
            material.Textures.Add(new FLVER2.Texture
            {
                ParamName = "g_SpecularTexture",
                Path = spec.SpecularTexture,
                TilingScale = Vector2.One,
                TilingTypeU = FLVER2.Texture.TilingType.Repeat,
                TilingTypeV = FLVER2.Texture.TilingType.Repeat
            });
        }

        return material;
    }

    private static string ResolveDefaultTextureForParam(string paramName, DS3PrimitiveSpec spec)
    {
        var upper = paramName.ToUpperInvariant();
        if (upper.Contains("BUMP") || upper.Contains("NORMAL"))
            return spec.NormalTexture;
        if (upper.Contains("SPECULAR") || upper.Contains("SHININESS"))
            return spec.SpecularTexture;
        if (upper.Contains("DETAIL"))
            return spec.NormalTexture;
        return spec.DiffuseTexture;
    }

    private static void AppendBoxQuads(
        float x0, float x1, float y0, float y1, float z0, float z1,
        float uvScale, List<FLVER.Vertex> vertices, List<int> indices)
    {
        var p000 = new Vector3(x0, y0, z0);
        var p100 = new Vector3(x1, y0, z0);
        var p101 = new Vector3(x1, y0, z1);
        var p001 = new Vector3(x0, y0, z1);

        var p010 = new Vector3(x0, y1, z0);
        var p110 = new Vector3(x1, y1, z0);
        var p111 = new Vector3(x1, y1, z1);
        var p011 = new Vector3(x0, y1, z1);

        float w = (x1 - x0) * uvScale;
        float h = (y1 - y0) * uvScale;
        float d = (z1 - z0) * uvScale;

        // Top (+Y) & Bottom (-Y)
        AddQuadOutward(vertices, indices, p010, p110, p111, p011, Vector3.UnitY,
            new(0, 0), new(w, 0), new(w, d), new(0, d));
        AddQuadOutward(vertices, indices, p000, p001, p101, p100, -Vector3.UnitY,
            new(0, 0), new(0, d), new(w, d), new(w, 0));

        // Front (-Z) & Back (+Z)
        AddQuadOutward(vertices, indices, p000, p100, p110, p010, -Vector3.UnitZ,
            new(0, h), new(w, h), new(w, 0), new(0, 0));
        AddQuadOutward(vertices, indices, p101, p001, p011, p111, Vector3.UnitZ,
            new(0, h), new(w, h), new(w, 0), new(0, 0));

        // Left (-X) & Right (+X)
        AddQuadOutward(vertices, indices, p001, p000, p010, p011, -Vector3.UnitX,
            new(0, h), new(d, h), new(d, 0), new(0, 0));
        AddQuadOutward(vertices, indices, p100, p101, p111, p110, Vector3.UnitX,
            new(0, h), new(d, h), new(d, 0), new(0, 0));
    }

    private static void AppendRamp(
        float x0, float x1, float y0, float y1, float z0, float z1,
        float uvScale, List<FLVER.Vertex> vertices, List<int> indices)
    {
        // Wedge rising along +Z from y0 at z0 to y1 at z1
        var b00 = new Vector3(x0, y0, z0);
        var b10 = new Vector3(x1, y0, z0);
        var b11 = new Vector3(x1, y0, z1);
        var b01 = new Vector3(x0, y0, z1);

        var t11 = new Vector3(x1, y1, z1);
        var t01 = new Vector3(x0, y1, z1);

        float w = (x1 - x0) * uvScale;
        float h = (y1 - y0) * uvScale;
        float d = (z1 - z0) * uvScale;
        float slopeLen = MathF.Sqrt((y1 - y0) * (y1 - y0) + (z1 - z0) * (z1 - z0)) * uvScale;

        var slopeNormal = Vector3.Normalize(new Vector3(0f, z1 - z0, -(y1 - y0)));

        // Slope face
        AddQuadOutward(vertices, indices, b00, b10, t11, t01, slopeNormal,
            new(0, 0), new(w, 0), new(w, slopeLen), new(0, slopeLen));

        // Bottom (-Y)
        AddQuadOutward(vertices, indices, b00, b01, b11, b10, -Vector3.UnitY,
            new(0, 0), new(0, d), new(w, d), new(w, 0));

        // Back (+Z)
        AddQuadOutward(vertices, indices, b11, b01, t01, t11, Vector3.UnitZ,
            new(0, h), new(w, h), new(w, 0), new(0, 0));

        // Left (-X) & Right (+X) triangular side caps
        AddTriangleOutward(vertices, indices, b00, t01, b01, -Vector3.UnitX,
            new(0, h), new(d, 0), new(d, h));
        AddTriangleOutward(vertices, indices, b10, b11, t11, Vector3.UnitX,
            new(0, h), new(d, h), new(d, 0));
    }

    private static void AppendStairs(
        float x0, float x1, float y0, float y1, float z0, float z1,
        int steps, float uvScale, List<FLVER.Vertex> vertices, List<int> indices)
    {
        float totalH = y1 - y0;
        float totalD = z1 - z0;
        float stepH = totalH / steps;
        float stepD = totalD / steps;
        float w = (x1 - x0) * uvScale;

        // Solid back (+Z) and full bottom (-Y)
        var bl0 = new Vector3(x0, y0, z0);
        var br0 = new Vector3(x1, y0, z0);
        var bl1 = new Vector3(x0, y0, z1);
        var br1 = new Vector3(x1, y0, z1);
        var tl1 = new Vector3(x0, y1, z1);
        var tr1 = new Vector3(x1, y1, z1);

        AddQuadOutward(vertices, indices, bl0, bl1, br1, br0, -Vector3.UnitY,
            new(0, 0), new(0, totalD * uvScale), new(w, totalD * uvScale), new(w, 0));
        AddQuadOutward(vertices, indices, br1, bl1, tl1, tr1, Vector3.UnitZ,
            new(0, totalH * uvScale), new(w, totalH * uvScale), new(w, 0), new(0, 0));

        for (int i = 0; i < steps; i++)
        {
            float sz0 = z0 + i * stepD;
            float sz1 = sz0 + stepD;
            float syBottom = y0 + i * stepH;
            float syTop = y0 + (i + 1) * stepH;

            // Riser (-Z)
            AddQuadOutward(vertices, indices,
                new Vector3(x0, syBottom, sz0),
                new Vector3(x1, syBottom, sz0),
                new Vector3(x1, syTop, sz0),
                new Vector3(x0, syTop, sz0),
                -Vector3.UnitZ,
                new(0, stepH * uvScale), new(w, stepH * uvScale), new(w, 0), new(0, 0));

            // Tread (+Y)
            AddQuadOutward(vertices, indices,
                new Vector3(x0, syTop, sz0),
                new Vector3(x1, syTop, sz0),
                new Vector3(x1, syTop, sz1),
                new Vector3(x0, syTop, sz1),
                Vector3.UnitY,
                new(0, 0), new(w, 0), new(w, stepD * uvScale), new(0, stepD * uvScale));

            // Side Skirt slice on -X and +X down to base y0 so sides are watertight without T-junction gaps
            AddQuadOutward(vertices, indices,
                new Vector3(x0, y0, sz1),
                new Vector3(x0, y0, sz0),
                new Vector3(x0, syTop, sz0),
                new Vector3(x0, syTop, sz1),
                -Vector3.UnitX,
                new(0, (syTop - y0) * uvScale), new(stepD * uvScale, (syTop - y0) * uvScale),
                new(stepD * uvScale, 0), new(0, 0));

            AddQuadOutward(vertices, indices,
                new Vector3(x1, y0, sz0),
                new Vector3(x1, y0, sz1),
                new Vector3(x1, syTop, sz1),
                new Vector3(x1, syTop, sz0),
                Vector3.UnitX,
                new(0, (syTop - y0) * uvScale), new(stepD * uvScale, (syTop - y0) * uvScale),
                new(stepD * uvScale, 0), new(0, 0));
        }
    }

    private static void AddQuadOutward(
        List<FLVER.Vertex> vertices,
        List<int> indices,
        Vector3 a, Vector3 b, Vector3 c, Vector3 d,
        Vector3 outwardNormal,
        Vector2 uvA, Vector2 uvB, Vector2 uvC, Vector2 uvD)
    {
        AddTriangleOutward(vertices, indices, a, b, c, outwardNormal, uvA, uvB, uvC);
        AddTriangleOutward(vertices, indices, a, c, d, outwardNormal, uvA, uvC, uvD);
    }

    private static void AddTriangleOutward(
        List<FLVER.Vertex> vertices,
        List<int> indices,
        Vector3 a, Vector3 b, Vector3 c,
        Vector3 outwardNormal,
        Vector2 uvA, Vector2 uvB, Vector2 uvC)
    {
        var n = Vector3.Normalize(outwardNormal);
        var tangentDir = Vector3.Normalize(b - a);
        if (!float.IsFinite(tangentDir.X) || tangentDir.LengthSquared() < 1e-6f)
            tangentDir = MathF.Abs(n.Y) < 0.9f ? Vector3.Normalize(Vector3.Cross(n, Vector3.UnitY)) : Vector3.UnitX;
        else
            tangentDir = Vector3.Normalize(tangentDir - Vector3.Dot(tangentDir, n) * n);

        var tangent = new Vector4(tangentDir, 1.0f);
        var color = new FLVER.VertexColor(255, 255, 255, 255);

        int idxA = vertices.Count;
        vertices.Add(MakeVertex(a, n, tangent, color, uvA));
        int idxB = vertices.Count;
        vertices.Add(MakeVertex(b, n, tangent, color, uvB));
        int idxC = vertices.Count;
        vertices.Add(MakeVertex(c, n, tangent, color, uvC));

        // DS3 FLVER2 FrontFace is VkFrontFace.CounterClockwise in Smithbox's LH viewport:
        // outward faces satisfy Dot(Cross(pC - pA, pB - pA), n) > 0.
        var lhCross = Vector3.Cross(c - a, b - a);
        if (Vector3.Dot(lhCross, n) >= 0.0f)
        {
            indices.Add(idxA);
            indices.Add(idxB);
            indices.Add(idxC);
        }
        else
        {
            indices.Add(idxA);
            indices.Add(idxC);
            indices.Add(idxB);
        }
    }

    private static FLVER.Vertex MakeVertex(Vector3 pos, Vector3 normal, Vector4 tangent, FLVER.VertexColor color, Vector2 uv)
    {
        var v = new FLVER.Vertex(1, 1, 1)
        {
            Position = pos,
            Normal = normal,
            NormalW = 0
        };
        v.UVs.Add(new Vector3(uv.X, uv.Y, 0.0f));
        v.Tangents.Add(tangent);
        v.Colors.Add(color);
        return v;
    }

    private static void ComputeBounds(IReadOnlyList<FLVER.Vertex> vertices, out Vector3 min, out Vector3 max)
    {
        min = new Vector3(float.MaxValue);
        max = new Vector3(float.MinValue);

        foreach (var v in vertices)
        {
            min = Vector3.Min(min, v.Position);
            max = Vector3.Max(max, v.Position);
        }

        if (vertices.Count == 0)
        {
            min = Vector3.Zero;
            max = Vector3.Zero;
        }
    }
}
