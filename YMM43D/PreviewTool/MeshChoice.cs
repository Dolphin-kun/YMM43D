namespace YMM43D.PreviewTool
{
    public static class MeshChoice
    {
        public static bool Drops(IReadOnlySet<int> selection, IReadOnlyCollection<int> wanted)
            => wanted.Count > 0 && wanted.All(selection.Contains);

        public static void Apply(HashSet<int> selection, IReadOnlyCollection<int> wanted, bool extend)
        {
            if (!extend)
            {
                selection.Clear();
            }
            else if (Drops(selection, wanted))
            {
                foreach (var one in wanted)
                    selection.Remove(one);

                return;
            }

            foreach (var one in wanted)
                selection.Add(one);
        }
    }
}
