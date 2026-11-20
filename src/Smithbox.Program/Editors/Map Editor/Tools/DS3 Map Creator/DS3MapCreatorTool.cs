using Hexa.NET.ImGui;
using StudioCore.Application;
using StudioCore.Editors.Common;
using StudioCore.Renderer;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace StudioCore.Editors.MapEditor.DS3Creator;

/// <summary>
/// Authoring UI panel for the Dark Souls 3 Custom Map Creation Suite (Milestone 1: Scaffolder, Primitives,
/// Havok 2014 Collision Builder, Collision Viewport Visualization, and Validator).
/// </summary>
public sealed class DS3MapCreatorTool
{
    private readonly MapEditorView View;
    private readonly ProjectEntry Project;

    public readonly DS3CollisionOverlayRenderer CollisionOverlay = new();

    // 1A: New DS3 Map Scaffolder
    private int PresetIndex = 0;
    private string CustomMapId = DS3MapCreatorConstants.MapSlotPresets[0].MapId;
    private Vector3 PlayerSpawnPosition = new(0.0f, 1.0f, 0.0f);
    private bool IncludeStarterBox = true;
    private Vector3 StarterBoxSize = new(24.0f, 1.0f, 24.0f);
    private bool OverwriteExisting = false;
    private bool LoadScaffoldedImmediately = true;

    // 1B: Primitive Spawner
    private DS3PrimitiveType PrimitiveType = DS3PrimitiveType.BoxFloor;
    private Vector3 PrimitiveSize = new(8.0f, 1.0f, 8.0f);
    private int PrimitiveStairSteps = 10;
    private bool PrimitiveAnchorTopAtOrigin = false;
    private bool SpawnAtPlacementOrb = true;
    private Vector3 ManualSpawnPosition = Vector3.Zero;
    private bool AutoBakePrimitiveCollision = true;
    private string PrimitiveMtdPath = DS3MapCreatorConstants.DefaultMtdPath;

    // 1C: Collision Builder
    private int MaterialPresetIndex = 0;
    private uint CustomCollisionMaterialId = DS3MapCreatorConstants.DefaultWalkableMaterialId;
    private string RequestedCollisionId6 = "";
    private bool BuildHiRes = true;
    private bool BuildLoRes = true;
    private bool RegisterCollisionInMsb = true;
    private bool FlipExtractedFlverWinding = true;

    // Status & Validation output
    public bool FocusRequested;
    private string StatusMessage = "";
    private bool StatusIsError = false;
    private List<DS3CollisionValidationReport> LastValidationReports = new();

    public void RequestFocus() => FocusRequested = true;

    public DS3MapCreatorTool(MapEditorView view, ProjectEntry project)
    {
        View = view;
        Project = project;
    }

    public bool CanUse()
    {
        return Project?.Descriptor.ProjectType is ProjectType.DS3;
    }

    public void Update()
    {
        CollisionOverlay.Update(View);
    }

    public void OnToolWindow()
    {
        if (!CanUse())
            return;

        DrawScaffolderSection();
        GUI.Spacer();

        DrawPrimitiveSpawnerSection();
        GUI.Spacer();

        DrawCollisionBuilderSection();
        GUI.Spacer();

        DrawVisualizationSection();
        GUI.Spacer();

        DrawValidatorSection();

        if (!string.IsNullOrEmpty(StatusMessage))
        {
            GUI.Spacer();
            var color = StatusIsError ? UI.Current.ImGui_Invalid_Text_Color : UI.Current.ImGui_AliasName_Text;
            GUI.WrappedTextColored(color, StatusMessage);
        }
    }

    private void DrawScaffolderSection()
    {
        if (!ImGui.CollapsingHeader("1A. New DS3 Map Scaffolder##ds3CreatorScaffold", ImGuiTreeNodeFlags.DefaultOpen))
            return;

        ImGui.TextUnformatted("Preset Clean Map Slot:");
        ImGui.SetNextItemWidth(-1);
        var currentPreset = DS3MapCreatorConstants.MapSlotPresets[PresetIndex];
        if (ImGui.BeginCombo("##ds3ScaffoldPreset", currentPreset.Label))
        {
            for (int i = 0; i < DS3MapCreatorConstants.MapSlotPresets.Count; i++)
            {
                var p = DS3MapCreatorConstants.MapSlotPresets[i];
                if (ImGui.Selectable($"{p.Label}##ds3Preset{i}", i == PresetIndex))
                {
                    PresetIndex = i;
                    CustomMapId = p.MapId;
                }
            }
            ImGui.EndCombo();
        }
        GUI.Tooltip(currentPreset.Notes);

        ImGui.TextUnformatted("Target DS3 Map ID (mAA_BB_CC_DD):");
        ImGui.SetNextItemWidth(-1);
        ImGui.InputText("##ds3ScaffoldMapId", ref CustomMapId, 16);

        ImGui.DragFloat3("Player Spawn##ds3PlayerSpawn", ref PlayerSpawnPosition, 0.25f);
        ImGui.Checkbox("Include Standing Box Floor + Collision##ds3StarterBox", ref IncludeStarterBox);

        if (IncludeStarterBox)
        {
            ImGui.DragFloat3("Floor Size (W, H, D)##ds3StarterBoxSize", ref StarterBoxSize, 0.5f, 1.0f, 200.0f);
        }

        ImGui.Checkbox("Overwrite Existing Project Files (.bak saved)##ds3Overwrite", ref OverwriteExisting);
        ImGui.Checkbox("Load Scaffolded Map Immediately##ds3LoadNow", ref LoadScaffoldedImmediately);

        var idValid = DS3MapCreatorConstants.IsValidDs3MapId(CustomMapId);
        if (!idValid)
        {
            GUI.WrappedTextColored(UI.Current.ImGui_Warning_Text_Color,
                "Enter a valid DS3 map ID matching mXX_XX_XX_XX (for example m31_90_00_00).");
        }

        ImGui.BeginDisabled(!idValid);
        if (ImGui.Button("Scaffold DS3 Map##ds3ScaffoldBtn", new Vector2(-1, 0)))
        {
            var opts = new DS3MapScaffoldOptions
            {
                MapId = CustomMapId,
                PlayerSpawnPosition = PlayerSpawnPosition,
                IncludeStarterBox = IncludeStarterBox,
                StarterBoxWidth = MathF.Max(StarterBoxSize.X, 1.0f),
                StarterBoxHeight = MathF.Max(StarterBoxSize.Y, 0.2f),
                StarterBoxDepth = MathF.Max(StarterBoxSize.Z, 1.0f),
                StarterCollisionMaterialId = CustomCollisionMaterialId,
                OverwriteExisting = OverwriteExisting,
                LoadImmediately = LoadScaffoldedImmediately
            };

            var res = DS3MapScaffolder.ScaffoldIntoProject(View, opts);
            if (res.Success)
            {
                StatusIsError = false;
                StatusMessage = $"Scaffolded {res.MapId} ({res.WrittenVfsPaths.Count} files).";
                LastValidationReports.Clear();
                if (res.HiValidation != null) LastValidationReports.Add(res.HiValidation);
                if (res.LoValidation != null) LastValidationReports.Add(res.LoValidation);
                CollisionOverlay.Invalidate();
            }
            else
            {
                StatusIsError = true;
                StatusMessage = res.Error ?? "Scaffold failed.";
            }
        }
        ImGui.EndDisabled();
    }

    private void DrawPrimitiveSpawnerSection()
    {
        if (!ImGui.CollapsingHeader("1B. Primitive Blockout Spawner##ds3CreatorPrim", ImGuiTreeNodeFlags.DefaultOpen))
            return;

        var activeMap = View.Selection.SelectedMapContainer;
        if (activeMap == null)
        {
            GUI.WrappedText("Load and select a DS3 map in the Map List to spawn primitives.");
            return;
        }

        ImGui.SetNextItemWidth(-1);
        if (ImGui.BeginCombo("##ds3PrimType", PrimitiveType.ToString()))
        {
            foreach (var t in Enum.GetValues<DS3PrimitiveType>())
            {
                if (ImGui.Selectable($"{t}##ds3PrimOpt{t}", t == PrimitiveType))
                {
                    PrimitiveType = t;
                    PrimitiveSize = t switch
                    {
                        DS3PrimitiveType.BoxFloor => new(12f, 1f, 12f),
                        DS3PrimitiveType.Wall => new(10f, 5f, 1f),
                        DS3PrimitiveType.Ramp => new(6f, 4f, 10f),
                        DS3PrimitiveType.Stairs => new(6f, 4f, 10f),
                        _ => PrimitiveSize
                    };
                    PrimitiveAnchorTopAtOrigin = t == DS3PrimitiveType.BoxFloor;
                }
            }
            ImGui.EndCombo();
        }

        ImGui.DragFloat3("Dimensions (W, H, D)##ds3PrimSize", ref PrimitiveSize, 0.25f, 0.2f, 200.0f);

        if (PrimitiveType == DS3PrimitiveType.Stairs)
        {
            ImGui.SliderInt("Stair Steps##ds3PrimSteps", ref PrimitiveStairSteps, 2, 48);
        }

        ImGui.Checkbox("Top Surface at Local Y=0 (Floor Mode)##ds3AnchorTop", ref PrimitiveAnchorTopAtOrigin);
        ImGui.Checkbox("Spawn at Viewport Placement Orb##ds3UseOrb", ref SpawnAtPlacementOrb);
        if (!SpawnAtPlacementOrb)
        {
            ImGui.DragFloat3("Spawn Position##ds3ManualPos", ref ManualSpawnPosition, 0.25f);
        }

        ImGui.TextUnformatted("Material MTD:");
        ImGui.SetNextItemWidth(-1);
        ImGui.InputText("##ds3PrimMtd", ref PrimitiveMtdPath, 128);

        ImGui.Checkbox("Auto-Bake Havok 2014 Collision + Register in MSB3##ds3AutoBake", ref AutoBakePrimitiveCollision);

        if (ImGui.Button("Spawn Primitive MapPiece##ds3SpawnPrimBtn", new Vector2(-1, 0)))
        {
            var pos = SpawnAtPlacementOrb && View.ViewportWindow != null
                ? View.ViewportWindow.GetPlacementPosition()
                : ManualSpawnPosition;

            var spec = new DS3PrimitiveSpec
            {
                Type = PrimitiveType,
                Width = MathF.Max(PrimitiveSize.X, 0.2f),
                Height = MathF.Max(PrimitiveSize.Y, 0.2f),
                Depth = MathF.Max(PrimitiveSize.Z, 0.2f),
                StairSteps = PrimitiveStairSteps,
                AnchorTopAtOrigin = PrimitiveAnchorTopAtOrigin,
                MtdPath = PrimitiveMtdPath
            };

            var res = DS3PrimitiveFlverBuilder.SpawnPrimitiveInMap(
                View, activeMap, spec, pos, AutoBakePrimitiveCollision, CustomCollisionMaterialId);

            if (res.Success)
            {
                StatusIsError = false;
                StatusMessage = AutoBakePrimitiveCollision
                    ? $"Spawned {res.MapPiecePartName} + collision {res.CollisionModelName}."
                    : $"Spawned {res.MapPiecePartName} ({res.MapBndVfsPath}).";

                LastValidationReports.Clear();
                if (res.HiValidation != null) LastValidationReports.Add(res.HiValidation);
                if (res.LoValidation != null) LastValidationReports.Add(res.LoValidation);
                CollisionOverlay.Invalidate();
            }
            else
            {
                StatusIsError = true;
                StatusMessage = res.Error ?? "Primitive spawn failed.";
            }
        }
    }

    private void DrawCollisionBuilderSection()
    {
        if (!ImGui.CollapsingHeader("1C. DS3 Havok 2014 Collision Builder##ds3CreatorCol", ImGuiTreeNodeFlags.DefaultOpen))
            return;

        var activeMap = View.Selection.SelectedMapContainer;
        if (activeMap == null)
        {
            GUI.WrappedText("Load a DS3 map and select MapPiece entities in the viewport.");
            return;
        }

        var selectedPieces = View.ViewportSelection
            .GetFilteredSelection<MsbEntity>(e => EntityHelper.IsPartMapPiece(e) && e.MapID == activeMap.Name)
            .ToList();

        GUI.WrappedText($"Selected MapPieces ({activeMap.Name}): {selectedPieces.Count}");

        ImGui.TextUnformatted("Walkable Hit-Material Preset:");
        ImGui.SetNextItemWidth(-1);
        var matPreset = DS3MapCreatorConstants.CollisionMaterialPresets[MaterialPresetIndex];
        if (ImGui.BeginCombo("##ds3MatPreset", matPreset.Name))
        {
            for (int i = 0; i < DS3MapCreatorConstants.CollisionMaterialPresets.Length; i++)
            {
                var mp = DS3MapCreatorConstants.CollisionMaterialPresets[i];
                if (ImGui.Selectable($"{mp.Name}##ds3Mat{i}", i == MaterialPresetIndex))
                {
                    MaterialPresetIndex = i;
                    CustomCollisionMaterialId = mp.Id;
                }
            }
            ImGui.EndCombo();
        }

        int matIdInt = (int)CustomCollisionMaterialId;
        if (ImGui.InputInt("Material ID Override##ds3MatId", ref matIdInt))
            CustomCollisionMaterialId = (uint)Math.Max(0, matIdInt);

        ImGui.TextUnformatted("Collision ID (6 digits, blank = auto):");
        ImGui.SetNextItemWidth(-1);
        ImGui.InputText("##ds3ColId6", ref RequestedCollisionId6, 6);

        ImGui.Checkbox("Build Hi-Res (h*)##ds3BuildHi", ref BuildHiRes);
        ImGui.SameLine();
        ImGui.Checkbox("Build Lo-Res (l*)##ds3BuildLo", ref BuildLoRes);
        ImGui.Checkbox("Auto-Register MSB3 Collision Part##ds3RegMsb", ref RegisterCollisionInMsb);
        ImGui.Checkbox("Flip FLVER Winding for DS3 hknp##ds3FlipWind", ref FlipExtractedFlverWinding);

        ImGui.BeginDisabled(selectedPieces.Count == 0 || (!BuildHiRes && !BuildLoRes));
        if (ImGui.Button("Build & Register DS3 Collision##ds3BuildColBtn", new Vector2(-1, 0)))
        {
            var res = DS3CollisionBuilder.BuildAndRegisterForMapPieces(
                View,
                activeMap,
                selectedPieces,
                RequestedCollisionId6,
                CustomCollisionMaterialId,
                BuildHiRes,
                BuildLoRes,
                RegisterCollisionInMsb,
                FlipExtractedFlverWinding);

            if (res.Success)
            {
                StatusIsError = false;
                StatusMessage = $"Built collision {res.CollisionModelName} ({res.ExtractedTriangles} tris, stripped {res.StrippedDegenerates} degens).";
                LastValidationReports.Clear();
                if (res.HiValidation != null) LastValidationReports.Add(res.HiValidation);
                if (res.LoValidation != null) LastValidationReports.Add(res.LoValidation);
                CollisionOverlay.Invalidate();
            }
            else
            {
                StatusIsError = true;
                StatusMessage = res.Error ?? "Collision build failed.";
            }
        }
        ImGui.EndDisabled();
    }

    private void DrawVisualizationSection()
    {
        if (!ImGui.CollapsingHeader("Collision Viewport Visualization (M1.7)##ds3CreatorVis", ImGuiTreeNodeFlags.DefaultOpen))
            return;

        if (ImGui.Button("Solid Collision##ds3VisSolid"))
        {
            SetBasicRenderFilter(RenderFilter.Collision, true);
            CFG.Current.Viewport_Collision_Wireframe = false;
            CFG.Current.Viewport_Collision_Opacity = 1.0f;
            CollisionOverlay.ShowEdgeWireframeOverlay = false;
            CollisionTransparency.Refresh();
        }
        ImGui.SameLine();
        if (ImGui.Button("Wireframe##ds3VisWire"))
        {
            SetBasicRenderFilter(RenderFilter.Collision, true);
            if (SceneRenderer.Device?.Features.FillModeWireframe == true)
            {
                CFG.Current.Viewport_Collision_Wireframe = true;
                CollisionOverlay.ShowEdgeWireframeOverlay = false;
            }
            else
            {
                CFG.Current.Viewport_Collision_Wireframe = false;
                CollisionOverlay.ShowEdgeWireframeOverlay = true;
            }
            CollisionTransparency.Refresh();
        }
        ImGui.SameLine();
        if (ImGui.Button("Combined (50%)##ds3VisCombined"))
        {
            SetBasicRenderFilter(RenderFilter.MapPiece, true);
            SetBasicRenderFilter(RenderFilter.Collision, true);
            CFG.Current.Viewport_Collision_Wireframe = false;
            CFG.Current.Viewport_Collision_Opacity = 0.5f;
            CollisionOverlay.ShowEdgeWireframeOverlay = false;
            CollisionTransparency.Refresh();
        }

        var activeScene = View.ViewportHandler.ActiveViewport?.RenderScene ?? View.Universe?.GetCurrentScene();
        var showCol = activeScene?.DrawFilter.HasFlag(RenderFilter.Collision) ?? CFG.Current.MapEditor_BasicFilter_Collision;
        if (ImGui.Checkbox("Show Collision Filter##ds3ShowCol", ref showCol))
            SetBasicRenderFilter(RenderFilter.Collision, showCol);

        var wireframe = CFG.Current.Viewport_Collision_Wireframe;
        if (ImGui.Checkbox("Wireframe Collision FillMode##ds3WireframeChk", ref wireframe))
        {
            CFG.Current.Viewport_Collision_Wireframe = wireframe;
            CollisionTransparency.Refresh();
        }

        var edgeOverlay = CollisionOverlay.ShowEdgeWireframeOverlay;
        if (ImGui.Checkbox("Edge Wireframe Overlay##ds3EdgeWireOverlay", ref edgeOverlay))
            CollisionOverlay.ShowEdgeWireframeOverlay = edgeOverlay;

        var boundsOverlay = CollisionOverlay.ShowBoundingVolumes;
        if (ImGui.Checkbox("Bounding Volumes (Mesh + Section AABBs)##ds3BoundsOverlay", ref boundsOverlay))
            CollisionOverlay.ShowBoundingVolumes = boundsOverlay;

        var selectedOnly = CollisionOverlay.SelectedCollisionOnly;
        if (ImGui.Checkbox("Selected Collision Only##ds3BoundsSelOnly", ref selectedOnly))
            CollisionOverlay.SelectedCollisionOnly = selectedOnly;

        var opacity = CFG.Current.Viewport_Collision_Opacity;
        if (ImGui.SliderFloat("Collision Opacity##ds3ColOpacity", ref opacity, 0.1f, 1.0f, "%.2f"))
        {
            CFG.Current.Viewport_Collision_Opacity = opacity;
            CollisionTransparency.Refresh();
        }

        var showLo = CFG.Current.MapEditor_Display_Low_Collision;
        if (ImGui.Checkbox("Low Layer (l*)##ds3ShowLo", ref showLo))
            View.HavokCollisionBank.ToggleCollisionType(HavokCollisionType.Low);
        ImGui.SameLine();
        var showHi = CFG.Current.MapEditor_Display_High_Collision;
        if (ImGui.Checkbox("High Layer (h*)##ds3ShowHi", ref showHi))
            View.HavokCollisionBank.ToggleCollisionType(HavokCollisionType.High);
    }

    private void SetBasicRenderFilter(RenderFilter filter, bool enabled)
    {
        var scene = View.ViewportHandler?.ActiveViewport?.RenderScene ?? View.Universe?.GetCurrentScene();
        if (scene != null)
        {
            scene.SetDrawFilter(filter, enabled);
            View.BasicFilters?.StoreFilterState();
            View.DelayPicking();
            return;
        }

        if (filter == RenderFilter.Collision)
            CFG.Current.MapEditor_BasicFilter_Collision = enabled;
        else if (filter == RenderFilter.MapPiece)
            CFG.Current.MapEditor_BasicFilter_MapPiece = enabled;
    }

    private void DrawValidatorSection()
    {
        if (!ImGui.CollapsingHeader("1D. Collision & Map Validator (M1.8)##ds3CreatorVal"))
            return;

        var activeMap = View.Selection.SelectedMapContainer;
        ImGui.BeginDisabled(activeMap == null);
        if (ImGui.Button("Validate Active Map Collisions##ds3ValidateBtn", new Vector2(-1, 0)) && activeMap != null)
        {
            LastValidationReports = DS3CollisionValidator.ValidateActiveMap(View, activeMap);
            var passed = LastValidationReports.Count(r => r.IsValid);
            var total = LastValidationReports.Count;
            StatusIsError = passed < total;
            StatusMessage = $"Validation completed: {passed}/{total} checks passed.";
        }
        ImGui.EndDisabled();

        foreach (var report in LastValidationReports)
        {
            var col = report.IsValid ? UI.Current.ImGui_AliasName_Text : UI.Current.ImGui_Invalid_Text_Color;
            GUI.WrappedTextColored(col, report.FormatSummary());

            foreach (var err in report.Errors)
                GUI.WrappedTextColored(UI.Current.ImGui_Invalid_Text_Color, $"  ERROR: {err}");

            foreach (var warn in report.Warnings)
                GUI.WrappedTextColored(UI.Current.ImGui_Warning_Text_Color, $"  WARN: {warn}");
        }
    }
}
