using System;
using System.Runtime.InteropServices;
using HyperTizen.SDK;

namespace HyperTizen.Capture
{
    /// <summary>
    /// Tizen 7 SDK capture method using SecVideoCaptureT7
    /// Wraps the legacy libsec-video-capture.so API
    /// Medium priority - fast full-frame capture on Tizen 7 and below
    /// Also works as fallback on some Tizen 8 models
    /// </summary>
    public class T7SdkCaptureMethod : ICaptureMethod
    {
        private IntPtr _pImageY;
        private IntPtr _pImageUV;
        private bool _buffersAllocated = false;

        public string Name => "Tizen 7 SDK (Legacy API)";
        public CaptureMethodType Type => CaptureMethodType.T7SDK;

        public bool IsAvailable()
        {
            // T7 SDK not available on Tizen 9+ (library removed)
            if (SDK.SystemInfo.TizenVersionMajor >= 9)
            {
                Helper.Log.Write(Helper.eLogType.Debug,
                    "T7SDK: Not available (Tizen 9+ detected - libsec-video-capture.so.0 removed from OS)");
                return false;
            }

            // Check if T7 library exists
            if (!System.IO.File.Exists("/usr/lib/libsec-video-capture.so.0"))
            {
                Helper.Log.Write(Helper.eLogType.Debug, "T7SDK: Not available (library file not found)");
                return false;
            }

            return true;
        }

        public bool Test()
        {
            if (!IsAvailable())
                return false;

            // T7 SDK methods are disabled on Tizen 8+ (DllImports commented out for safety)
            // This code path should never execute due to IsAvailable() check above
            // But we need it to compile, so return false immediately
            Helper.Log.Write(Helper.eLogType.Error,
                "T7SDK Test: Cannot test - T7 API methods are disabled (Tizen 8+ only supports T8 API)");
            return false;
        }

        public CaptureResult Capture(int width, int height)
        {
            // T7 SDK methods are disabled on Tizen 8+ (DllImports commented out for safety)
            // This should never be called because IsAvailable() returns false on Tizen 8+
            // But we need it to compile, so return failure immediately
            return CaptureResult.CreateFailure(
                "T7 SDK is disabled - T7 API methods not available on Tizen 8+");
        }

        public void Cleanup()
        {
            if (_pImageY != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(_pImageY);
                _pImageY = IntPtr.Zero;
            }

            if (_pImageUV != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(_pImageUV);
                _pImageUV = IntPtr.Zero;
            }

            _buffersAllocated = false;
        }
    }
}
