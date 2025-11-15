using System;
using HyperTizen.SDK;

namespace HyperTizen.Capture
{
    /// <summary>
    /// Pixel sampling capture method using VideoEnhanceCapture
    /// Wraps libvideoenhance.so to sample edge pixels for ambient lighting
    /// Lowest priority - slow but works on all Tizen 8+ when SDK methods fail
    /// </summary>
    public class PixelSamplingCaptureMethod : ICaptureMethod
    {
        private bool _isInitialized = false;
        private VideoEnhanceCapture.CapturePoint[] _capturePoints;

        public string Name => "Pixel Sampling (VideoEnhance)";
        public CaptureMethodType Type => CaptureMethodType.PixelSampling;

        public bool IsAvailable()
        {
            // Check if library exists
            if (!System.IO.File.Exists("/usr/lib/libvideoenhance.so"))
            {
                Helper.Log.Write(Helper.eLogType.Debug, "PixelSampling: Not available (library not found)");
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
                // Initialize if needed
                if (!_isInitialized)
                {
                    _isInitialized = VideoEnhanceCapture.Initialize();
                    if (_isInitialized)
                    {
                        _capturePoints = VideoEnhanceCapture.GetDefaultCapturePoints();
                    }
                }

                if (!_isInitialized)
                {
                    Helper.Log.Write(Helper.eLogType.Warning, "PixelSampling Test: Initialization failed");
                    return false;
                }

                // Run test capture using built-in test method
                return VideoEnhanceCapture.TestCapture();
            }
            catch (Exception ex)
            {
                Helper.Log.Write(Helper.eLogType.Error, $"PixelSampling Test exception: {ex.Message}");
                return false;
            }
        }

        public CaptureResult Capture(int width, int height)
        {
            try
            {
                // Initialize if needed
                if (!_isInitialized)
                {
                    _isInitialized = VideoEnhanceCapture.Initialize();
                    if (_isInitialized)
                    {
                        _capturePoints = VideoEnhanceCapture.GetDefaultCapturePoints();
                    }
                    else
                    {
                        return CaptureResult.CreateFailure("VideoEnhanceCapture initialization failed");
                    }
                }

                // Capture pixel samples
                VideoEnhanceCapture.Color[] colors;
                bool success = VideoEnhanceCapture.CapturePixels(_capturePoints, out colors);

                if (!success || colors == null)
                {
                    return CaptureResult.CreateFailure("CapturePixels failed");
                }

                // Convert pixel samples to NV12 format
                int ySize = width * height;
                int uvSize = (width * height) / 2;
                byte[] yData = new byte[ySize];
                byte[] uvData = new byte[uvSize];

                ConvertPixelSamplesToNV12(colors, yData, uvData, width, height);

                return CaptureResult.CreateSuccess(yData, uvData, width, height);
            }
            catch (Exception ex)
            {
                return CaptureResult.CreateFailure($"Pixel sampling exception: {ex.Message}");
            }
        }

        public void Cleanup()
        {
            // No resources to clean up (VideoEnhanceCapture is static)
            _isInitialized = false;
            _capturePoints = null;
        }

        /// <summary>
        /// Convert 16 sampled edge pixels (RGB 10-bit) to NV12 format for ambient lighting
        /// Creates a low-resolution ambient frame perfect for HyperHDR LED strips
        /// </summary>
        private void ConvertPixelSamplesToNV12(VideoEnhanceCapture.Color[] colors,
            byte[] yData, byte[] uvData, int width, int height)
        {
            // Convert 10-bit RGB (0-1023) to 8-bit (0-255) and then to YUV
            byte[] yValues = new byte[colors.Length];
            byte[] uValues = new byte[colors.Length];
            byte[] vValues = new byte[colors.Length];

            for (int i = 0; i < colors.Length; i++)
            {
                // Convert 10-bit to 8-bit
                int r = (colors[i].R * 255) / 1023;
                int g = (colors[i].G * 255) / 1023;
                int b = (colors[i].B * 255) / 1023;

                // Clamp to valid range
                r = Math.Max(0, Math.Min(255, r));
                g = Math.Max(0, Math.Min(255, g));
                b = Math.Max(0, Math.Min(255, b));

                // RGB to YUV conversion (BT.601 standard)
                yValues[i] = (byte)(((66 * r + 129 * g + 25 * b + 128) >> 8) + 16);
                uValues[i] = (byte)(((-38 * r - 74 * g + 112 * b + 128) >> 8) + 128);
                vValues[i] = (byte)(((112 * r - 94 * g - 18 * b + 128) >> 8) + 128);
            }

            // Fill Y plane (luminance)
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    int index = y * width + x;
                    int colorIndex = GetColorIndexForPosition(x, y, width, height);
                    yData[index] = yValues[colorIndex];
                }
            }

            // Fill UV plane (chrominance) - NV12 format (interleaved U and V)
            // UV plane is half resolution (width/2 x height/2)
            for (int y = 0; y < height / 2; y++)
            {
                for (int x = 0; x < width / 2; x++)
                {
                    int index = y * width + x * 2;
                    // Map UV coordinates to Y coordinates
                    int colorIndex = GetColorIndexForPosition(x * 2, y * 2, width, height);
                    uvData[index] = uValues[colorIndex];     // U
                    uvData[index + 1] = vValues[colorIndex]; // V
                }
            }
        }

        /// <summary>
        /// Map a pixel position to one of the 16 sampled color indices
        /// Creates edge-based ambient lighting effect
        /// </summary>
        private int GetColorIndexForPosition(int x, int y, int width, int height)
        {
            // Define edge thresholds (outer 20% of frame for edges)
            int edgeThresholdX = width / 5;
            int edgeThresholdY = height / 5;

            bool isTop = y < edgeThresholdY;
            bool isBottom = y >= height - edgeThresholdY;
            bool isLeft = x < edgeThresholdX;
            bool isRight = x >= width - edgeThresholdX;

            // Top edge (4 points: indices 0-3)
            if (isTop && !isLeft && !isRight)
            {
                int segment = (x * 4) / width;
                return Math.Min(3, segment);
            }

            // Top-right corner
            if (isTop && isRight)
                return 3;

            // Right edge (3 points: indices 4-6)
            if (isRight && !isTop && !isBottom)
            {
                int segment = (y * 3) / height;
                return 4 + Math.Min(2, segment);
            }

            // Bottom-right corner
            if (isBottom && isRight)
                return 6;

            // Bottom edge (4 points: indices 7-10)
            if (isBottom && !isLeft && !isRight)
            {
                int segment = ((width - x) * 4) / width;
                return 7 + Math.Min(3, segment);
            }

            // Bottom-left corner
            if (isBottom && isLeft)
                return 10;

            // Left edge (3 points: indices 11-13)
            if (isLeft && !isTop && !isBottom)
            {
                int segment = ((height - y) * 3) / height;
                return 11 + Math.Min(2, segment);
            }

            // Top-left corner
            if (isTop && isLeft)
                return 13;

            // Center region (2 points: indices 14-15)
            if (x < width / 2)
                return 14;
            else
                return 15;
        }
    }
}
