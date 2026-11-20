using Havok;
using Havok.Builders;
using Havok.Files;
using Havok.Formats.Packfile;
using Havok.hk2014;
using Havok.Shared;
using SoulsFormats;
using StudioCore.Application;
using StudioCore.Editors.Common;
using StudioCore.Editors.HavokEditor;
using StudioCore.Renderer;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;

namespace StudioCore.Editors.MapEditor.DS3Creator;

public sealed class DS3CollisionBuildResult
{
    public bool Success { get; set; }
    public string CollisionModelName { get; set; } = "";
    public string HiFileName { get; set; } = "";
    public string LoFileName { get; set; } = "";
    public string CollisionPartName { get; set; } = "";
    public int ExtractedTriangles { get; set; }
    public int ExtractedVertices { get; set; }
    public int StrippedDegenerates { get; set; }
    public string Error { get; set; }
    public DS3CollisionValidationReport HiValidation { get; set; }
    public DS3CollisionValidationReport LoValidation { get; set; }
}

/// <summary>
/// DS3 geometry extractor and standalone Havok 2014 (hk_2014.1.0-r1 64-bit Packfile / hknp) collision builder
/// + MSB3 collision registration pipeline (M1.4, M1.5, M1.6).
/// </summary>
public static class DS3CollisionBuilder
{
    /// <summary>
    /// Extracts collision geometry from a FLVER2 model (M1.4).
    /// Defaults to FlipWinding = true because DS3 FLVER2 faces use LH CCW while DS3 hknp collision uses RH CCW / LH CW.
    /// </summary>
    public static ModelCollisionGeometry ExtractFromFlver2(
        FLVER2 flver,
        Matrix4x4 worldTransform,
        uint defaultMaterialId = DS3MapCreatorConstants.DefaultWalkableMaterialId,
        bool flipWinding = true,
        bool weldVertices = true,
        float weldTolerance = 0.0001f)
    {
        return ModelCollisionExtractor.ExtractFromFlver(flver, CreateExtractionOptions(worldTransform, defaultMaterialId, flipWinding, weldVertices, weldTolerance));
    }

    public static ModelCollisionGeometry ExtractFromMultiPart(
        IReadOnlyList<ModelPartSource> parts,
        uint defaultMaterialId = DS3MapCreatorConstants.DefaultWalkableMaterialId,
        bool flipWinding = true,
        bool weldVertices = true,
        float weldTolerance = 0.0001f)
    {
        return ModelCollisionExtractor.ExtractFromParts(parts, CreateExtractionOptions(Matrix4x4.Identity, defaultMaterialId, flipWinding, weldVertices, weldTolerance));
    }

    private static ModelCollisionExtractionOptions CreateExtractionOptions(
        Matrix4x4 transform,
        uint defaultMaterialId,
        bool flipWinding,
        bool weldVertices,
        float weldTolerance)
    {
        return new ModelCollisionExtractionOptions
        {
            ApplyNodeTransforms = true,
            ObjectTransform = transform,
            WeldVertices = weldVertices,
            WeldTolerance = weldTolerance,
            WeldAcrossMaterials = true,
            RemoveDegenerateTriangles = true,
            DegenerateAreaEpsilon = DS3CollisionValidator.DefaultDegenerateAreaEpsilon,
            RemoveInvalidGeometry = true,
            FlipWinding = flipWinding,
            DefaultMaterialId = defaultMaterialId,
            ColorNormalIsNode = true
        };
    }

    /// <summary>
    /// Builds a complete standalone DS3 Havok 2014 hknp packfile document from raw geometry without needing any
    /// pre-existing template file (M1.5).
    /// </summary>
    public static HavokDocument BuildStandaloneDs3Document(
        IReadOnlyList<Vector3> vertices,
        IReadOnlyList<int> indices,
        IReadOnlyList<uint> triangleMaterials,
        string bodyName,
        uint defaultMaterialId = DS3MapCreatorConstants.DefaultWalkableMaterialId)
    {
        var shape = HavokCompressedMeshGenerator_hk2014.BuildCompressedMeshShape(
            vertices,
            indices,
            triangleMaterials,
            defaultMaterialId,
            HavokCompressedMeshGenerator_hk2014.Variant.DS3);

        var root = BuildRootContainer(shape, bodyName);

        // Round-trip once through HavokPackfileSerializer + HavokFile.Read so HavokDocument.Objects, Source header,
        // and serializer object layout are populated exactly like files loaded from disk.
        var serializer = new HavokPackfileSerializer(HavokFile.ClassSetFor(HavokProfile.DS3));
        var rawPackfile = serializer.Write(root);
        return HavokFile.Read(rawPackfile, HavokProfile.DS3);
    }

    public static (HavokDocument HiDoc, HavokDocument LoDoc) BuildHiAndLoDocuments(
        ModelCollisionGeometry geometry,
        string id6,
        uint defaultMaterialId = DS3MapCreatorConstants.DefaultWalkableMaterialId)
    {
        ArgumentNullException.ThrowIfNull(geometry);

        var hi = BuildStandaloneDs3Document(geometry.Vertices, geometry.Indices, geometry.TriangleMaterials, $"h{id6}", defaultMaterialId);
        var lo = BuildStandaloneDs3Document(geometry.Vertices, geometry.Indices, geometry.TriangleMaterials, $"l{id6}", defaultMaterialId);
        return (hi, lo);
    }

    public static hkRootLevelContainer BuildRootContainer(fsnpCustomParamCompressedMeshShape shape, string bodyName)
    {
        ArgumentNullException.ThrowIfNull(shape);

        var material = BuildDefaultDs3Material();
        var bodyCinfo = BuildDefaultDs3BodyCinfo(bodyName, shape);

        var physicsSystemData = new hknpPhysicsSystemData
        {
            m_name = "Default Physics System Data",
            m_materials = [material],
            m_motionProperties = new List<hknpMotionProperties>(),
            m_motionCinfos = new List<hknpMotionCinfo>(),
            m_bodyCinfos = [bodyCinfo],
            m_constraintCinfos = new List<hknpConstraintCinfo>(),
            m_referencedObjects = [shape]
        };

        var physicsSceneData = new hknpPhysicsSceneData
        {
            m_systemDatas = [physicsSystemData],
            m_worldCinfo = null
        };

        return new hkRootLevelContainer
        {
            m_namedVariants =
            [
                new hkRootLevelContainer.NamedVariant
                {
                    m_name = "Physics Scene Data",
                    m_className = "hknpPhysicsSceneData",
                    m_variant = physicsSceneData
                }
            ]
        };
    }

    public static hknpMaterial BuildDefaultDs3Material()
    {
        // Note: HavokPackfileSerializer encodes Size==2 hkHalf16 fields via (ushort)(SingleToInt32Bits(value) >> 16).
        // Decode the vanilla DS3 16-bit half patterns from hknpCollisionMeshBuilder.cs back into exact floats:
        // 16544 -> 5.0f, 16128 -> 0.5f, 16256 -> 1.0f, 16076 -> ~0.3984f, 15692 -> ~0.0498f.
        static float BFloat16FromBits(int bits16) => BitConverter.Int32BitsToSingle(bits16 << 16);

        return new hknpMaterial
        {
            m_name = "",
            m_isExclusive = 0,
            m_flags = 0,
            m_triggerType = hknpMaterial.TriggerType.TRIGGER_TYPE_NONE,
            m_triggerManifoldTolerance = new hkUFloat8 { m_value = 255 },
            m_dynamicFriction = BFloat16FromBits(16128),
            m_staticFriction = BFloat16FromBits(16128),
            m_restitution = BFloat16FromBits(16076),
            m_frictionCombinePolicy = hknpMaterial.CombinePolicy.COMBINE_MIN,
            m_restitutionCombinePolicy = hknpMaterial.CombinePolicy.COMBINE_MAX,
            m_weldingTolerance = BFloat16FromBits(15692),
            m_maxContactImpulse = 3.40282E+38F,
            m_fractionOfClippedImpulseToApply = 1f,
            m_massChangerCategory = hknpMaterial.MassChangerCategory.MASS_CHANGER_IGNORE,
            m_massChangerHeavyObjectFactor = BFloat16FromBits(16256),
            m_softContactForceFactor = 0f,
            m_softContactDampFactor = 0f,
            m_softContactSeperationVelocity = new hkUFloat8 { m_value = 0 },
            m_surfaceVelocity = null,
            m_disablingCollisionsBetweenCvxCvxDynamicObjectsDistance = BFloat16FromBits(16544),
            m_userData = 0,
            m_isShared = false
        };
    }

    public static hknpBodyCinfo BuildDefaultDs3BodyCinfo(string bodyName, hknpShape shape)
    {
        return new hknpBodyCinfo
        {
            m_shape = shape,
            m_reservedBodyId = 2147483647,
            m_motionId = 2147483647,
            m_qualityId = 255,
            m_materialId = 0,
            m_collisionFilterInfo = 0,
            m_flags = 1,
            m_collisionLookAheadDistance = 0f,
            m_name = bodyName ?? "",
            m_userData = 0,
            m_position = new Vector4(0f, 0f, 0f, 1f),
            m_orientation = Quaternion.Identity,
            m_spuFlags = 0,
            m_localFrame = null
        };
    }

    public static MSB3.Part.Collision BuildDs3CollisionPart(
        string partName,
        string modelName,
        Vector3 position,
        Vector3 rotation)
    {
        var part = new MSB3.Part.Collision
        {
            Name = partName,
            ModelName = modelName,
            SibPath = "",
            Position = position,
            Rotation = rotation,
            Scale = Vector3.One,
            EntityID = -1,
            HitFilterID = 8,
            SoundSpaceType = 0,
            MapNameID = -1,
            DisableStart = false,
            DisableBonfireEntityID = -1,
            MapVisType = 0,
            PlayRegionID = -1,
            LockCamID1 = -1,
            LockCamID2 = -1,
            GparamConfig = new MSB3.Part.GparamConfig(),
            SceneGparamConfig = new MSB3.Part.SceneGparamConfig
            {
                TransitionTime = -1f
            }
        };

        for (int i = 0; i < part.SceneGparamConfig.EventIDs.Length; i++)
            part.SceneGparamConfig.EventIDs[i] = -1;

        var full = DS3MapCreatorConstants.CreateFullGroupMask();
        Array.Copy(full, part.DrawGroups, 8);
        Array.Copy(full, part.DispGroups, 8);
        Array.Copy(full, part.BackreadGroups, 8);

        return part;
    }

    public static string ResolvePartModelName(MsbEntity entity)
    {
        if (entity == null)
            return null;

        var propName = entity.GetPartModelName();
        return !string.IsNullOrEmpty(propName) ? propName : entity.ModelName;
    }

    public static DS3CollisionBuildResult BuildAndRegisterForMapPieces(
        MapEditorView view,
        MapContainer map,
        IReadOnlyList<MsbEntity> mapPieces,
        string requestedId6 = null,
        uint materialId = DS3MapCreatorConstants.DefaultWalkableMaterialId,
        bool buildHi = true,
        bool buildLo = true,
        bool registerInMsb = true,
        bool flipWinding = true)
    {
        var result = new DS3CollisionBuildResult();

        if (view?.Project == null || map == null)
        {
            result.Error = "No active DS3 project or map loaded.";
            return result;
        }

        if (mapPieces == null || mapPieces.Count == 0)
        {
            result.Error = "Select at least one MapPiece entity to build collision from.";
            return result;
        }

        var project = view.Project;
        if (project.Descriptor.ProjectType is not ProjectType.DS3)
        {
            result.Error = "DS3 Collision Builder only supports Dark Souls 3 projects.";
            return result;
        }

        try
        {
            var anchor = mapPieces[0];
            var anchorScale = anchor.GetPropertyValue("Scale") is Vector3 s ? s : Vector3.One;
            static float Inv(float v) => MathF.Abs(v) > 1e-6f ? 1f / v : 1f;
            var anchorUnscaledWorld = Matrix4x4.CreateScale(Inv(anchorScale.X), Inv(anchorScale.Y), Inv(anchorScale.Z)) * anchor.GetWorldMatrix();
            var toAnchor = Matrix4x4.Invert(anchorUnscaledWorld, out var inv) ? inv : Matrix4x4.Identity;

            var parts = new List<ModelPartSource>();
            var failures = new List<string>();

            foreach (var entity in mapPieces)
            {
                var modelName = ResolvePartModelName(entity);
                if (string.IsNullOrEmpty(modelName))
                {
                    failures.Add($"{entity.Name}: empty ModelName");
                    continue;
                }

                var flver = ReadEntityFlver2(project, entity, out var readFailure);
                if (flver == null)
                {
                    failures.Add($"{entity.Name} ({modelName}): {readFailure}");
                    continue;
                }

                Matrix4x4 sourceTransform = ReferenceEquals(entity, anchor)
                    ? Matrix4x4.CreateScale(anchorScale)
                    : entity.GetWorldMatrix() * toAnchor;

                parts.Add(new ModelPartSource
                {
                    Name = entity.Name ?? modelName,
                    Flver2 = flver,
                    Transform = sourceTransform
                });
            }

            if (parts.Count == 0)
            {
                result.Error = failures.Count > 0
                    ? $"No readable FLVER2 model found: {string.Join(" | ", failures)}"
                    : "None of the selected MapPieces had a readable FLVER2 model.";
                return result;
            }

            var geometry = ExtractFromMultiPart(parts, materialId, flipWinding);
            result.ExtractedVertices = geometry.Vertices.Count;
            result.ExtractedTriangles = geometry.TriangleCount;
            result.StrippedDegenerates = geometry.Stats.DegenerateTrianglesCount;

            if (geometry.TriangleCount == 0 || geometry.Vertices.Count == 0)
            {
                result.Error = "Geometry extraction produced 0 valid non-degenerate triangles.";
                return result;
            }

            var id6 = NormalizeOrAllocateCollisionId6(view, map, requestedId6);
            var colModelName = $"h{id6}";
            var bank = view.HavokCollisionBank;

            if (buildHi)
            {
                var hiName = $"h{map.Name.Substring(1)}_{id6}.hkx.dcx";
                var hiDoc = BuildStandaloneDs3Document(geometry.Vertices, geometry.Indices, geometry.TriangleMaterials, $"h{id6}", materialId);
                var hiReport = DS3CollisionValidator.ValidateDocument(hiDoc, hiName, geometry.TriangleCount);
                result.HiValidation = hiReport;
                result.HiFileName = hiName;

                if (!hiReport.IsValid)
                {
                    result.Error = $"Hi-res collision validation failed: {string.Join("; ", hiReport.Errors)}";
                    return result;
                }

                bank.AddMapCollision(map.Name, "h", hiName, hiDoc);
            }

            if (buildLo)
            {
                var loName = $"l{map.Name.Substring(1)}_{id6}.hkx.dcx";
                var loDoc = BuildStandaloneDs3Document(geometry.Vertices, geometry.Indices, geometry.TriangleMaterials, $"l{id6}", materialId);
                var loReport = DS3CollisionValidator.ValidateDocument(loDoc, loName, geometry.TriangleCount);
                result.LoValidation = loReport;
                result.LoFileName = loName;

                if (!loReport.IsValid)
                {
                    result.Error = $"Lo-res collision validation failed: {string.Join("; ", loReport.Errors)}";
                    return result;
                }

                bank.AddMapCollision(map.Name, "l", loName, loDoc);
            }

            var saved = true;
            if (buildHi) saved &= bank.SaveMapCollision(map.Name, "h");
            if (buildLo) saved &= bank.SaveMapCollision(map.Name, "l");

            if (!saved)
            {
                result.Error = "Failed saving collision BXF4 binder(s) to project.";
                return result;
            }

            // Ensure new collision BXF4 entries are in Project.Locator.CollisionFiles too
            if (buildHi) RegisterCollisionBinderLocatorEntry(project, DS3MapCreatorConstants.GetCollisionBhdVfsPath(map.Name, "h"), $"h{map.Name.Substring(1)}");
            if (buildLo) RegisterCollisionBinderLocatorEntry(project, DS3MapCreatorConstants.GetCollisionBhdVfsPath(map.Name, "l"), $"l{map.Name.Substring(1)}");

            var assetMapId = PathBuilder.GetAssetMapID(project, map.Name);
            var assetName = ModelLocator.MapModelNameToAssetName(project, assetMapId, colModelName);
            foreach (var visibleType in bank.GetVisibleCollisionTypes())
            {
                var colAsset = ModelLocator.GetMapCollisionModel(project, assetMapId, assetName, false, visibleType);
                if (colAsset.IsValid())
                {
                    ResourceManager.DefaultPool.LoadAsset<HavokCollisionResource>(project, colAsset, new LoadOptions
                    {
                        JobName = LOC.Get("MAP_ResourceManager_Collision_Models"),
                        Types = ResourceType.CollisionHKX,
                        AccessLevel = AccessLevel.AccessGPUOptimizedOnly,
                        OnlyNames = [colAsset.Name],
                        Reload = true
                    });
                }
            }

            if (registerInMsb)
            {
                var anchorPos = anchor.GetPropertyValue("Position") is Vector3 p ? p : Vector3.Zero;
                var anchorRot = anchor.GetPropertyValue("Rotation") is Vector3 r ? r : Vector3.Zero;
                var colPartName = DS3PrimitiveFlverBuilder.AllocateUniquePartName(map, colModelName);
                var msbCol = BuildDs3CollisionPart(colPartName, colModelName, anchorPos, anchorRot);

                var colEntity = new MsbEntity(view.Universe, map, msbCol, MsbEntityType.Part)
                {
                    ModelName = colModelName
                };
                view.ViewportActionManager.ExecuteAction(
                    new EntAddAction(view, map, new List<MsbEntity> { colEntity }, map.RootObject));

                result.CollisionPartName = colPartName;
            }

            view.CollisionEditorTool?.OnCollisionCreated();
            result.CollisionModelName = colModelName;
            result.Success = true;
            return result;
        }
        catch (Exception ex)
        {
            result.Error = $"DS3 collision generation failed: {ex.Message}";
            Smithbox.LogError(typeof(DS3CollisionBuilder), result.Error, ex);
            return result;
        }
    }

    private static FLVER2 ReadEntityFlver2(ProjectEntry project, MsbEntity entity, out string failureReason)
    {
        failureReason = "";
        var model = ResolvePartModelName(entity);
        if (string.IsNullOrEmpty(model))
        {
            failureReason = "empty ModelName";
            return null;
        }

        ResourceDescriptor asset;
        string assetName;
        if (EntityHelper.IsPartMapPiece(entity))
        {
            var mapId = PathBuilder.GetAssetMapID(project, entity.MapID);
            assetName = ModelLocator.MapModelNameToAssetName(project, mapId, model);
            asset = ModelLocator.GetMapModel(project, mapId, assetName, assetName);
        }
        else
        {
            assetName = model;
            asset = ModelLocator.GetObjModel(project, model, model);
        }

        var containerPath = asset.GetContainerPath(project);
        if (string.IsNullOrEmpty(containerPath))
        {
            failureReason = $"unresolved container path for '{assetName}'";
            return null;
        }

        byte[] ReadFromVfs(string path)
        {
            if (string.IsNullOrEmpty(path))
                return null;

            var candidates = new[] { path, path.Replace('\\', '/') };
            foreach (var candidate in candidates)
            {
                if (project.VFS.ProjectFS.FileExists(candidate))
                {
                    var data = project.VFS.ProjectFS.ReadFile(candidate);
                    if (data != null) return data.Value.ToArray();
                }

                if (project.VFS.FS.FileExists(candidate))
                {
                    var data = project.VFS.FS.ReadFile(candidate);
                    if (data != null) return data.Value.ToArray();
                }
            }

            return null;
        }

        var raw = CollisionFlverReader.FindModelBytes(ReadFromVfs, containerPath, assetName);
        if (raw == null)
        {
            failureReason = $"model '{assetName}' not found/readable in '{containerPath}'";
            return null;
        }

        return FLVER2.Read(raw);
    }

    private static string NormalizeOrAllocateCollisionId6(MapEditorView view, MapContainer map, string requestedId6)
    {
        var naming = CollisionEditorNaming.For("DS3");
        if (!string.IsNullOrWhiteSpace(requestedId6))
        {
            var trimmed = requestedId6.Trim();
            if (naming.IsValid(trimmed))
                return naming.Normalize(trimmed);
        }

        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (view.HavokCollisionBank.MapCollisions.TryGetValue(map.Name, out var names))
        {
            foreach (var n in names)
            {
                if (naming.IdFromFile(n) is { } id)
                    used.Add(id);
            }
        }

        foreach (var e in map.Objects.OfType<MsbEntity>().Where(EntityHelper.IsPartCollision))
        {
            var colModel = ResolvePartModelName(e);
            if (!string.IsNullOrEmpty(colModel) && naming.IdFromModel(colModel) is { } id)
                used.Add(id);
        }

        return naming.Suggest(used);
    }

    public static void RegisterCollisionBinderLocatorEntry(ProjectEntry project, string bhdVfsPath, string stem)
    {
        var normalized = bhdVfsPath.StartsWith('/') ? bhdVfsPath : "/" + bhdVfsPath;
        if (project.Locator.CollisionFiles.Entries.Any(e => string.Equals(e.Path, normalized, StringComparison.OrdinalIgnoreCase)))
            return;

        var lastSlash = normalized.LastIndexOf('/');
        project.Locator.CollisionFiles.Entries.Add(new FileDictionaryEntry
        {
            Archive = "",
            Path = normalized,
            Folder = lastSlash > 0 ? normalized[..lastSlash] : "",
            Filename = stem,
            Extension = "hkxbhd",
            Validated = true
        });
    }
}
