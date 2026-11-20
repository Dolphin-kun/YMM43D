using System.Collections.Immutable;
using System.Numerics;
using System.Windows.Input;
using YMM43D.Commons;
using YMM43D.Project.Shape;
using YukkuriMovieMaker.Commons;

namespace YMM43D.PreviewTool.ViewModels
{
    public partial class Preview3DViewModel
    {
        private const float JointReach = 10f;
        private const float SameJoint = 0.5f;

        private JointDrag? jointDrag;

        public bool EditsBones
        {
            get;
            set
            {
                var wanted = value && isEditingMesh && renderer.MeshEdit is { Parameter.HasBones: true };

                if (field == wanted)
                    return;

                field = wanted;
                jointDrag = null;
                OnPropertyChanged(nameof(EditsBones));
                TouchHint();
            }
        }

        public ICommand ToggleBoneEditCommand => field ??= new ActionCommand(_ => isEditingMesh, _ => EditsBones = !EditsBones);

        private bool GrabJoint(in MeshEditTarget edit, Vector2 screen)
        {
            var bones = edit.Parameter.Bones;
            var found = -1;
            var part = JointPart.Whole;
            var nearest = JointReach;

            foreach (var bone in renderer.Bones)
            {
                if (!ReferenceEquals(bone.Item, edit.Item) || bone.Index >= bones.Count)
                    continue;

                if (renderer.ToScreen(bone.Position) is not { } head || renderer.ToScreen(bone.Tail) is not { } tail)
                    continue;

                foreach (var (distance, kind) in new[] { (Vector2.Distance(screen, head), JointPart.Head), (Vector2.Distance(screen, tail), JointPart.Tail), (Along(screen, head, tail) + JointReach / 2f, JointPart.Whole) })
                {
                    if (distance >= nearest)
                        continue;

                    nearest = distance;
                    found = bone.Index;
                    part = kind;
                }
            }

            if (found < 0)
                return false;

            var item = edit.Item;
            var grabbed = renderer.Bones.First(bone => ReferenceEquals(bone.Item, item) && bone.Index == found);
            var start = bones.Select(bone => (Head: new Vector3((float)bone.HeadX, (float)bone.HeadY, (float)bone.HeadZ), Tail: new Vector3((float)bone.TailX, (float)bone.TailY, (float)bone.TailZ))).ToArray();
            var plane = part switch
            {
                JointPart.Head => grabbed.Position,
                JointPart.Tail => grabbed.Tail,
                _ => (grabbed.Position + grabbed.Tail) / 2f,
            };

            SeparateHistory();
            jointDrag = new JointDrag(Connected(bones, start, found, part), start, plane, freeCamera.State.Forward);

            return true;
        }

        private bool DragJoint(Vector2 screen)
        {
            if (jointDrag is not { } drag)
                return false;

            if (renderer.MeshEdit is not { } edit
                || renderer.CreateRay(screen) is not { } ray
                || ray.IntersectPlane(drag.Plane, drag.Normal) is not { } hit
                || !Matrix4x4.Invert(edit.ToWorld, out var back))
            {
                return true;
            }

            var shift = WorldScale.ToPixelOffset(Vector3.TransformNormal(hit - drag.Plane, back));
            var bones = edit.Parameter.Bones;

            foreach (var (index, tail) in drag.Moved)
            {
                if (index >= bones.Count)
                    continue;

                var to = (tail ? drag.Start[index].Tail : drag.Start[index].Head) + shift;
                var bone = bones[index];

                if (tail)
                {
                    bone.TailX = to.X;
                    bone.TailY = to.Y;
                    bone.TailZ = to.Z;
                }
                else
                {
                    bone.HeadX = to.X;
                    bone.HeadY = to.Y;
                    bone.HeadZ = to.Z;
                }
            }

            meshRevision++;
            refresher?.ForceRefresh(timeline!);

            return true;
        }

        private void EndJoint()
        {
            jointDrag = null;
            SeparateHistory();
        }

        private static (int Index, bool Tail)[] Connected(ImmutableList<ShapeBone> bones, (Vector3 Head, Vector3 Tail)[] start, int index, JointPart part)
        {
            var moved = new HashSet<(int, bool)>();

            void Joint(Vector3 at)
            {
                for (var i = 0; i < bones.Count; i++)
                {
                    if (Vector3.Distance(start[i].Head, at) < SameJoint)
                        moved.Add((i, false));

                    if (Vector3.Distance(start[i].Tail, at) < SameJoint)
                        moved.Add((i, true));
                }
            }

            if (part is not JointPart.Tail)
            {
                moved.Add((index, false));
                Joint(start[index].Head);
            }

            if (part is not JointPart.Head)
            {
                moved.Add((index, true));
                Joint(start[index].Tail);
            }

            return [.. moved];
        }

        private static float Along(Vector2 point, Vector2 from, Vector2 to)
        {
            var span = to - from;
            var length = span.LengthSquared();
            var at = length > 1e-6f ? Math.Clamp(Vector2.Dot(point - from, span) / length, 0f, 1f) : 0f;

            return Vector2.Distance(point, from + span * at);
        }

        private enum JointPart
        {
            Head,
            Tail,
            Whole,
        }

        private sealed record JointDrag((int Index, bool Tail)[] Moved, (Vector3 Head, Vector3 Tail)[] Start, Vector3 Plane, Vector3 Normal);
    }
}
