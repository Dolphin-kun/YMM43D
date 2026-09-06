using System.Diagnostics;
using Vortice.Direct3D11;

namespace YMM43D.Graphics
{
    public static class DeviceHealth
    {
        private static readonly HashSet<nint> reported = [];

        public static bool IsLost(ID3D11Device? device, string where, out string reason)
        {
            reason = string.Empty;

            if (device is null)
                return false;

            var result = device.DeviceRemovedReason;

            if (!result.Failure)
                return false;

            reason = $"{Describe(result.Code)}（HRESULT 0x{result.Code:X8}）";

            lock (reported)
            {
                if (reported.Add(device.NativePointer))
                    Trace.TraceError($"[YMM43D] {where} の GPU デバイスが失われました。{reason}");
            }

            return true;
        }

        public static void Forget(ID3D11Device? device)
        {
            if (device is null)
                return;

            lock (reported)
                reported.Remove(device.NativePointer);
        }

        private static string Describe(int code) => (uint)code switch
        {
            0x887A0006 => "GPU が固まって打ち切られました。描画が重すぎるか、シェーダーが終わらなかった可能性があります",
            0x887A0005 => "GPU が取り外されました。ドライバの更新や再起動でも起きます",
            0x887A0007 => "GPU がリセットされました。別のアプリを巻き込んだ異常の可能性があります",
            0x887A0020 => "GPU ドライバの内部エラーです",
            0x887A0004 => "この GPU では扱えない呼び出しがありました",
            _ => "GPU デバイスが失われました",
        };
    }
}
