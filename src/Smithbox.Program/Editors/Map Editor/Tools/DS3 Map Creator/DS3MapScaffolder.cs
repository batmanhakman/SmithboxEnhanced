using Havok;
using SoulsFormats;
using StudioCore.Application;
using StudioCore.Editors.Common;
using StudioCore.Editors.MaterialEditor;
using StudioCore.Renderer;
using StudioCore.Utilities;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Numerics;

namespace StudioCore.Editors.MapEditor.DS3Creator;

public sealed class DS3MapScaffoldOptions
{
    public string MapId { get; set; } = "m31_90_00_00";
    public Vector3 PlayerSpawnPosition { get; set; } = new(0.0f, 1.0f, 0.0f);
    public bool IncludeStarterBox { get; set; } = true;
    public float StarterBoxWidth { get; set; } = 24.0f;
    public float StarterBoxHeight { get; set; } = 1.0f;
    public float StarterBoxDepth { get; set; } = 24.0f;
    public string StarterPieceId6 { get; set; } = "000100";
    public uint StarterCollisionMaterialId { get; set; } = DS3MapCreatorConstants.DefaultWalkableMaterialId;
    public bool OverwriteExisting { get; set; } = false;
    public bool LoadImmediately { get; set; } = true;
}

public sealed class DS3ScaffoldedFile
{
    public string VfsPath { get; init; } = "";
    public byte[] Bytes { get; init; } = Array.Empty<byte>();
}

public sealed class DS3StandingBoxBundle
{
    public string MapId { get; init; } = "";
    public string AssetMapId { get; init; } = "";
    public string GparamStem { get; init; } = "";
    public string StarterPieceStem { get; init; } = "";
    public string StarterPieceModelName { get; init; } = "";
    public string StarterCollisionModelName { get; init; } = "";
    public List<DS3ScaffoldedFile> Files { get; } = new();
    public DS3CollisionValidationReport HiValidation { get; set; }
    public DS3CollisionValidationReport LoValidation { get; set; }
}

public sealed class DS3ScaffoldResult
{
    public bool Success { get; set; }
    public string MapId { get; set; } = "";
    public List<string> WrittenVfsPaths { get; } = new();
    public string Error { get; set; }
    public DS3CollisionValidationReport HiValidation { get; set; }
    public DS3CollisionValidationReport LoValidation { get; set; }
}

/// <summary>
/// Generates valid Dark Souls 3 map boilerplate (MSB3, baseline GPARAM, empty BTL, empty or populated h/l BXF4
/// collision binders, and optional "Standing Box" starter geometry) inside an active DS3 project (M1.1, M1.2).
/// </summary>
public static class DS3MapScaffolder
{
    private static readonly string[] BaselineGparamCandidateVfsPaths =
    [
        "/param/drawparam/m30_00_0000.gparam.dcx",
        "/param/drawparam/m40_00_0000.gparam.dcx",
        "/param/drawparam/mapbase.gparam.dcx",
        "/param/drawparam/default.gparam.dcx"
    ];

    public static MSB3 BuildMinimalMsb3(Vector3 playerSpawnPosition)
    {
        var msb = new MSB3
        {
            Compression = DS3MapCreatorConstants.Ds3DcxType
        };

        msb.Models.Players.Add(new MSB3.Model.Player
        {
            Name = "c0000",
            SibPath = @"N:\FDP\data\Model\chr\c0000\sib\c0000.sib"
        });

        msb.Parts.Players.Add(new MSB3.Part.Player
        {
            Name = "c0000_0000",
            ModelName = "c0000",
            SibPath = "",
            Position = playerSpawnPosition,
            Rotation = Vector3.Zero,
            Scale = Vector3.One,
            EntityID = -1
        });

        return msb;
    }

    public static BTL BuildEmptyBtl()
    {
        return new BTL
        {
            Version = DS3MapCreatorConstants.Ds3BtlVersion,
            LightSize = DS3MapCreatorConstants.Ds3BtlLightSize,
            LongOffsets = true,
            Compression = DS3MapCreatorConstants.Ds3DcxType,
            Lights = new List<BTL.Light>()
        };
    }

    public static GPARAM BuildBaselineGparam(Func<string, Memory<byte>?> tryReadVfs = null)
    {
        if (tryReadVfs != null)
        {
            foreach (var candidate in BaselineGparamCandidateVfsPaths)
            {
                try
                {
                    var data = tryReadVfs(candidate);
                    if (data is { Length: > 0 } raw)
                    {
                        var cloned = GPARAM.Read(raw);
                        cloned.Version = GPARAM.GparamVersion.V3;
                        cloned.WideStrings = true;
                        cloned.Unk0D = true;
                        cloned.Compression = DS3MapCreatorConstants.Ds3DcxType;
                        return cloned;
                    }
                }
                catch
                {
                    // Fall back to synthesized GPARAM below if a candidate cannot be parsed.
                }
            }
        }

        var gparam = new GPARAM
        {
            Version = GPARAM.GparamVersion.V3,
            WideStrings = true,
            Unk0D = true,
            Count14 = 0,
            Data30 = Array.Empty<byte>(),
            UnkParamExtras = new List<GPARAM.UnkParamExtra>(),
            Unk40 = 1.0f,
            Compression = DS3MapCreatorConstants.Ds3DcxType
        };

        var lightSet = new GPARAM.Param
        {
            Key = "LightSet ParamEditor",
            Name = "Light Set",
            Comments = new List<string>()
        };
        lightSet.Fields.Add(CreateVector2Field("Directional Light Angle0", "Directional Light: Angle: 0",
            (0, new Vector2(0.75f, 0.75f)), (100, new Vector2(0.75f, 0.75f))));
        lightSet.Fields.Add(CreateVector4Field("Directional Light DiffColor0", "Directional Light: Diffuse Color: 0",
            (0, new Vector4(1.0f, 0.96f, 0.90f, 2.5f)), (100, new Vector4(1.0f, 0.96f, 0.90f, 2.5f))));
        lightSet.Fields.Add(CreateVector4Field("Directional Light SpecColor0", "Directional Light: Specular Color: 0",
            (0, new Vector4(1.0f, 0.98f, 0.94f, 1.5f)), (100, new Vector4(1.0f, 0.98f, 0.94f, 1.5f))));
        lightSet.Fields.Add(CreateVector4Field("Directional Light Hemi Color Up", "Directional Light: Hemisphere Color Up",
            (0, new Vector4(0.45f, 0.55f, 0.70f, 0.85f)), (100, new Vector4(0.45f, 0.55f, 0.70f, 0.85f))));
        lightSet.Fields.Add(CreateVector4Field("Directional Light Hemi Color Down", "Directional Light: Hemisphere Color Down",
            (0, new Vector4(0.24f, 0.22f, 0.20f, 0.65f)), (100, new Vector4(0.24f, 0.22f, 0.20f, 0.65f))));

        var toneMap = new GPARAM.Param
        {
            Key = "Tone Map[Yebis]",
            Name = "Tone Map",
            Comments = new List<string>()
        };
        toneMap.Fields.Add(CreateFloatField("Yebis-ToneMapExposure", "Yebis: Exposure", (0, 2.0f)));
        toneMap.Fields.Add(CreateFloatField("Yebis-Gamma", "Yebis: Gamma", (0, 2.2f)));
        toneMap.Fields.Add(CreateIntField("Yebis-ToneType", "Yebis: Tone Type", (0, 2)));
        toneMap.Fields.Add(CreateIntField("Yebis-AutoExposure", "Yebis: Enable Auto Exposure", (0, 0)));
        toneMap.Fields.Add(CreateFloatField("Yebis-MiddleGray", "Yebis: Middle Gray", (0, 0.18f)));
        toneMap.Fields.Add(CreateFloatField("Yebis-MinExposure", "Yebis: Minimum Exposure", (0, 0.5f)));
        toneMap.Fields.Add(CreateFloatField("Yebis-MaxExposure", "Yebis: Maximum Exposure", (0, 4.0f)));

        var settingInfo = new GPARAM.Param
        {
            Key = "Setting Info[Yebis]",
            Name = "Setting Info",
            Comments = new List<string>()
        };
        settingInfo.Fields.Add(CreateColorField("GS - BG Color-Data", "BG Color", (0, Color.FromArgb(255, 58, 66, 80))));

        gparam.Params.Add(lightSet);
        gparam.Params.Add(toneMap);
        gparam.Params.Add(settingInfo);

        return gparam;
    }

    public static (byte[] Bhd, byte[] Bdt) BuildCollisionBinder(params (string InnerName, byte[] RawHkxBytes)[] entries)
    {
        var bxf = new BXF4
        {
            Version = DS3MapCreatorConstants.Ds3BinderVersion,
            Format = Binder.Format.IDs | Binder.Format.Names1 | Binder.Format.Names2 | Binder.Format.Compression,
            Unicode = true,
            Extended = 4,
            PadHeaderEnd = true
        };

        for (int i = 0; i < entries.Length; i++)
        {
            var (innerName, rawBytes) = entries[i];
            var dcxBytes = DCX.Compress(rawBytes, DS3MapCreatorConstants.Ds3DcxType);
            bxf.Files.Add(new BinderFile(Binder.FileFlags.Flag1, i, innerName, dcxBytes)
            {
                CompressionType = DCX.Type.None
            });
        }

        bxf.Write(out byte[] bhd, out byte[] bdt);
        return (bhd, bdt);
    }

    /// <summary>
    /// Builds all files of a clean DS3 map (with or without the "Standing Box" floor + validated Havok 2014 collision)
    /// completely in memory so both the UI Scaffolder and automated headless tests share one canonical path.
    /// </summary>
    public static DS3StandingBoxBundle BuildStandingBoxBundle(
        DS3MapScaffoldOptions options,
        Func<string, Memory<byte>?> tryReadVfs = null,
        MaterialBank materialBank = null)
    {
        ArgumentNullException.ThrowIfNull(options);

        var mapId = DS3MapCreatorConstants.NormalizeMapId(options.MapId);
        if (!DS3MapCreatorConstants.IsValidDs3MapId(mapId))
            throw new ArgumentException($"Invalid DS3 map ID '{options.MapId}'. Expected mAA_BB_CC_DD.", nameof(options));

        var assetMapId = DS3MapCreatorConstants.GetAssetMapId(mapId);
        var gparamStem = DS3MapCreatorConstants.GetGparamFileStem(mapId);
        var id6 = string.IsNullOrWhiteSpace(options.StarterPieceId6) ? "000100" : options.StarterPieceId6.Trim();

        var msb = BuildMinimalMsb3(options.PlayerSpawnPosition);
        var btl = BuildEmptyBtl();
        var gparam = BuildBaselineGparam(tryReadVfs);

        var hiEntries = new List<(string InnerName, byte[] RawHkxBytes)>();
        var loEntries = new List<(string InnerName, byte[] RawHkxBytes)>();

        DS3CollisionValidationReport hiReport = null;
        DS3CollisionValidationReport loReport = null;

        var bundle = new DS3StandingBoxBundle
        {
            MapId = mapId,
            AssetMapId = assetMapId,
            GparamStem = gparamStem,
            StarterPieceStem = options.IncludeStarterBox ? $"{assetMapId}_{id6}" : "",
            StarterPieceModelName = options.IncludeStarterBox ? $"m{id6}" : "",
            StarterCollisionModelName = options.IncludeStarterBox ? $"h{id6}" : ""
        };

        if (options.IncludeStarterBox)
        {
            var stem = $"{assetMapId}_{id6}";
            var pieceModel = $"m{id6}";
            var colModel = $"h{id6}";

            var spec = DS3PrimitiveFlverBuilder.CreateStarterFloorSpec(
                options.StarterBoxWidth,
                options.StarterBoxHeight,
                options.StarterBoxDepth);

            var flver = DS3PrimitiveFlverBuilder.BuildFlver2(stem, spec, materialBank);
            var mapBndBytes = DS3PrimitiveFlverBuilder.PackMapBnd(assetMapId, id6, flver);

            bundle.Files.Add(new DS3ScaffoldedFile
            {
                VfsPath = DS3MapCreatorConstants.GetMapBndVfsPath(assetMapId, id6),
                Bytes = mapBndBytes
            });

            msb.Models.MapPieces.Add(new MSB3.Model.MapPiece
            {
                Name = pieceModel,
                SibPath = $@"N:\FDP\data\Model\map\{mapId}\sib\{pieceModel}.sib"
            });

            msb.Parts.MapPieces.Add(DS3PrimitiveFlverBuilder.BuildDs3MapPiecePart(
                $"{pieceModel}_0000", pieceModel, Vector3.Zero));

            // Extract geometry and build DS3 Havok 2014 Hi + Lo collisions
            var geom = DS3CollisionBuilder.ExtractFromFlver2(
                flver,
                Matrix4x4.Identity,
                options.StarterCollisionMaterialId,
                flipWinding: true);

            var (hiDoc, loDoc) = DS3CollisionBuilder.BuildHiAndLoDocuments(
                geom,
                id6,
                options.StarterCollisionMaterialId);

            var hiFileName = $"h{mapId.Substring(1)}_{id6}.hkx.dcx";
            var loFileName = $"l{mapId.Substring(1)}_{id6}.hkx.dcx";

            hiReport = DS3CollisionValidator.ValidateDocument(hiDoc, hiFileName, geom.TriangleCount);
            loReport = DS3CollisionValidator.ValidateDocument(loDoc, loFileName, geom.TriangleCount);

            bundle.HiValidation = hiReport;
            bundle.LoValidation = loReport;

            if (!hiReport.IsValid)
                throw new InvalidDataException($"Starter Hi-res collision failed validation: {string.Join("; ", hiReport.Errors)}");
            if (!loReport.IsValid)
                throw new InvalidDataException($"Starter Lo-res collision failed validation: {string.Join("; ", loReport.Errors)}");

            hiEntries.Add(($@"h{mapId.Substring(1)}\{hiFileName}", Havok.Files.HavokFile.Write(hiDoc)));
            loEntries.Add(($@"l{mapId.Substring(1)}\{loFileName}", Havok.Files.HavokFile.Write(loDoc)));

            msb.Models.Collisions.Add(new MSB3.Model.Collision
            {
                Name = colModel,
                SibPath = $@"N:\FDP\data\Model\map\{mapId}\hkt\{colModel}.hkt"
            });

            msb.Parts.Collisions.Add(DS3CollisionBuilder.BuildDs3CollisionPart(
                $"{colModel}_0000", colModel, Vector3.Zero, Vector3.Zero));
        }

        var (hBhd, hBdt) = BuildCollisionBinder(hiEntries.ToArray());
        var (lBhd, lBdt) = BuildCollisionBinder(loEntries.ToArray());

        bundle.Files.Add(new DS3ScaffoldedFile
        {
            VfsPath = DS3MapCreatorConstants.GetMsbVfsPath(mapId),
            Bytes = msb.Write()
        });
        bundle.Files.Add(new DS3ScaffoldedFile
        {
            VfsPath = DS3MapCreatorConstants.GetBtlVfsPath(mapId),
            Bytes = btl.Write()
        });
        bundle.Files.Add(new DS3ScaffoldedFile
        {
            VfsPath = DS3MapCreatorConstants.GetGparamVfsPath(mapId),
            Bytes = gparam.Write()
        });
        bundle.Files.Add(new DS3ScaffoldedFile
        {
            VfsPath = DS3MapCreatorConstants.GetCollisionBhdVfsPath(mapId, "h"),
            Bytes = hBhd
        });
        bundle.Files.Add(new DS3ScaffoldedFile
        {
            VfsPath = DS3MapCreatorConstants.GetCollisionBdtVfsPath(mapId, "h"),
            Bytes = hBdt
        });
        bundle.Files.Add(new DS3ScaffoldedFile
        {
            VfsPath = DS3MapCreatorConstants.GetCollisionBhdVfsPath(mapId, "l"),
            Bytes = lBhd
        });
        bundle.Files.Add(new DS3ScaffoldedFile
        {
            VfsPath = DS3MapCreatorConstants.GetCollisionBdtVfsPath(mapId, "l"),
            Bytes = lBdt
        });

        return bundle;
    }

    public static DS3ScaffoldResult ScaffoldIntoProject(MapEditorView view, DS3MapScaffoldOptions options)
    {
        var result = new DS3ScaffoldResult();

        if (view?.Project == null)
        {
            result.Error = "No active project is loaded.";
            return result;
        }

        var project = view.Project;
        if (project.Descriptor.ProjectType is not ProjectType.DS3)
        {
            result.Error = "DS3 Map Scaffolder only targets Dark Souls 3 projects.";
            return result;
        }

        var mapId = DS3MapCreatorConstants.NormalizeMapId(options?.MapId);
        if (!DS3MapCreatorConstants.IsValidDs3MapId(mapId))
        {
            result.Error = $"Invalid DS3 map ID '{options?.MapId}'. Use format mAA_BB_CC_DD (e.g. m31_90_00_00).";
            return result;
        }

        result.MapId = mapId;

        try
        {
            var materialBank = project.Handler.MaterialData?.PrimaryBank;
            var bundle = BuildStandingBoxBundle(
                options!,
                vfsPath => project.VFS.FS.FileExists(vfsPath) ? project.VFS.FS.ReadFile(vfsPath) : null,
                materialBank);

            result.HiValidation = bundle.HiValidation;
            result.LoValidation = bundle.LoValidation;

            if (!options!.OverwriteExisting)
            {
                var conflicts = bundle.Files
                    .Select(f => f.VfsPath)
                    .Where(p => project.VFS.ProjectFS.FileExists(p))
                    .ToList();

                if (conflicts.Count > 0)
                {
                    result.Error = $"Target map already has files in project directory ({string.Join(", ", conflicts.Take(3))}). Enable 'Overwrite Existing' to replace them.";
                    return result;
                }
            }

            foreach (var file in bundle.Files)
            {
                var relPath = DS3MapCreatorConstants.ToProjectRelativePath(file.VfsPath);
                if (options.OverwriteExisting && project.VFS.ProjectFS.FileExists(file.VfsPath))
                {
                    var backupData = project.VFS.ProjectFS.ReadFile(file.VfsPath);
                    if (backupData != null)
                        project.VFS.ProjectFS.WriteFile(relPath + ".bak", backupData.Value.ToArray());
                }

                project.VFS.ProjectFS.WriteFile(relPath, file.Bytes);
                result.WrittenVfsPaths.Add(file.VfsPath);
            }

            RegisterScaffoldedFilesInLocator(project, bundle);

            view.GparamLightingBank?.ClearCache();
            EnsureMapBankEntry(project, mapId, DS3MapCreatorConstants.GetMsbVfsPath(mapId));
            view.MapListView?.RefreshMapList();

            if (options.LoadImmediately)
            {
                if (view.Selection.GetMapContainerFromMapID(mapId) != null)
                    view.Universe.UnloadMap(mapId, false);

                view.Universe.LoadMap(mapId, selectOnLoad: true);
            }

            result.Success = true;
            return result;
        }
        catch (Exception ex)
        {
            result.Error = $"Map scaffolding failed: {ex.Message}";
            Smithbox.LogError(typeof(DS3MapScaffolder), result.Error, ex);
            return result;
        }
    }

    private static void RegisterScaffoldedFilesInLocator(ProjectEntry project, DS3StandingBoxBundle bundle)
    {
        var mapId = bundle.MapId;

        AddLocatorEntryIfMissing(project.Locator.MapFiles, DS3MapCreatorConstants.GetMsbVfsPath(mapId), mapId, "msb");
        AddLocatorEntryIfMissing(project.Locator.LightFiles, DS3MapCreatorConstants.GetBtlVfsPath(mapId), $"{mapId}_0000", "btl");
        AddLocatorEntryIfMissing(project.Locator.GparamFiles, DS3MapCreatorConstants.GetGparamVfsPath(mapId), bundle.GparamStem, "gparam");

        DS3CollisionBuilder.RegisterCollisionBinderLocatorEntry(project, DS3MapCreatorConstants.GetCollisionBhdVfsPath(mapId, "h"), $"h{mapId.Substring(1)}");
        DS3CollisionBuilder.RegisterCollisionBinderLocatorEntry(project, DS3MapCreatorConstants.GetCollisionBhdVfsPath(mapId, "l"), $"l{mapId.Substring(1)}");

        if (!string.IsNullOrEmpty(bundle.StarterPieceStem))
        {
            DS3PrimitiveFlverBuilder.RegisterMapPieceEntry(
                project,
                DS3MapCreatorConstants.GetMapBndVfsPath(bundle.AssetMapId, bundle.StarterPieceStem[^6..]),
                bundle.StarterPieceStem);
        }
    }

    private static void AddLocatorEntryIfMissing(FileDictionary dict, string vfsPath, string stem, string extension)
    {
        if (dict == null)
            return;

        var normalized = vfsPath.StartsWith('/') ? vfsPath : "/" + vfsPath;
        if (dict.Entries.Any(e => string.Equals(e.Path, normalized, StringComparison.OrdinalIgnoreCase)))
            return;

        var slash = normalized.LastIndexOf('/');
        dict.Entries.Add(new FileDictionaryEntry
        {
            Archive = "",
            Path = normalized,
            Folder = slash > 0 ? normalized[..slash] : "",
            Filename = stem,
            Extension = extension,
            Validated = true
        });
    }

    private static void EnsureMapBankEntry(ProjectEntry project, string mapId, string msbVfsPath)
    {
        var bank = project.Handler.MapData?.PrimaryBank;
        if (bank == null)
            return;

        var existingKey = bank.Maps.Keys.FirstOrDefault(k => string.Equals(k.Filename, mapId, StringComparison.OrdinalIgnoreCase));
        if (existingKey != null)
            return;

        var entry = project.Locator.MapFiles.Entries
            .FirstOrDefault(e => string.Equals(e.Filename, mapId, StringComparison.OrdinalIgnoreCase))
            ?? new FileDictionaryEntry
            {
                Archive = "",
                Path = msbVfsPath,
                Folder = "/map/mapstudio",
                Filename = mapId,
                Extension = "msb",
                Validated = true
            };

        bank.Maps[entry] = new MapWrapper(project, entry, bank.TargetFS);
    }

    private static GPARAM.Vector2Field CreateVector2Field(string key, string name, params (int Id, Vector2 Value)[] values)
    {
        var field = new GPARAM.Vector2Field
        {
            Key = key,
            Name = name,
            Unk = 0,
            Capacity = (short)values.Length,
            Values = values.Select(v => new GPARAM.FieldValue<Vector2>
            {
                ID = v.Id,
                TimeOfDay = 0f,
                Value = v.Value
            }).ToList()
        };
        return field;
    }

    private static GPARAM.Vector4Field CreateVector4Field(string key, string name, params (int Id, Vector4 Value)[] values)
    {
        var field = new GPARAM.Vector4Field
        {
            Key = key,
            Name = name,
            Unk = 0,
            Capacity = (short)values.Length,
            Values = values.Select(v => new GPARAM.FieldValue<Vector4>
            {
                ID = v.Id,
                TimeOfDay = 0f,
                Value = v.Value
            }).ToList()
        };
        return field;
    }

    private static GPARAM.FloatField CreateFloatField(string key, string name, params (int Id, float Value)[] values)
    {
        var field = new GPARAM.FloatField
        {
            Key = key,
            Name = name,
            Unk = 0,
            Capacity = (short)values.Length,
            Values = values.Select(v => new GPARAM.FieldValue<float>
            {
                ID = v.Id,
                TimeOfDay = 0f,
                Value = v.Value
            }).ToList()
        };
        return field;
    }

    private static GPARAM.IntField CreateIntField(string key, string name, params (int Id, int Value)[] values)
    {
        var field = new GPARAM.IntField
        {
            Key = key,
            Name = name,
            Unk = 0,
            Capacity = (short)values.Length,
            Values = values.Select(v => new GPARAM.FieldValue<int>
            {
                ID = v.Id,
                TimeOfDay = 0f,
                Value = v.Value
            }).ToList()
        };
        return field;
    }

    private static GPARAM.ColorField CreateColorField(string key, string name, params (int Id, Color Value)[] values)
    {
        var field = new GPARAM.ColorField
        {
            Key = key,
            Name = name,
            Unk = 0,
            Capacity = (short)values.Length,
            Values = values.Select(v => new GPARAM.FieldValue<Color>
            {
                ID = v.Id,
                TimeOfDay = 0f,
                Value = v.Value
            }).ToList()
        };
        return field;
    }
}
