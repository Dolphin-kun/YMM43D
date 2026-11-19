using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Vortice.Direct3D11;
using Vortice.DXGI;

namespace YMM43D.Graphics.Meshes
{
    public sealed class MeshDistanceField : IDisposable
    {
        private const string Shader = "MeshDistanceField.hlsl";
        private const string Profile = "cs_5_0";
        private const int GroupSize = 4;
        private const long PairsPerDispatch = 1L << 30;
        private const float NearReach = 6f;

        private readonly ID3D11Device device;
        private readonly ID3D11ComputeShader bound;
        private readonly ID3D11ComputeShader accumulate;
        private readonly ID3D11ComputeShader resolve;
        private readonly ID3D11Buffer constants;

        private ID3D11Texture3D? texture;
        private ID3D11UnorderedAccessView? fieldTarget;
        private ID3D11Texture3D? normalTexture;
        private ID3D11UnorderedAccessView? normalTarget;
        private ID3D11Buffer? state;
        private ID3D11UnorderedAccessView? stateTarget;
        private ID3D11Buffer? bounds;
        private ID3D11UnorderedAccessView? boundsTarget;
        private (int X, int Y, int Z) reserved;
        private (SurfaceGeometry? Geometry, DistanceFieldLayout Layout) baked;

        private static readonly ConditionalWeakTable<SurfaceGeometry, StrongBox<bool>> openness = [];

        public MeshDistanceField(ID3D11Device device)
        {
            this.device = device;

            var assembly = typeof(MeshDistanceField).Assembly;

            bound = device.CreateComputeShader(ShaderLibrary.Compile(assembly, Shader, "CSBound", Profile));
            accumulate = device.CreateComputeShader(ShaderLibrary.Compile(assembly, Shader, "CSAccumulate", Profile));
            resolve = device.CreateComputeShader(ShaderLibrary.Compile(assembly, Shader, "CSResolve", Profile));
            constants = D3D11Buffers.CreateConstantBuffer<FieldConstants>(device);
        }

        public ID3D11ShaderResourceView? Distances { get; private set; }

        public ID3D11ShaderResourceView? Normals { get; private set; }

        public static bool IsOpen(SurfaceGeometry geometry) => openness.GetValue(geometry, found => new StrongBox<bool>(HasBorder(found))).Value;

        public bool Bake(ID3D11DeviceContext context, SurfaceGeometry geometry, in DistanceFieldLayout layout)
        {
            if (Distances is not null && ReferenceEquals(baked.Geometry, geometry) && baked.Layout == layout)
                return true;

            var (corners, normals) = Triangulate(geometry, layout);

            if (corners.Length == 0)
                return false;

            Reserve(layout.CellsX, layout.CellsY, layout.CellsZ);

            using var source = Upload(corners);
            using var sourceView = device.CreateShaderResourceView(source);
            using var normalSource = Upload(normals);
            using var normalSourceView = device.CreateShaderResourceView(normalSource);

            var triangles = corners.Length / 3;
            var chunk = (int)Math.Clamp(PairsPerDispatch / Math.Max(layout.CellCount, 1), 1, triangles);

            var groupsX = Groups(layout.CellsX);
            var groupsY = Groups(layout.CellsY);
            var groupsZ = Groups(layout.CellsZ);

            try
            {
                context.CSSetConstantBuffer(0, constants);
                context.CSSetShaderResource(0, sourceView);
                context.CSSetShaderResource(1, normalSourceView);
                context.CSSetUnorderedAccessView(0, stateTarget!);
                context.CSSetUnorderedAccessView(1, fieldTarget!);
                context.CSSetUnorderedAccessView(2, boundsTarget!);
                context.CSSetUnorderedAccessView(3, normalTarget!);

                Sweep(context, bound, layout, triangles, chunk, groupsX, groupsY, groupsZ);
                Sweep(context, accumulate, layout, triangles, chunk, groupsX, groupsY, groupsZ);

                context.CSSetShader(resolve);
                context.Dispatch(groupsX, groupsY, groupsZ);
            }
            finally
            {
                context.CSSetUnorderedAccessView(0, null!);
                context.CSSetUnorderedAccessView(1, null!);
                context.CSSetUnorderedAccessView(2, null!);
                context.CSSetUnorderedAccessView(3, null!);
                context.CSSetShaderResource(0, null!);
                context.CSSetShaderResource(1, null!);
                context.CSSetShader(null);
            }

            baked = (geometry, layout);

            return true;
        }

        private ID3D11Buffer Upload(Vector3[] data)
        {
            var stride = Marshal.SizeOf<Vector3>();

            return device.CreateBuffer(data, new BufferDescription
            {
                Usage = ResourceUsage.Default,
                BindFlags = BindFlags.ShaderResource,
                MiscFlags = ResourceOptionFlags.BufferStructured,
                StructureByteStride = stride,
                ByteWidth = stride * data.Length,
            });
        }

        private void Sweep(
            ID3D11DeviceContext context,
            ID3D11ComputeShader shader,
            in DistanceFieldLayout layout,
            int triangles,
            int chunk,
            int groupsX,
            int groupsY,
            int groupsZ)
        {
            context.CSSetShader(shader);

            for (var first = 0; first < triangles; first += chunk)
            {
                var settings = Constants(layout, first, Math.Min(chunk, triangles - first));

                context.UpdateSubresource(in settings, constants);
                context.Dispatch(groupsX, groupsY, groupsZ);
            }
        }

        private static FieldConstants Constants(in DistanceFieldLayout layout, int first, int count) => new()
        {
            Origin = -layout.Size / 2f,
            CellSize = layout.CellSize,
            CellsX = (uint)layout.CellsX,
            CellsY = (uint)layout.CellsY,
            CellsZ = (uint)layout.CellsZ,
            First = (uint)first,
            Count = (uint)count,
            Reset = first == 0 ? 1u : 0u,
            NearReach = NearReach,
        };

        private static int Groups(int cells) => (cells + GroupSize - 1) / GroupSize;

        private static (Vector3[] Corners, Vector3[] Normals) Triangulate(SurfaceGeometry geometry, in DistanceFieldLayout layout)
        {
            var corners = new List<Vector3>();
            var normals = new List<Vector3>();
            var vertices = geometry.Vertices;
            var flipped = Orient(geometry);
            var center = layout.Center;
            var scale = layout.Scale;
            Span<int> picked = stackalloc int[3];

            for (var f = 0; f < geometry.Faces.Length; f++)
            {
                var face = geometry.Faces[f];
                var indices = face.Indices;
                var flat = FaceNormal(vertices, indices);
                var (second, third) = flipped[f] ? (2, 1) : (1, 2);

                for (var i = 1; i + 1 < indices.Length; i++)
                {
                    picked[0] = indices[0];
                    picked[1] = indices[i + second - 1];
                    picked[2] = indices[i + third - 1];

                    foreach (var at in picked)
                    {
                        var shading = face.IsSmooth && geometry.Normals[at].LengthSquared() > 1e-12f
                            ? geometry.Normals[at]
                            : flat;

                        corners.Add((vertices[at] - center) * scale);
                        normals.Add(Vector3.Normalize(shading / scale));
                    }
                }
            }

            return ([.. corners], [.. normals]);
        }

        private static Vector3 FaceNormal(Vector3[] vertices, int[] indices)
        {
            var sum = Vector3.Zero;

            for (var i = 0; i < indices.Length; i++)
                sum += Vector3.Cross(vertices[indices[i]], vertices[indices[(i + 1) % indices.Length]]);

            return sum.LengthSquared() > 1e-20f ? Vector3.Normalize(sum) : Vector3.UnitZ;
        }

        private static bool[] Orient(SurfaceGeometry geometry)
        {
            var faces = geometry.Faces;
            var welded = Weld(geometry.Vertices);
            var edges = new Dictionary<(int, int), List<(int Face, bool Forward)>>();

            for (var f = 0; f < faces.Length; f++)
            {
                foreach (var (key, forward) in EdgesOf(faces[f].Indices, welded))
                {
                    if (!edges.TryGetValue(key, out var sharing))
                        edges[key] = sharing = [];

                    sharing.Add((f, forward));
                }
            }

            var flipped = new bool[faces.Length];
            var seen = new bool[faces.Length];
            var queue = new Queue<int>();
            var piece = new List<int>();

            for (var start = 0; start < faces.Length; start++)
            {
                if (seen[start])
                    continue;

                seen[start] = true;
                queue.Enqueue(start);
                piece.Clear();

                while (queue.TryDequeue(out var face))
                {
                    piece.Add(face);

                    foreach (var (key, forward) in EdgesOf(faces[face].Indices, welded))
                    {
                        var along = forward ^ flipped[face];

                        foreach (var (other, otherForward) in edges[key])
                        {
                            if (seen[other])
                                continue;

                            seen[other] = true;
                            flipped[other] = otherForward == along;
                            queue.Enqueue(other);
                        }
                    }
                }

                if (piece.Count(face => flipped[face]) * 2 <= piece.Count)
                    continue;

                foreach (var face in piece)
                    flipped[face] = !flipped[face];
            }

            return flipped;
        }

        private static bool HasBorder(SurfaceGeometry geometry)
        {
            var welded = Weld(geometry.Vertices);
            var uses = new Dictionary<(int, int), int>();

            foreach (var face in geometry.Faces)
            {
                foreach (var (key, _) in EdgesOf(face.Indices, welded))
                    uses[key] = uses.GetValueOrDefault(key) + 1;
            }

            return uses.Values.Any(count => count == 1);
        }

        private static int[] Weld(Vector3[] vertices)
        {
            var ids = new Dictionary<Vector3, int>();
            var welded = new int[vertices.Length];

            for (var i = 0; i < vertices.Length; i++)
            {
                if (!ids.TryGetValue(vertices[i], out var id))
                {
                    id = ids.Count;
                    ids.Add(vertices[i], id);
                }

                welded[i] = id;
            }

            return welded;
        }

        private static IEnumerable<((int, int) Key, bool Forward)> EdgesOf(int[] indices, int[] welded)
        {
            for (var i = 0; i < indices.Length; i++)
            {
                var from = welded[indices[i]];
                var to = welded[indices[(i + 1) % indices.Length]];

                if (from == to)
                    continue;

                yield return from < to ? ((from, to), true) : ((to, from), false);
            }
        }

        private void Reserve(int x, int y, int z)
        {
            if (Distances is not null && reserved == (x, y, z))
                return;

            Release();

            texture = device.CreateTexture3D(new Texture3DDescription
            {
                Width = x,
                Height = y,
                Depth = z,
                MipLevels = 1,
                Format = Format.R16_Float,
                Usage = ResourceUsage.Default,
                BindFlags = BindFlags.ShaderResource | BindFlags.UnorderedAccess,
            });

            Distances = device.CreateShaderResourceView(texture);
            fieldTarget = device.CreateUnorderedAccessView(texture);

            normalTexture = device.CreateTexture3D(new Texture3DDescription
            {
                Width = x,
                Height = y,
                Depth = z,
                MipLevels = 1,
                Format = Format.R16G16B16A16_Float,
                Usage = ResourceUsage.Default,
                BindFlags = BindFlags.ShaderResource | BindFlags.UnorderedAccess,
            });

            Normals = device.CreateShaderResourceView(normalTexture);
            normalTarget = device.CreateUnorderedAccessView(normalTexture);

            var stride = Marshal.SizeOf<CellState>();

            state = device.CreateBuffer(new BufferDescription
            {
                Usage = ResourceUsage.Default,
                BindFlags = BindFlags.UnorderedAccess,
                MiscFlags = ResourceOptionFlags.BufferStructured,
                StructureByteStride = stride,
                ByteWidth = stride * x * y * z,
            });

            stateTarget = device.CreateUnorderedAccessView(state);

            var bricks = Groups(x) * Groups(y) * Groups(z);
            var boundStride = Marshal.SizeOf<float>();

            bounds = device.CreateBuffer(new BufferDescription
            {
                Usage = ResourceUsage.Default,
                BindFlags = BindFlags.UnorderedAccess,
                MiscFlags = ResourceOptionFlags.BufferStructured,
                StructureByteStride = boundStride,
                ByteWidth = boundStride * bricks,
            });

            boundsTarget = device.CreateUnorderedAccessView(bounds);
            reserved = (x, y, z);
        }

        private void Release()
        {
            baked = default;
            reserved = default;

            boundsTarget?.Dispose();
            bounds?.Dispose();
            stateTarget?.Dispose();
            state?.Dispose();
            normalTarget?.Dispose();
            Normals?.Dispose();
            normalTexture?.Dispose();
            fieldTarget?.Dispose();
            Distances?.Dispose();
            texture?.Dispose();

            boundsTarget = null;
            bounds = null;
            stateTarget = null;
            state = null;
            normalTarget = null;
            Normals = null;
            normalTexture = null;
            fieldTarget = null;
            Distances = null;
            texture = null;
        }

        public void Dispose()
        {
            Release();
            constants.Dispose();
            resolve.Dispose();
            accumulate.Dispose();
            bound.Dispose();
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct CellState
        {
            public float Squared;
            public float Winding;
            public uint Nearest;
            public uint Unused;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct FieldConstants
        {
            public Vector3 Origin;
            public float CellSize;
            public uint CellsX;
            public uint CellsY;
            public uint CellsZ;
            public uint First;
            public uint Count;
            public uint Reset;
            public float NearReach;
            public uint Unused;
        }
    }
}
