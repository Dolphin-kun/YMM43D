namespace YMM43D.Commons
{
    public readonly record struct SceneMirror(string Image, float Ambient)
    {
        public static SceneMirror None => new(string.Empty, 0f);

        public string Path => Image ?? string.Empty;

        public bool HasImage => !string.IsNullOrEmpty(Image);
    }
}
