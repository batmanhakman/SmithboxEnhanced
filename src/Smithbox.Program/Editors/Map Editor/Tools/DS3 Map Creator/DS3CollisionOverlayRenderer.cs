using Havok.hk2014;
using Havok.Shared;
using StudioCore.Application;
using StudioCore.Editors.Common;
using StudioCore.Renderer;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Numerics;
using System.Text;

namespace StudioCore.Editors.MapEditor.DS3Creator;

/// <summary>
/// Viewport overlay rendering DS3 collision bounding volumes (mesh-tree AABB + compressed section AABBs) and an
/// optional edge-wireframe fallback for devices without GPU FillModeWireframe (M1.7).
/// Follows NavigationOverlayRenderer's overlap-frame pattern so updates never flicker.
/// </summary>
public sealed class DS3CollisionOverlayRenderer
{
    public bool ShowBoundingVolumes { get; set; }
    public bool ShowEdgeWireframeOverlay { get; set; }
    public bool SelectedCollisionOnly { get; set; } = true;

    public const int OverlapFrames = 2;

    private readonly List<(DebugPrimitiveRenderableProxy Proxy, Action Release)> Shown = new();
    private List<(DebugPrimitiveRenderableProxy Proxy, Action Release)> Pending;
    private int PendingShownFrames;
    private string LastStateKey = "";

    public void Update(MapEditorView view)
    {
        if (view?.Project == null || view.Project.Descriptor.ProjectType is not ProjectType.DS3)
        {
            Clear();
            return;
        }

        if (!ShowBoundingVolumes && !ShowEdgeWireframeOverlay)
        {
            Clear();
            return;
        }

        var scene = view.Universe?.GetCurrentScene();
        var map = view.Selection?.SelectedMapContainer;
        if (scene == null || map == null)
        {
            Clear();
            return;
        }

        AdvancePending();

        var targets = GatherCollisionEntities(view, map);
        var stateKey = BuildStateKey(view, map, targets);
        if (stateKey == LastStateKey)
            return;

        LastStateKey = stateKey;
        RebuildOverlay(view, scene, targets);
    }

    public void Invalidate()
    {
        LastStateKey = "";
    }

    public void Clear()
    {
        LastStateKey = "";
        Release(Shown);

        if (Pending != null)
        {
            Release(Pending);
            Pending = null;
        }

        PendingShownFrames = 0;
    }

    private void AdvancePending()
    {
        if (Pending == null)
            return;

        if (PendingShownFrames >= OverlapFrames)
        {
            Release(Shown);
            Shown.AddRange(Pending);
            Pending = null;
            PendingShownFrames = 0;
        }
        else if (PendingShownFrames > 0 || Pending.All(p => p.Proxy.IsRenderableConstructed))
        {
            foreach (var (proxy, _) in Pending)
            {
                if (!proxy.Visible)
                    proxy.Visible = true;
            }

            PendingShownFrames++;
        }
    }

    private List<MsbEntity> GatherCollisionEntities(MapEditorView view, MapContainer map)
    {
        var allCollisions = map.Objects
            .OfType<MsbEntity>()
            .Where(EntityHelper.IsPartCollision)
            .ToList();

        if (!SelectedCollisionOnly)
            return allCollisions;

        var selectedCollisions = view.ViewportSelection
            .GetFilteredSelection<MsbEntity>(e => EntityHelper.IsPartCollision(e) && e.MapID == map.Name)
            .ToList();

        // If user selected a MapPiece with a matching h###### collision, also highlight that collision if present
        if (selectedCollisions.Count == 0)
        {
            var selectedModelSuffixes = view.ViewportSelection
                .GetFilteredSelection<MsbEntity>(e => EntityHelper.IsPartMapPiece(e) && e.MapID == map.Name)
                .Select(DS3CollisionBuilder.ResolvePartModelName)
                .Where(m => !string.IsNullOrEmpty(m) && m.Length >= 2)
                .Select(m => m.Substring(1))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            if (selectedModelSuffixes.Count > 0)
            {
                selectedCollisions = allCollisions
                    .Where(c =>
                    {
                        var colModel = DS3CollisionBuilder.ResolvePartModelName(c);
                        return !string.IsNullOrEmpty(colModel) && colModel.Length >= 2 &&
                               selectedModelSuffixes.Contains(colModel.Substring(1));
                    })
                    .ToList();
            }
        }

        return selectedCollisions.Count > 0 ? selectedCollisions : allCollisions;
    }

    private string BuildStateKey(MapEditorView view, MapContainer map, List<MsbEntity> targets)
    {
        var sb = new StringBuilder();
        sb.Append(map.Name)
          .Append('|').Append(ShowBoundingVolumes)
          .Append('|').Append(ShowEdgeWireframeOverlay)
          .Append('|').Append(SelectedCollisionOnly)
          .Append('|').Append(CFG.Current.MapEditor_Display_High_Collision)
          .Append('|').Append(CFG.Current.MapEditor_Display_Low_Collision)
          .Append('|').Append(targets.Count);

        foreach (var t in targets)
        {
            var tr = t.GetWorldMatrix().Translation;
            sb.Append('|').Append(t.Name)
              .Append(':').Append(DS3CollisionBuilder.ResolvePartModelName(t))
              .Append(':').Append(MathF.Round(tr.X, 3))
              .Append(',').Append(MathF.Round(tr.Y, 3))
              .Append(',').Append(MathF.Round(tr.Z, 3));
        }

        return sb.ToString();
    }

    private void RebuildOverlay(MapEditorView view, RenderScene scene, List<MsbEntity> targets)
    {
        if (Pending != null)
        {
            Release(Pending);
            Pending = null;
        }

        var rootBoxLines = new List<(Vector3 A, Vector3 B)>();
        var sectionBoxLines = new List<(Vector3 A, Vector3 B)>();
        var wireframeLines = new List<(Vector3 A, Vector3 B)>();

        var layerPrefix = CFG.Current.MapEditor_Display_Low_Collision || !CFG.Current.MapEditor_Display_High_Collision
            ? "l"
            : "h";

        foreach (var entity in targets)
        {
            var modelName = DS3CollisionBuilder.ResolvePartModelName(entity);
            if (string.IsNullOrEmpty(modelName) || modelName.Length < 2)
                continue;

            var fileName = $"{layerPrefix}{entity.MapID.Substring(1)}_{modelName.Substring(1)}.hkx.dcx";
            var doc = view.HavokCollisionBank.PeekEditableDocument(fileName)
                ?? view.HavokCollisionBank.GetEditableDocument(fileName);

            if (doc == null)
                continue;

            var world = entity.GetWorldMatrix();

            foreach (var body in CollisionGeometry.Bodies(doc.Objects))
            {
                var bodyWorld = CollisionGeometry.BodyTransform(body) * world;

                if (ShowBoundingVolumes && body.Shape is fsnpCustomParamCompressedMeshShape meshShape &&
                    meshShape.m_data?.m_meshTree is { } tree)
                {
                    AppendTransformedBox(rootBoxLines, AsVec3(tree.m_domain.m_min), AsVec3(tree.m_domain.m_max), bodyWorld);

                    if (tree.m_sections != null)
                    {
                        foreach (var section in tree.m_sections)
                        {
                            AppendTransformedBox(sectionBoxLines, AsVec3(section.m_domain.m_min), AsVec3(section.m_domain.m_max), bodyWorld);
                        }
                    }
                }

                if (ShowEdgeWireframeOverlay)
                {
                    var tris = new List<Vector3>();
                    if (CollisionGeometry.AppendTriangles(body.Shape, bodyWorld, tris))
                    {
                        for (int i = 0; i + 2 < tris.Count; i += 3)
                        {
                            var a = tris[i];
                            var b = tris[i + 1];
                            var c = tris[i + 2];
                            wireframeLines.Add((a, b));
                            wireframeLines.Add((b, c));
                            wireframeLines.Add((c, a));
                        }
                    }
                }
            }
        }

        var built = new List<(DebugPrimitiveRenderableProxy Proxy, Action Release)>();
        void AddLines(List<(Vector3 A, Vector3 B)> lines, Color color)
        {
            if (lines.Count == 0)
                return;

            foreach (var chunk in lines.Chunk(DbgPrimLineBatch.MaxLines))
            {
                var prim = new DbgPrimLineBatch(chunk);
                var proxy = new DebugPrimitiveRenderableProxy(scene.OpaqueRenderables, prim)
                {
                    Pickable = false,
                    Visible = false,
                    BaseColor = color,
                    HighlightedColor = color,
                    RenderOverlay = true
                };
                built.Add((proxy, prim.Release));
            }
        }

        AddLines(rootBoxLines, Color.FromArgb(255, 0, 235, 255));    // Cyan root domain AABB
        AddLines(sectionBoxLines, Color.FromArgb(255, 255, 210, 40)); // Gold section AABBs
        AddLines(wireframeLines, Color.FromArgb(255, 110, 255, 140)); // Mint edge wireframe

        if (built.Count == 0)
        {
            Release(Shown);
            PendingShownFrames = 0;
            return;
        }

        Pending = built;
        PendingShownFrames = 0;
    }

    private static void AppendTransformedBox(List<(Vector3 A, Vector3 B)> lines, Vector3 min, Vector3 max, Matrix4x4 transform)
    {
        if (!float.IsFinite(min.X) || !float.IsFinite(max.X) || min.X > max.X + 1e-3f)
            return;

        Vector3 T(float x, float y, float z) => Vector3.Transform(new Vector3(x, y, z), transform);

        var c000 = T(min.X, min.Y, min.Z);
        var c100 = T(max.X, min.Y, min.Z);
        var c110 = T(max.X, max.Y, min.Z);
        var c010 = T(min.X, max.Y, min.Z);

        var c001 = T(min.X, min.Y, max.Z);
        var c101 = T(max.X, min.Y, max.Z);
        var c111 = T(max.X, max.Y, max.Z);
        var c011 = T(min.X, max.Y, max.Z);

        // Bottom rect
        lines.Add((c000, c100));
        lines.Add((c100, c101));
        lines.Add((c101, c001));
        lines.Add((c001, c000));

        // Top rect
        lines.Add((c010, c110));
        lines.Add((c110, c111));
        lines.Add((c111, c011));
        lines.Add((c011, c010));

        // Pillars
        lines.Add((c000, c010));
        lines.Add((c100, c110));
        lines.Add((c101, c111));
        lines.Add((c001, c011));
    }

    private static Vector3 AsVec3(Vector4 v) => new(v.X, v.Y, v.Z);

    private static void Release(List<(DebugPrimitiveRenderableProxy Proxy, Action Release)> parts)
    {
        foreach (var (proxy, release) in parts)
        {
            proxy.Dispose();
            release();
        }

        parts.Clear();
    }
}
