using System.Diagnostics;
using SharpGen.Runtime;
using Vortice.Direct3D11;
using ResultCode = Vortice.DXGI.ResultCode;

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

            reason = $"{Describe(result)}（HRESULT 0x{result.Code:X8}）";

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

        private static string Describe(Result result)
        {
            if (result == ResultCode.DeviceHung)
                return "GPU が固まって打ち切られました。描画が重すぎるか、シェーダーが終わらなかった可能性があります";

            if (result == ResultCode.DeviceRemoved)
                return "GPU が取り外されました。ドライバの更新や再起動でも起きます";

            if (result == ResultCode.DeviceReset)
                return "GPU がリセットされました。別のアプリを巻き込んだ異常の可能性があります";

            if (result == ResultCode.DriverInternalError)
                return "GPU ドライバの内部エラーです";

            if (result == ResultCode.InvalidCall)
                return "この GPU では扱えない呼び出しがありました";

            return "GPU デバイスが失われました";
        }
    }
}
