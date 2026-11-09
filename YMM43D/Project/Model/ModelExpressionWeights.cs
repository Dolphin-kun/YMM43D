using YMM43D.Graphics.Models;

namespace YMM43D.Project.Model
{
    internal static class ModelExpressionWeights
    {
        public static float[]? Of(ModelData model, IEnumerable<(string Name, float Strength)> wanted)
        {
            float[]? weights = null;

            foreach (var (name, strength) in wanted)
            {
                if (name.Length == 0 || strength == 0f)
                    continue;

                var loose = !Holds(model, name, false);

                foreach (var expression in model.Expressions)
                {
                    if (!Matches(expression, name, loose))
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

        public static bool Has(ModelData model, string name)
            => name.Length > 0 && (Holds(model, name, false) || Holds(model, name, true));

        private static bool Holds(ModelData model, string name, bool loose)
        {
            foreach (var expression in model.Expressions)
            {
                if (Matches(expression, name, loose))
                    return true;
            }

            return false;
        }

        private static bool Matches(ModelExpression expression, string name, bool loose)
            => loose
                ? string.Equals(expression.Name, name, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(expression.Preset, name, StringComparison.OrdinalIgnoreCase)
                : expression.Name == name;
    }
}
