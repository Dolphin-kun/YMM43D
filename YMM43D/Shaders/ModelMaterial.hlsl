#include "Model.hlsli"

MODEL_PS VSMain(MODEL_IN input)
{
    return MakeModelVertex(input.Pos, input.Col, input.Tex, input.Nrm, input.Tan);
}

float4 PSMain(MODEL_PS input) : SV_TARGET
{
    return ShadeModel(input);
}
