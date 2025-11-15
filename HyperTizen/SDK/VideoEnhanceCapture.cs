using System;
using System.Runtime.InteropServices;
using System.Threading;

namespace HyperTizen.SDK
{
    /// <summary>
    /// Video Enhance pixel sampling capture (works on Tizen 8.0!)
    /// Uses libvideoenhance.so to sample individual RGB pixels
    /// This is slower than full-frame capture but NOT blocked by Samsung firmware
    /// </summary>
    public static unsafe class VideoEnhanceCapture
    {
        private static Condition _condition;
        private static bool _isInitialized = false;

        // Color struct for RGB values (10-bit color space, 0-1023)
        [StructLayout(LayoutKind.Sequential)]
        public struct Color
        {
            public int R;
            public int G;
            public int B;
        }

        // Condition struct returned by API
        [StructLayout(LayoutKind.Sequential)]
        public struct Condition
        {
            public int ScreenCapturePoints;  // Max pixels per batch
            public int PixelDensityX;        // Pixel sampling density X
            public int PixelDensityY;        // Pixel sampling density Y
            public int SleepMS;              // Required sleep between batches (THIS IS THE BOTTLENECK!)
            public int Width;                // Screen width
            public int Height;               // Screen height
        }

        // Capture point (normalized 0.0-1.0 coordinates)
        public struct CapturePoint
        {
            public double X;
            public double Y;

            public CapturePoint(double x, double y)
            {
                X = x;
                Y = y;
            }
        }

        // Tizen 7+ API functions
        [DllImport("/usr/lib/libvideoenhance.so", CallingConvention = CallingConvention.Cdecl,
                   EntryPoint = "ve_get_rgb_measure_condition")]
        private static extern int MeasureCondition(out Condition condition);

        [DllImport("/usr/lib/libvideoenhance.so", CallingConvention = CallingConvention.Cdecl,
                   EntryPoint = "ve_set_rgb_measure_position")]
        private static extern int MeasurePosition(int index, int x, int y);

        [DllImport("/usr/lib/libvideoenhance.so", CallingConvention = CallingConvention.Cdecl,
                   EntryPoint = "ve_get_rgb_measure_pixel")]
        private static extern int MeasurePixel(int index, out Color color);

        /// <summary>
        /// Initialize Video Enhance capture - get capabilities from API
        /// </summary>
        public static bool Initialize()
        {
            // Skip if already initialized
            if (_isInitialized)
            {
                Helper.Log.Write(Helper.eLogType.Debug, "VideoEnhance: Already initialized, skipping re-initialization");
                return true;
            }

            try
            {
                Helper.Log.Write(Helper.eLogType.Info, "VideoEnhance: Initializing pixel sampling capture...");

                int result = MeasureCondition(out _condition);

                if (result < 0)
                {
                    Helper.Log.Write(Helper.eLogType.Error, $"VideoEnhance: MeasureCondition failed with code {result}");
                    return false;
                }

                Helper.Log.Write(Helper.eLogType.Info, $"VideoEnhance: Initialized successfully!");
                Helper.Log.Write(Helper.eLogType.Info, $"  Screen: {_condition.Width}x{_condition.Height}");
                Helper.Log.Write(Helper.eLogType.Info, $"  Max points per batch: {_condition.ScreenCapturePoints}");
                Helper.Log.Write(Helper.eLogType.Info, $"  Pixel density: {_condition.PixelDensityX}x{_condition.PixelDensityY}");
                Helper.Log.Write(Helper.eLogType.Info, $"  Required sleep: {_condition.SleepMS}ms ⚠️ (THIS LIMITS FRAME RATE!)");

                _isInitialized = true;
                return true;
            }
            catch (Exception ex)
            {
                Helper.Log.Write(Helper.eLogType.Error, $"VideoEnhance: Initialize exception: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Capture RGB values at specified pixel locations
        /// OPTIMIZATION: Reduces sleep time to improve frame rate
        /// </summary>
        public static bool CapturePixels(CapturePoint[] points, out Color[] colors)
        {
            colors = new Color[points.Length];

            if (!_isInitialized)
            {
                Helper.Log.Write(Helper.eLogType.Error, "VideoEnhance: Not initialized");
                return false;
            }

            if (_condition.ScreenCapturePoints == 0)
            {
                Helper.Log.Write(Helper.eLogType.Error, "VideoEnhance: ScreenCapturePoints is 0");
                return false;
            }

            try
            {
                int i = 0;
                while (i < points.Length)
                {
                    // Set positions for this batch (up to ScreenCapturePoints at once)
                    int batchSize = Math.Min(_condition.ScreenCapturePoints, points.Length - i);

                    Helper.Log.Write(Helper.eLogType.Info,
                        $"VideoEnhance: Processing batch {(i / batchSize) + 1} of {(points.Length + batchSize - 1) / batchSize} ({batchSize} points)");

                    for (int j = 0; j < batchSize; j++)
                    {
                        int pointIndex = i + j;

                        // Convert normalized coordinates to pixel coordinates
                        int x = (int)(points[pointIndex].X * _condition.Width) - _condition.PixelDensityX / 2;
                        int y = (int)(points[pointIndex].Y * _condition.Height) - _condition.PixelDensityY / 2;

                        // Clamp to screen bounds
                        x = Math.Max(0, Math.Min(x, _condition.Width - _condition.PixelDensityX - 1));
                        y = Math.Max(0, Math.Min(y, _condition.Height - _condition.PixelDensityY - 1));

                        int result = MeasurePosition(j, x, y);

                        if (result < 0)
                        {
                            Helper.Log.Write(Helper.eLogType.Warning, $"VideoEnhance: MeasurePosition({j}, {x}, {y}) failed: {result}");
                            return false;
                        }
                    }

                    // Use API-specified sleep time (don't optimize - hardware requires this timing)
                    int sleepTime = _condition.SleepMS;

                    if (sleepTime > 0)
                    {
                        Helper.Log.Write(Helper.eLogType.Info, $"VideoEnhance: Sleeping {sleepTime}ms as required by API...");
                        Thread.Sleep(sleepTime);
                        Helper.Log.Write(Helper.eLogType.Info, $"VideoEnhance: Sleep complete, reading {batchSize} pixel colors...");
                    }
                    else
                    {
                        Helper.Log.Write(Helper.eLogType.Info, $"VideoEnhance: Reading {batchSize} pixel colors (no sleep required)...");
                    }

                    // Read pixel colors for this batch
                    for (int k = 0; k < batchSize; k++)
                    {
                        Color color;
                        int result = MeasurePixel(k, out color);

                        Helper.Log.Write(Helper.eLogType.Debug,
                            $"VideoEnhance: Pixel {k + 1}/{batchSize} returned code {result}");

                        if (result < 0)
                        {
                            Helper.Log.Write(Helper.eLogType.Error,
                                $"VideoEnhance: MeasurePixel({k}) failed with code {result}");
                            return false;
                        }

                        // Validate color (should be 0-1023 in 10-bit color space)
                        if (color.R > 1023 || color.G > 1023 || color.B > 1023 ||
                            color.R < 0 || color.G < 0 || color.B < 0)
                        {
                            Helper.Log.Write(Helper.eLogType.Error,
                                $"VideoEnhance: Invalid color RGB({color.R}, {color.G}, {color.B})");
                            return false;
                        }

                        colors[i + k] = color;
                    }

                    Helper.Log.Write(Helper.eLogType.Info,
                        $"VideoEnhance: ✓ Batch complete - read {batchSize} pixel(s) successfully");

                    i += batchSize;
                }

                return true;
            }
            catch (Exception ex)
            {
                Helper.Log.Write(Helper.eLogType.Error, $"VideoEnhance: CapturePixels exception: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Get default capture points (16 edge/corner samples for ambient lighting)
        /// These are the same points used in the working HyperTizen implementation
        /// </summary>
        public static CapturePoint[] GetDefaultCapturePoints()
        {
            return new CapturePoint[] {
                // Top edge (4 points)
                new CapturePoint(0.21, 0.05),
                new CapturePoint(0.45, 0.05),
                new CapturePoint(0.7, 0.05),
                new CapturePoint(0.93, 0.07),

                // Right edge (3 points)
                new CapturePoint(0.95, 0.275),
                new CapturePoint(0.95, 0.5),
                new CapturePoint(0.95, 0.8),

                // Bottom edge (4 points)
                new CapturePoint(0.79, 0.95),
                new CapturePoint(0.65, 0.95),
                new CapturePoint(0.35, 0.95),
                new CapturePoint(0.15, 0.95),

                // Left edge (3 points)
                new CapturePoint(0.05, 0.725),
                new CapturePoint(0.05, 0.4),
                new CapturePoint(0.05, 0.2),

                // Center (2 points)
                new CapturePoint(0.35, 0.5),
                new CapturePoint(0.65, 0.5)
            };
        }

        /// <summary>
        /// Test VideoEnhance capture API
        /// </summary>
        public static bool TestCapture()
        {
            Helper.Log.Write(Helper.eLogType.Info, "=== Testing VideoEnhance Pixel Sampling ===");

            if (!Initialize())
            {
                Helper.Log.Write(Helper.eLogType.Error, "VideoEnhance: Initialization failed");
                return false;
            }

            // Use minimal test points (4 corners) for faster testing
            var points = new CapturePoint[] {
                new CapturePoint(0.1, 0.1),   // Top-left
                new CapturePoint(0.9, 0.1),   // Top-right
                new CapturePoint(0.9, 0.9),   // Bottom-right
                new CapturePoint(0.1, 0.9)    // Bottom-left
            };
            Color[] colors;

            Helper.Log.Write(Helper.eLogType.Info, $"Testing with {points.Length} corner sample points (minimal test)...");

            var startTime = DateTime.Now;
            bool success = CapturePixels(points, out colors);
            var elapsed = (DateTime.Now - startTime).TotalMilliseconds;

            if (success)
            {
                Helper.Log.Write(Helper.eLogType.Info, $"✅ VideoEnhance capture test PASSED!");
                Helper.Log.Write(Helper.eLogType.Info, $"Captured {points.Length} test points in {elapsed:F1}ms");
                Helper.Log.Write(Helper.eLogType.Info, $"Estimated frame rate: ~{1000.0 / elapsed:F1} FPS (for {points.Length} points)");

                Helper.Log.Write(Helper.eLogType.Info, "Sample colors (RGB 10-bit):");
                for (int i = 0; i < colors.Length; i++)
                {
                    Helper.Log.Write(Helper.eLogType.Info, $"  Corner {i + 1}: RGB({colors[i].R}, {colors[i].G}, {colors[i].B})");
                }

                Helper.Log.Write(Helper.eLogType.Info, "✓ VideoEnhance pixel sampling is WORKING on this Tizen 8+ device");
                return true;
            }
            else
            {
                Helper.Log.Write(Helper.eLogType.Error, "VideoEnhance: Test capture failed");
                return false;
            }
        }

        public static Condition GetCondition()
        {
            return _condition;
        }

        public static bool IsInitialized
        {
            get { return _isInitialized; }
        }
    }
}
