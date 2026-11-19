cbuffer FieldConstants : register(b0)
{
    float3 Origin;
    float CellSize;
    uint3 Cells;
    uint First;
    uint Count;
    uint Reset;
    float NearReach;
    uint Unused;
};

struct CellState
{
    float Squared;
    float Winding;
    uint Nearest;
    uint Unused;
};

StructuredBuffer<float3> Corners : register(t0);
StructuredBuffer<float3> CornerNormals : register(t1);
RWStructuredBuffer<CellState> State : register(u0);
RWTexture3D<float> Field : register(u1);
RWStructuredBuffer<float> Bounds : register(u2);
RWTexture3D<float4> Normals : register(u3);

#define BRICK 4
#define LANES 64

static const float HalfSphere = 6.28318530718;

groupshared float3 SharedA[LANES];
groupshared float3 SharedB[LANES];
groupshared float3 SharedC[LANES];
groupshared float SharedReach[LANES];
groupshared float Reduce[LANES];

float LengthSquared(float3 v)
{
    return dot(v, v);
}

float EdgeSquared(float3 edge, float3 toPoint)
{
    float along = saturate(dot(edge, toPoint) / max(LengthSquared(edge), 1e-20));

    return LengthSquared(edge * along - toPoint);
}

float TriangleSquared(float3 p, float3 a, float3 b, float3 c)
{
    float3 ba = b - a;
    float3 cb = c - b;
    float3 ac = a - c;
    float3 pa = p - a;
    float3 pb = p - b;
    float3 pc = p - c;
    float3 normal = cross(ba, ac);
    float area = LengthSquared(normal);

    bool outside = area < 1e-20
        || sign(dot(cross(ba, normal), pa)) + sign(dot(cross(cb, normal), pb)) + sign(dot(cross(ac, normal), pc)) < 2.0;

    if (outside)
        return min(min(EdgeSquared(ba, pa), EdgeSquared(cb, pb)), EdgeSquared(ac, pc));

    float height = dot(normal, pa);

    return height * height / area;
}

float SolidAngle(float3 p, float3 a, float3 b, float3 c)
{
    a -= p;
    b -= p;
    c -= p;

    float la = length(a);
    float lb = length(b);
    float lc = length(c);

    float above = dot(a, cross(b, c));
    float beside = la * lb * lc + dot(a, b) * lc + dot(a, c) * lb + dot(b, c) * la;

    return 2.0 * atan2(above, beside);
}

float3 Barycentric(float3 p, float3 a, float3 b, float3 c)
{
    float3 ab = b - a;
    float3 ac = c - a;
    float3 ap = p - a;

    float d1 = dot(ab, ap);
    float d2 = dot(ac, ap);

    if (d1 <= 0.0 && d2 <= 0.0)
        return float3(1.0, 0.0, 0.0);

    float3 bp = p - b;
    float d3 = dot(ab, bp);
    float d4 = dot(ac, bp);

    if (d3 >= 0.0 && d4 <= d3)
        return float3(0.0, 1.0, 0.0);

    float vc = d1 * d4 - d3 * d2;

    if (vc <= 0.0 && d1 >= 0.0 && d3 <= 0.0)
    {
        float v = d1 / max(d1 - d3, 1e-20);
        return float3(1.0 - v, v, 0.0);
    }

    float3 cp = p - c;
    float d5 = dot(ab, cp);
    float d6 = dot(ac, cp);

    if (d6 >= 0.0 && d5 <= d6)
        return float3(0.0, 0.0, 1.0);

    float vb = d5 * d2 - d1 * d6;

    if (vb <= 0.0 && d2 >= 0.0 && d6 <= 0.0)
    {
        float w = d2 / max(d2 - d6, 1e-20);
        return float3(1.0 - w, 0.0, w);
    }

    float va = d3 * d6 - d5 * d4;

    if (va <= 0.0 && (d4 - d3) >= 0.0 && (d5 - d6) >= 0.0)
    {
        float w = (d4 - d3) / max((d4 - d3) + (d5 - d6), 1e-20);
        return float3(0.0, 1.0 - w, w);
    }

    float scale = 1.0 / max(va + vb + vc, 1e-20);
    float v = vb * scale;
    float w = vc * scale;

    return float3(1.0 - v - w, v, w);
}

uint IndexOf(uint3 cell)
{
    return cell.x + Cells.x * (cell.y + Cells.y * cell.z);
}

uint BrickOf(uint3 group)
{
    uint3 bricks = (Cells + BRICK - 1) / BRICK;

    return group.x + bricks.x * (group.y + bricks.y * group.z);
}

float3 BrickCenter(uint3 group)
{
    return Origin + (float3(group) + 0.5) * (BRICK * CellSize);
}

float BrickReach()
{
    return sqrt(3.0) * 0.5 * BRICK * CellSize;
}

void LoadCorners(uint index, out float3 a, out float3 b, out float3 c)
{
    uint at = (First + index) * 3;

    a = Corners[at];
    b = Corners[at + 1];
    c = Corners[at + 2];
}

float ReduceMin(uint lane, float value)
{
    Reduce[lane] = value;
    GroupMemoryBarrierWithGroupSync();

    [unroll]
    for (uint step = LANES / 2; step > 0; step >>= 1)
    {
        if (lane < step)
            Reduce[lane] = min(Reduce[lane], Reduce[lane + step]);

        GroupMemoryBarrierWithGroupSync();
    }

    float result = Reduce[0];
    GroupMemoryBarrierWithGroupSync();

    return result;
}

float ReduceSum(uint lane, float value)
{
    Reduce[lane] = value;
    GroupMemoryBarrierWithGroupSync();

    [unroll]
    for (uint step = LANES / 2; step > 0; step >>= 1)
    {
        if (lane < step)
            Reduce[lane] += Reduce[lane + step];

        GroupMemoryBarrierWithGroupSync();
    }

    float result = Reduce[0];
    GroupMemoryBarrierWithGroupSync();

    return result;
}

[numthreads(BRICK, BRICK, BRICK)]
void CSBound(uint3 group : SV_GroupID, uint lane : SV_GroupIndex)
{
    float3 center = BrickCenter(group);
    float best = 1e30;

    [loop]
    for (uint i = lane; i < Count; i += LANES)
    {
        float3 a, b, c;
        LoadCorners(i, a, b, c);

        best = min(best, TriangleSquared(center, a, b, c));
    }

    float nearest = sqrt(ReduceMin(lane, best));

    if (lane == 0)
    {
        uint brick = BrickOf(group);

        Bounds[brick] = Reset != 0 ? nearest : min(Bounds[brick], nearest);
    }
}

[numthreads(BRICK, BRICK, BRICK)]
void CSAccumulate(uint3 cell : SV_DispatchThreadID, uint3 group : SV_GroupID, uint lane : SV_GroupIndex)
{
    bool inField = all(cell < Cells);
    uint index = inField ? IndexOf(cell) : 0;

    CellState state;

    if (Reset != 0 || !inField)
    {
        state.Squared = 1e30;
        state.Winding = 0.0;
        state.Nearest = 0;
        state.Unused = 0;
    }
    else
    {
        state = State[index];
    }

    float3 center = BrickCenter(group);
    float3 p = Origin + (float3(cell) + 0.5) * CellSize;

    float reach = BrickReach();
    float candidate = Bounds[BrickOf(group)] + 2.0 * reach;
    float near = NearReach * reach;
    float far = 0.0;

    [loop]
    for (uint first = 0; first < Count; first += LANES)
    {
        uint mine = first + lane;

        if (mine < Count)
        {
            float3 a, b, c;
            LoadCorners(mine, a, b, c);

            SharedA[lane] = a;
            SharedB[lane] = b;
            SharedC[lane] = c;

            float distance = sqrt(TriangleSquared(center, a, b, c));
            SharedReach[lane] = distance;

            if (distance > near)
                far += SolidAngle(center, a, b, c);
        }

        GroupMemoryBarrierWithGroupSync();

        uint loaded = min(LANES, Count - first);

        [loop]
        for (uint i = 0; i < loaded; i++)
        {
            float distance = SharedReach[i];

            if (distance <= candidate)
            {
                float squared = TriangleSquared(p, SharedA[i], SharedB[i], SharedC[i]);

                if (squared < state.Squared)
                {
                    state.Squared = squared;
                    state.Nearest = First + first + i;
                }
            }

            if (distance <= near)
                state.Winding += SolidAngle(p, SharedA[i], SharedB[i], SharedC[i]);
        }

        GroupMemoryBarrierWithGroupSync();
    }

    state.Winding += ReduceSum(lane, far);

    if (inField)
        State[index] = state;
}

[numthreads(BRICK, BRICK, BRICK)]
void CSResolve(uint3 cell : SV_DispatchThreadID)
{
    if (any(cell >= Cells))
        return;

    CellState state = State[IndexOf(cell)];

    bool inside = abs(state.Winding) > HalfSphere;
    float distance = sqrt(state.Squared);

    Field[cell] = inside ? -distance : distance;

    uint at = state.Nearest * 3;
    float3 a = Corners[at];
    float3 b = Corners[at + 1];
    float3 c = Corners[at + 2];

    float3 p = Origin + (float3(cell) + 0.5) * CellSize;
    float3 weights = Barycentric(p, a, b, c);
    float3 closest = a * weights.x + b * weights.y + c * weights.z;

    float3 normal = CornerNormals[at] * weights.x + CornerNormals[at + 1] * weights.y + CornerNormals[at + 2] * weights.z;
    float3 away = (p - closest) * (inside ? -1.0 : 1.0);

    if (dot(normal, normal) < 1e-12)
        normal = cross(b - a, c - a);

    if (dot(away, away) > 1e-12 && dot(normal, away) < 0.0)
        normal = -normal;

    Normals[cell] = float4(dot(normal, normal) > 1e-12 ? normalize(normal) : float3(0.0, 0.0, 0.0), 0.0);
}
