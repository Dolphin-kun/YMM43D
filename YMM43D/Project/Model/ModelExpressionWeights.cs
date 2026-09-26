using YMM43D.Graphics.Models;

namespace YMM43D.Project.Model
{
    // 表情の強さを、形ごとの動かす量に直す。
    internal static class ModelExpressionWeights
    {
        public static float[]? Of(ModelData model, IEnumerable<(string Name, float Strength)> wanted)
        {
            float[]? weights = null;

            foreach (var (name, strength) in wanted)
            {
                if (name.Length == 0 || strength == 0f)
                    continue;

                foreach (var expression in model.Expressions)
                {
                    if (expression.Name != name)
                        continue;

                    foreach (var bind in expression.Binds)
                    {
                        if (bind.Morph < 0 || bind.Morph >= model.Morphs.Length)
                            continue;

                        weights ??= new float[model.Morphs.Length];
                        weights[bind.Morph] = Math.Clamp(weights[bind.Morph] + (strength * bind.Weight), 0f, 1f);
                    }
                }
            }

            return weights;
        }
    }
}
