using System;
using System.Runtime.InteropServices;
using HyperTizen.SDK;

namespace HyperTizen.Capture
{
    /// <summary>
    /// Tizen 8 SDK capture method using SecVideoCaptureT8
    /// Wraps the new VTable API with Lock/getVideoMainYUV/Unlock
    /// Highest priority - fast full-frame capture on Tizen 8+
    /// </summary>
    public class T8SdkCaptureMethod : ICaptureMethod
    {
        private bool _isInitialized = false;
        private IntPtr _pImageY;
        private IntPtr _pImageUV;

        public string Name => "Tizen 8 SDK (VTable API)";
        public CaptureMethodType Type => CaptureMethodType.T8SDK;

        public bool IsAvailable()
        {
            // Check if Tizen 8+ and library exists
            if (SystemInfo.TizenVersionMajor < 8)
            {
                Helper.Log.Write(Helper.eLogType.Debug, "T8SDK: Not available (Tizen < 8)");
                return false;
            }

            if (!System.IO.File.Exists("/usr/lib/libvideo-capture.so.0.1.0"))
            {
                Helper.Log.Write(Helper.eLogType.Debug, "T8SDK: Not available (library not found)");
                return false;
            }

            return true;
        }

        public bool Test()
        {
            if (!IsAvailable())
                return false;

            try
            {
                // Initialize if not already done
                if (!_isInitialized)
                {
                    SecVideoCaptureT8.Init();
                    _isInitialized = true;
                }

                // Run test capture
                return SecVideoCaptureT8.TestCapture();
            }
            catch (Exception ex)
            {
                Helper.Log.Write(Helper.eLogType.Error, $"T8SDK Test failed: {ex.Message}");
                return false;
            }
        }

        public CaptureResult Capture(int width, int height)
        {
            try
            {
                // Initialize on first capture
                if (!_isInitialized)
                {
                    SecVideoCaptureT8.Init();
                    _isInitialized = true;

                    // Allocate buffers
                    int ySize = width * height;
                    int uvSize = (width * height) / 2;
                    _pImageY = Marshal.AllocHGlobal(ySize);
                    _pImageUV = Marshal.AllocHGlobal(uvSize);
                }

                // Prepare Info_t structure
                int ySize2 = width * height;
                int uvSize2 = (width * height) / 2;

                SecVideoCapture.Info_t info = new SecVideoCapture.Info_t
                {
                    iGivenBufferSize1 = ySize2,
                    iGivenBufferSize2 = uvSize2,
                    pImageY = _pImageY,
                    pImageUV = _pImageUV
                };

                // Call T8 SDK capture
                int result = SecVideoCaptureT8.CaptureScreen(width, height, ref info);

                // Check result
                if (result < 0)
                {
                    return CaptureResult.CreateFailure($"T8 SDK returned error code: {result}");
                }

                // Copy data from unmanaged buffers
                byte[] yData = new byte[ySize2];
                byte[] uvData = new byte[uvSize2];
                Marshal.Copy(info.pImageY, yData, 0, ySize2);
                Marshal.Copy(info.pImageUV, uvData, 0, uvSize2);

                return CaptureResult.CreateSuccess(yData, uvData, info.iWidth, info.iHeight);
            }
            catch (Exception ex)
            {
                return CaptureResult.CreateFailure($"T8 SDK exception: {ex.Message}");
            }
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

            _isInitialized = false;
        }
    }
}
