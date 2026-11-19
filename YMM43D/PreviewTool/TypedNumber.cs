using System.Globalization;
using System.Windows.Input;

namespace YMM43D.PreviewTool
{
    internal sealed class TypedNumber
    {
        private string digits = string.Empty;
        private bool negative;

        public bool HasValue => digits.Length > 0;

        public float Value => HasValue && float.TryParse(digits, NumberStyles.Float, CultureInfo.InvariantCulture, out var read) ? (negative ? -read : read) : 0f;

        public string Text => HasValue ? (negative ? "-" : string.Empty) + digits : string.Empty;

        public void Clear()
        {
            digits = string.Empty;
            negative = false;
        }

        public bool Take(Key key)
        {
            switch (key)
            {
                case >= Key.D0 and <= Key.D9:
                    digits += (char)('0' + (key - Key.D0));
                    return true;

                case >= Key.NumPad0 and <= Key.NumPad9:
                    digits += (char)('0' + (key - Key.NumPad0));
                    return true;

                case Key.OemPeriod or Key.Decimal:
                    if (!digits.Contains('.'))
                        digits += digits.Length == 0 ? "0." : ".";

                    return true;

                case Key.OemMinus or Key.Subtract:
                    negative = !negative;
                    return true;

                case Key.Back:
                    if (digits.Length > 0)
                        digits = digits[..^1];
                    else
                        negative = false;

                    return true;

                default:
                    return false;
            }
        }
    }
}
