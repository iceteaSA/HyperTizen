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
            // Check if library exists
            if (!System.IO.File.Exists("/usr/lib/libsec-video-capture.so.0"))
            {
                Helper.Log.Write(Helper.eLogType.Debug, "T7SDK: Not available (library not found)");
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
                // Try a test capture at low resolution
                int testWidth = 480;
                int testHeight = 270;
                int ySize = testWidth * testHeight;
                int uvSize = (testWidth * testHeight) / 2;

                IntPtr testY = Marshal.AllocHGlobal(ySize);
                IntPtr testUV = Marshal.AllocHGlobal(uvSize);

                SecVideoCapture.Info_t info = new SecVideoCapture.Info_t
                {
                    iGivenBufferSize1 = ySize,
                    iGivenBufferSize2 = uvSize,
                    pImageY = testY,
                    pImageUV = testUV
                };

                int result = SecVideoCaptureT7.CaptureScreenVideo(testWidth, testHeight, ref info);

                Marshal.FreeHGlobal(testY);
                Marshal.FreeHGlobal(testUV);

                if (result >= 0)
                {
                    Helper.Log.Write(Helper.eLogType.Info, $"T7SDK Test: SUCCESS (result={result})");
                    return true;
                }
                else
                {
                    Helper.Log.Write(Helper.eLogType.Warning, $"T7SDK Test: FAILED (result={result})");
                    return false;
                }
            }
            catch (Exception ex)
            {
                Helper.Log.Write(Helper.eLogType.Error, $"T7SDK Test exception: {ex.Message}");
                return false;
            }
        }

        public CaptureResult Capture(int width, int height)
        {
            try
            {
                // Allocate buffers on first capture
                if (!_buffersAllocated)
                {
                    int ySize = width * height;
                    int uvSize = (width * height) / 2;
                    _pImageY = Marshal.AllocHGlobal(ySize);
                    _pImageUV = Marshal.AllocHGlobal(uvSize);
                    _buffersAllocated = true;
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

                // Call T7 SDK capture (video only, no UI overlay)
                int result = SecVideoCaptureT7.CaptureScreenVideo(width, height, ref info);

                // Check result
                if (result < 0)
                {
                    return CaptureResult.CreateFailure($"T7 SDK returned error code: {result}");
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
                return CaptureResult.CreateFailure($"T7 SDK exception: {ex.Message}");
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

            _buffersAllocated = false;
        }
    }
}
