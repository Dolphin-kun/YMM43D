namespace YMM43D.PreviewTool
{
    public sealed class PreviewKeyText
    {
        public string this[string action] => Enum.TryParse<PreviewAction>(action, out var parsed) ? PreviewShortcuts.TextOf(parsed) : string.Empty;
    }
}
