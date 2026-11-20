using SoulsFormats;
using StudioCore.Utilities;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace StudioCore.Editors.MapEditor.DS3Creator;

/// <summary>
/// Shared DS3 format constants, asset paths, naming rules, and presets for the DS3 Custom Map Creation Suite.
/// </summary>
public static class DS3MapCreatorConstants
{
    public const DCX.Type Ds3DcxType = DCX.Type.DCX_DFLT_10000_44_9;

    public const int Ds3FlverVersion = 0x20014;
    public const int Ds3BtlVersion = 6;
    public const int Ds3BtlLightSize = 0xC8;

    public const string Ds3BinderVersion = "07D7R6";

    /// <summary>
    /// Default walkable DS3 Havok material ID (1 = Cobblestone; matches HavokCollisionMaterials.Names).
    /// </summary>
    public const uint DefaultWalkableMaterialId = 1;

    /// <summary>
    /// Default DS3 static map piece MTD shader fallback when MaterialBank is unloaded or lacks M[ARSN].
    /// </summary>
    public const string DefaultMtdPath = @"M[ARSN].mtd";

    /// <summary>
    /// Guaranteed textures inside /other/systex.tpf.dcx loaded automatically by DS3 and Smithbox.
    /// </summary>
    public const string DefaultDiffuseTexture = "SYSTEX_White.tif";
    public const string DefaultNormalTexture = "SYSTEX_DummyNormal.tif";
    public const string DefaultSpecularTexture = "reflectance_040_040_040_r.tif";

    public sealed record DS3MapSlotPreset(string MapId, string Label, string Notes);

    public static readonly IReadOnlyList<DS3MapSlotPreset> MapSlotPresets =
    [
        new("m31_90_00_00", "m31_90_00_00 (Test / Unused Slot)", "Clean unused m31 slot ideal for Standing Box testing."),
        new("m32_90_00_00", "m32_90_00_00 (Test / Unused Slot)", "Clean unused m32 slot ideal for custom tests."),
        new("m36_00_00_00", "m36_00_00_00 (Unused DS3 Block)", "Standard unused DS3 area slot."),
        new("m30_02_00_00", "m30_02_00_00 (Unused High Wall Slot)", "Unused High Wall area slot."),
        new("m46_00_00_00", "m46_00_00_00 (Arena Slot)", "Clean compact DS3 arena slot."),
    ];

    public static readonly (uint Id, string Name)[] CollisionMaterialPresets =
    [
        (1, "1 - Cobblestone (Walkable)"),
        (2, "2 - Stone (Walkable)"),
        (3, "3 - Soil (Walkable)"),
        (4, "4 - Wood (Walkable)"),
        (5, "5 - Grassland (Walkable)"),
        (6, "6 - Gravel (Walkable)"),
        (11, "11 - Iron (Walkable)"),
        (13, "13 - Sand (Walkable)"),
        (20, "20 - Water Puddle (Walkable)"),
        (29, "29 - Without / Invisible Barrier"),
    ];

    private static readonly Regex Ds3MapIdRegex = new(@"^m\d{2}_\d{2}_\d{2}_\d{2}$", RegexOptions.Compiled);

    public static bool IsValidDs3MapId(string mapId) =>
        !string.IsNullOrWhiteSpace(mapId) && Ds3MapIdRegex.IsMatch(mapId.Trim());

    public static string NormalizeMapId(string mapId) =>
        (mapId ?? "").Trim().ToLowerInvariant();

    /// <summary>
    /// DS3 map pieces live in mAA_BB_00_00 for mAA_BB_CC_DD blocks.
    /// </summary>
    public static string GetAssetMapId(string mapId)
    {
        if (string.IsNullOrEmpty(mapId) || mapId.Length < 6)
            return mapId;

        return $"{mapId.Substring(0, 6)}_00_00";
    }

    public static string GetGparamFileStem(string mapId)
    {
        if (string.IsNullOrEmpty(mapId) || mapId.Length < 6)
            return "m30_00_0000";

        return $"{mapId.Substring(0, 6)}_0000";
    }

    public static string GetMsbVfsPath(string mapId) => $"/map/mapstudio/{mapId}.msb.dcx";
    public static string GetBtlVfsPath(string mapId) => $"/map/{mapId}/{mapId}_0000.btl.dcx";
    public static string GetGparamVfsPath(string mapId) => $"/param/drawparam/{GetGparamFileStem(mapId)}.gparam.dcx";
    public static string GetCollisionBhdVfsPath(string mapId, string type) => $"/map/{mapId}/{type}{mapId.Substring(1)}.hkxbhd";
    public static string GetCollisionBdtVfsPath(string mapId, string type) => $"/map/{mapId}/{type}{mapId.Substring(1)}.hkxbdt";
    public static string GetMapBndVfsPath(string assetMapId, string id6) => $"/map/{assetMapId}/{assetMapId}_{id6}.mapbnd.dcx";

    public static string ToProjectRelativePath(string vfsPath) =>
        string.IsNullOrEmpty(vfsPath) ? "" : vfsPath.TrimStart('/').Replace('/', Path.DirectorySeparatorChar);

    public static uint[] CreateFullGroupMask() =>
    [
        0xFFFFFFFFu, 0xFFFFFFFFu, 0xFFFFFFFFu, 0xFFFFFFFFu,
        0xFFFFFFFFu, 0xFFFFFFFFu, 0xFFFFFFFFu, 0xFFFFFFFFu
    ];
}
