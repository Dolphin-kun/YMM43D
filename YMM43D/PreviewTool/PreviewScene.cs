using System.Numerics;
using YMM43D.Commons;
using YMM43D.Player;
using YMM43D.PreviewTool.Rendering;
using YukkuriMovieMaker.Project.Items;

namespace YMM43D.PreviewTool
{
    internal sealed class PreviewScene
    {
        public required CameraPose ViewPose { get; init; }

        public required CameraState SceneCamera { get; init; }

        public SceneLighting Lighting { get; init; } = SceneLighting.Default;

        public CameraPose SceneCameraPose => SceneCamera.GetPose();

        public float ScreenWidth => Environment.SourceDescription?.ScreenSize.Width ?? 0f;

        public float ScreenHeight => Environment.SourceDescription?.ScreenSize.Height ?? 0f;

        public Vector2 GetScreenTangent(float pixelsPerTangent)
        {
            if (ScreenWidth <= 0f || ScreenHeight <= 0f || pixelsPerTangent <= 0f)
                return new Vector2(0.96f, 0.54f);

            return new Vector2(ScreenWidth, ScreenHeight) / (2f * pixelsPerTangent);
        }

        public required FrameContext Time { get; init; }

        public required PreviewEnvironment Environment { get; init; }

        public required IReadOnlyList<PreviewItem> Items { get; init; }

        public IReadOnlyCollection<IVideoItem> Selection { get; init; } = [];

        public int SelectedBone { get; init; } = -1;

        public bool ShowsMesh { get; init; }

        public IReadOnlyList<SceneMarkerResolver.PlacedMarker> Markers { get; init; } = [];

        public IItem? SelectedMarker { get; init; }

        public bool ShowsGrid { get; init; } = true;

        public GizmoHandle ActiveHandle { get; init; }

        public bool IsOrthographic { get; init; }

        public bool IsEditingMesh { get; init; }

        public MeshSelection MeshSelection { get; init; } = new();

        public bool XRay { get; init; }

        public PivotPoint Pivot { get; init; }

        public Vector3 Cursor { get; init; }

        public ScreenOverlay? Overlay { get; init; }

        public long MeshRevision { get; init; }

        public int CutEdge { get; init; } = -1;

        public bool ShowsInflateHandle { get; init; }

        public float ViewDistance { get; init; } = SceneProjection.DefaultFocalDistance;

        public GizmoSpace Space { get; init; }

        public GizmoMode Mode { get; init; }
    }
}
