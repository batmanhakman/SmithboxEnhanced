using Hexa.NET.ImGui;
using SoulsFormats;
using StudioCore.Editors.Common;
using StudioCore.Editors.MetadataEditor;
using StudioCore.Editors.ModelEditor;
using StudioCore.Keybinds;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text;

namespace StudioCore.Editors.HavokEditor;

public class FlverSelectionMenu
{
    public HavokEditorView View;
    public ProjectEntry Project;

    public string FlverPath = "";
    public FLVER2 SourceFlver = null;
    public FLVER0 SourceFlver0 = null;
    public bool[] FlverMeshSelection = Array.Empty<bool>();

    public bool HasSourceModel => SourceFlver != null || SourceFlver0 != null;

    public int MeshCount => SourceFlver != null ? SourceFlver.Meshes.Count : (SourceFlver0 != null ? SourceFlver0.Meshes.Count : 0);

    public string GetMeshMaterialName(int index)
    {
        if (SourceFlver != null && index >= 0 && index < SourceFlver.Meshes.Count)
        {
            var matIdx = SourceFlver.Meshes[index].MaterialIndex;
            return matIdx >= 0 && matIdx < SourceFlver.Materials.Count ? SourceFlver.Materials[matIdx].Name : "";
        }
        if (SourceFlver0 != null && index >= 0 && index < SourceFlver0.Meshes.Count)
        {
            var matIdx = SourceFlver0.Meshes[index].MaterialIndex;
            return matIdx >= 0 && matIdx < SourceFlver0.Materials.Count ? SourceFlver0.Materials[matIdx].Name : "";
        }
        return "";
    }

    public int GetMeshVertexCount(int index)
    {
        if (SourceFlver != null && index >= 0 && index < SourceFlver.Meshes.Count)
            return SourceFlver.Meshes[index].Vertices.Count;
        if (SourceFlver0 != null && index >= 0 && index < SourceFlver0.Meshes.Count)
            return SourceFlver0.Meshes[index].Vertices.Count;
        return 0;
    }

    public bool TriggerFlverSelectionMenu = false;
    private bool UpdateFlverSelectionList = true;
    private ModelListType CurrentTab = ModelListType.Asset;
    private ModelListType _previousTab = ModelListType.Asset;
    private Dictionary<ModelListType, HashSet<string>> CachedSearchMatches = new();
    private string FlverListFilter = "";
    private bool ExactFlverListFilter = false;
    private bool _arrowKeyPressed = false;

    public FlverSelectionMenu(HavokEditorView view, ProjectEntry project)
    {
        View = view;
        Project = project;
    }

    public void DisplayFlverSelectionTabs()
    {
        DisplayFlverSelectionHeader();

        ImGui.BeginChild("ContainerList", new Vector2(400, 600) * DPI.UIScale(), ImGuiChildFlags.Borders);

        ImGui.BeginTabBar("sourceTabs");

        // Objects / Assets
        var name = LOC.Get("HAVOK_FlverSelection_Tab_Objects");
        if (Project.Descriptor.ProjectType is ProjectType.ER 
            or ProjectType.AC6 
            or ProjectType.NR
            or ProjectType.DB_NT
            or ProjectType.DB)
        {
            name = LOC.Get("HAVOK_FlverSelection_Tab_Assets");
        }

        if (ImGui.BeginTabItem($"{name}##objectTab"))
        {
            CurrentTab = ModelListType.Asset;

            ImGui.BeginChild($"assetSourceList", new Vector2(0, 0), ImGuiChildFlags.Borders);

            DisplayModelSourceList(ModelListType.Asset, Project.Locator.AssetFiles);

            ImGui.EndChild();
            ImGui.EndTabItem();
        }

        // Map Pieces
        if (ImGui.BeginTabItem($"{LOC.Get("HAVOK_FlverSelection_MapPieces")}##mapPieceTab"))
        {
            CurrentTab = ModelListType.MapPiece;

            ImGui.BeginChild($"mapPieceSourceList", new Vector2(0, 0), ImGuiChildFlags.Borders);

            DisplayModelSourceList(ModelListType.MapPiece, Project.Locator.MapPieceFiles);

            ImGui.EndChild();
            ImGui.EndTabItem();
        }

        // Characters
        if (ImGui.BeginTabItem($"{LOC.Get("MODEL_ENUM_ModelListType_Characters")}##characterTab"))
        {
            CurrentTab = ModelListType.Character;

            ImGui.BeginChild($"chrSourceList", new Vector2(0, 0), ImGuiChildFlags.Borders);

            DisplayModelSourceList(ModelListType.Character, Project.Locator.ChrFiles);

            ImGui.EndChild();
            ImGui.EndTabItem();
        }

        // Parts
        if (ImGui.BeginTabItem($"{LOC.Get("MODEL_ENUM_ModelListType_Parts")}##partTab"))
        {
            CurrentTab = ModelListType.Part;

            ImGui.BeginChild($"partSourceList", new Vector2(0, 0), ImGuiChildFlags.Borders);

            DisplayModelSourceList(ModelListType.Part, Project.Locator.PartFiles);

            ImGui.EndChild();
            ImGui.EndTabItem();
        }

        ImGui.EndTabBar();

        ImGui.EndChild();
    }

    private static readonly Dictionary<ModelListType, ProjectAliasType> AliasTypeMap = new()
    {
        { ModelListType.Asset,     ProjectAliasType.Assets     },
        { ModelListType.MapPiece,  ProjectAliasType.MapPieces  },
        { ModelListType.Character, ProjectAliasType.Characters },
        { ModelListType.Part,      ProjectAliasType.Parts      },
    };

    public void DisplayFlverSelectionHeader()
    {
        ImGui.BeginChild($"havokFlverSelectionHeader", EditorFilters.GetHeaderSize(), ImGuiChildFlags.Borders);

        EditorFilters.DisplaySearchbar("flverSelection", ref FlverListFilter, ref ExactFlverListFilter);

        bool filterChanged = ImGui.IsItemDeactivatedAfterEdit();
        bool tabChanged = _previousTab != CurrentTab;

        GUI.Tooltip(LOC.Get("HAVOK_FlverSelection_Filter_TT"));

        ImGui.EndChild();

        if (filterChanged)
        {
            UpdateFlverSelectionList = true;
        }

        if (tabChanged)
        {
            _previousTab = CurrentTab;
            UpdateFlverSelectionList = true;
        }

        if (!UpdateFlverSelectionList)
            return;

        UpdateFlverSelectionList = false;

        // Get the right file dictionary for the current tab
        var fileDict = CurrentTab switch
        {
            ModelListType.Asset => Project.Locator.AssetFiles,
            ModelListType.MapPiece => Project.Locator.MapPieceFiles,
            ModelListType.Character => Project.Locator.ChrFiles,
            ModelListType.Part => Project.Locator.PartFiles,
            _ => null
        };
        if (fileDict == null) return;

        var matches = new HashSet<string>();

        foreach (var entry in fileDict.Entries)
        {
            var modelName = entry.Filename;
            var nameAlias = "";

            if (CFG.Current.ModelEditor_Containers_IncludeAliasInSearch
                && AliasTypeMap.TryGetValue(CurrentTab, out var aliasType))
            {
                nameAlias = AliasHelper.GetAlias(Project, modelName, CurrentTab);
            }

            if (EditorFilters.IsMatch(FlverListFilter, modelName, ExactFlverListFilter, nameAlias, true, true))
            {
                matches.Add(modelName);
            }
        }

        CachedSearchMatches[CurrentTab] = matches;
    }

    public void DisplayModelSourceList(ModelListType modelListType, FileDictionary fileDictionary)
    {
        if (!CachedSearchMatches.TryGetValue(modelListType, out var matches))
            return;

        if (InputManager.HasArrowSelection())
        {
            _arrowKeyPressed = true;
        }

        var filteredEntries = fileDictionary.Entries
            .Where(e => matches.Contains(e.Filename))
            .ToList();

        var clipper = new ImGuiListClipper();
        clipper.Begin(filteredEntries.Count);

        while (clipper.Step())
        {
            for (int i = clipper.DisplayStart; i < clipper.DisplayEnd; i++)
            {
                var fileEntry = filteredEntries[i];

                bool selected = FlverPath == fileEntry.Path;

                var displayedName = $"{fileEntry.Filename}";

                var alias = ModelEditorUtils.GetAliasForSourceListEntry(Project,
                    displayedName, modelListType);

                var flags = ImGuiSelectableFlags.None;
                if (CFG.Current.ModelEditor_ModelSourceList_RequireDoubleClick)
                {
                    flags = ImGuiSelectableFlags.AllowDoubleClick;
                }

                if (ImGui.Selectable($"{displayedName}##modelSourceListEntry{modelListType.ToString()}{i}", selected,
                    flags))
                {
                    FlverPath = fileEntry.Path;
                    LoadInternalFlver(fileEntry);
                }

                if (_arrowKeyPressed && ImGui.IsItemFocused() && !selected)
                {
                    FlverPath = fileEntry.Path;
                    LoadInternalFlver(fileEntry);

                    _arrowKeyPressed = false;
                }

                if (alias != "")
                {
                    GUI.DisplayAlias(alias, CFG.Current.Interface_Alias_Wordwrap_Model_Editor);
                }
            }
        }

        clipper.End();
    }

    public void LoadInternalFlver(FileDictionaryEntry fileEntry)
    {
        var binderType = ModelEditorUtils.GetContainerTypeFromRelativePath(Project, fileEntry.Path);

        if (binderType is ResourceContainerType.None)
        {
            ReadDirect(fileEntry);
        }
        else if (binderType is ResourceContainerType.BND)
        {
            ReadBND(fileEntry);
        }
        else if (binderType is ResourceContainerType.BXF)
        {
            ReadBXF(fileEntry);
        }
    }

    public void ReadDirect(FileDictionaryEntry fileEntry)
    {
        if (Project.VFS.FS.FileExists(fileEntry.Path))
        {
            try
            {
                var fileData = Project.VFS.FS.ReadFile(fileEntry.Path);
                LoadFlverFile(fileEntry, fileData);
            }
            catch (Exception ex)
            {
                Smithbox.LogError(this, LOC.Get("HAVOK_CollisionGen_Failed_FLVER_Read", fileEntry.Path), ex);
            }
        }
        else
        {
            Smithbox.LogError(this, LOC.Get("HAVOK_CollisionGen_Failed_FLVER_Find", fileEntry.Path));
        }
    }

    public void ReadBND(FileDictionaryEntry fileEntry)
    {
        if (Project.VFS.FS.FileExists(fileEntry.Path))
        {
            try
            {
                var fileData = Project.VFS.FS.ReadFile(fileEntry.Path);

                if (fileData != null)
                {
                    byte[] bytes = fileData.Value.ToArray();
                    if (DCX.Is(bytes))
                        bytes = DCX.Decompress(bytes).ToArray();

                    if (BND4.Is(bytes))
                    {
                        var binder = new BND4Reader(bytes);
                        foreach (var file in binder.Files)
                        {
                            var filename = FilePathUtils.GetPureFilename(file.Name);
                            if (string.Equals(filename, fileEntry.Filename, StringComparison.OrdinalIgnoreCase) ||
                                file.Name.EndsWith(".flver", StringComparison.OrdinalIgnoreCase) ||
                                file.Name.EndsWith(".flv", StringComparison.OrdinalIgnoreCase))
                            {
                                LoadFlverFile(fileEntry, binder.ReadFile(file));
                                return;
                            }
                        }
                    }
                    else if (BND3.Is(bytes))
                    {
                        using var binder = new BND3Reader(bytes);
                        foreach (var file in binder.Files)
                        {
                            var filename = FilePathUtils.GetPureFilename(file.Name);
                            if (string.Equals(filename, fileEntry.Filename, StringComparison.OrdinalIgnoreCase) ||
                                file.Name.EndsWith(".flver", StringComparison.OrdinalIgnoreCase) ||
                                file.Name.EndsWith(".flv", StringComparison.OrdinalIgnoreCase))
                            {
                                LoadFlverFile(fileEntry, binder.ReadFile(file));
                                return;
                            }
                        }
                    }
                }
            }
            catch (Exception e)
            {
                Smithbox.LogError(this, LOC.Get("HAVOK_CollisionGen_Failed_Binder_Read", fileEntry.Path), e);
            }
        }
        else
        {
            Smithbox.LogError(this, LOC.Get("HAVOK_CollisionGen_Failed_Binder_Find", fileEntry.Path));
        }
    }

    public void ReadBXF(FileDictionaryEntry fileEntry)
    {
        Memory<byte> bhd = new Memory<byte>();
        Memory<byte> bdt = new Memory<byte>();

        var targetBhdPath = fileEntry.Path;
        var targetBdtPath = fileEntry.Path.Replace("bhd", "bdt");

        if (Project.VFS.FS.FileExists(targetBhdPath) && Project.VFS.FS.FileExists(targetBdtPath))
        {
            bhd = (Memory<byte>)Project.VFS.FS.ReadFile(targetBhdPath);
            bdt = (Memory<byte>)Project.VFS.FS.ReadFile(targetBdtPath);

            if (bhd.Length == 0 || bdt.Length == 0)
                return;

            PopulateBXF(fileEntry, bhd, bdt);
        }
        else
        {
            Smithbox.LogError(this, LOC.Get("HAVOK_CollisionGen_Failed_Binder_Find", targetBhdPath));
        }
    }

    public void PopulateBXF(FileDictionaryEntry fileEntry, Memory<byte> bhdData, Memory<byte> bdtData)
    {
        try
        {
            var bhdBytes = bhdData.ToArray();
            var bdtBytes = bdtData.ToArray();

            if (BXF4.IsBHD(bhdBytes))
            {
                var binder = new BXF4Reader(bhdBytes, bdtBytes);
                foreach (var file in binder.Files)
                {
                    var filename = FilePathUtils.GetPureFilename(file.Name);
                    if (string.Equals(filename, fileEntry.Filename, StringComparison.OrdinalIgnoreCase) ||
                        file.Name.EndsWith(".flver", StringComparison.OrdinalIgnoreCase) ||
                        file.Name.EndsWith(".flv", StringComparison.OrdinalIgnoreCase))
                    {
                        LoadFlverFile(fileEntry, binder.ReadFile(file));
                        return;
                    }
                }
            }
            else
            {
                var binder = BXF3.Read(bhdBytes, bdtBytes);
                foreach (var file in binder.Files)
                {
                    var filename = FilePathUtils.GetPureFilename(file.Name);
                    if (string.Equals(filename, fileEntry.Filename, StringComparison.OrdinalIgnoreCase) ||
                        file.Name.EndsWith(".flver", StringComparison.OrdinalIgnoreCase) ||
                        file.Name.EndsWith(".flv", StringComparison.OrdinalIgnoreCase))
                    {
                        LoadFlverFile(fileEntry, file.Bytes);
                        return;
                    }
                }
            }
        }
        catch (Exception e)
        {
            Smithbox.LogError(this, LOC.Get("HAVOK_CollisionGen_Failed_Binder_Read", fileEntry.Path), e);
        }
    }

    public void LoadFlverFile(FileDictionaryEntry fileEntry, Memory<byte>? fileData)
    {
        if (fileData == null || fileData.Value.Length == 0)
            return;

        try
        {
            byte[] bytes = fileData.Value.ToArray();
            if (DCX.Is(bytes))
                bytes = DCX.Decompress(bytes).ToArray();

            if (bytes.Length < 12 || !ModelCollisionExtractor.IsFlverBytes(bytes))
                return;

            var bigEndian = bytes[6] == 'B';
            var version = bigEndian
                ? (bytes[8] << 24) | (bytes[9] << 16) | (bytes[10] << 8) | bytes[11]
                : (bytes[11] << 24) | (bytes[10] << 16) | (bytes[9] << 8) | bytes[8];

            SourceFlver = null;
            SourceFlver0 = null;

            if (version >= 0x20000)
            {
                SourceFlver = FLVER2.Read(bytes);
                FlverMeshSelection = new bool[SourceFlver.Meshes.Count];
            }
            else
            {
                SourceFlver0 = FLVER0.Read(bytes);
                FlverMeshSelection = new bool[SourceFlver0.Meshes.Count];
            }

            FlverPath = fileEntry.Path;
            Array.Fill(FlverMeshSelection, true);

            ImGui.CloseCurrentPopup();
        }
        catch (Exception e)
        {
            Smithbox.LogError(this, LOC.Get("HAVOK_CollisionGen_Failed_FLVER_Read", fileEntry.Filename), e);
        }
    }
}
