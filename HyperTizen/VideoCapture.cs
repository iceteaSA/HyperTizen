using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using HyperTizen.SDK;
using Tizen.Applications;
using Tizen.Applications.Notifications;


namespace HyperTizen
{
    public class ImageData
    {
        public byte[] yData { get; set; }
        public byte[] uvData { get; set; }
    }
    

    public static class VideoCapture
    {
        private static IntPtr pImageY;
        private static IntPtr pImageUV;
        private static byte[] managedArrayY;
        private static byte[] managedArrayUV;

        // Capture method tracking
        private enum CaptureMethod
        {
            Unknown,
            T8SDK,
            T7SDK,
            PixelSampling
        }

        private static CaptureMethod _currentMethod = CaptureMethod.Unknown;
        private static bool _t8SdkAvailable = false;
        private static bool _pixelSamplingAvailable = false;
        private static SDK.VideoEnhanceCapture.CapturePoint[] _capturePoints;
        public static void InitCapture()
        {
            Helper.Log.Write(Helper.eLogType.Info, $"InitCapture: Tizen {SystemInfo.TizenVersionMajor}.{SystemInfo.TizenVersionMinor} detected");
            
            try
            {
                Helper.Log.Write(Helper.eLogType.Info, "InitCapture: Prelinking SecVideoCapture library...");
                Marshal.PrelinkAll(typeof(SDK.SecVideoCapture));
                Helper.Log.Write(Helper.eLogType.Info, "InitCapture: Prelink OK");
            }
            catch (Exception ex)
            {
                Helper.Log.Write(Helper.eLogType.Error, 
                    $"InitCapture: Prelink FAILED - {ex.GetType().Name}: {ex.Message}");
                throw; // Re-throw to be caught by caller
            }

            // Initialize SecVideoCapture for Tizen 8+ (only once!)
            if (SystemInfo.TizenVersionMajor >= 8)
            {
                Helper.Log.Write(Helper.eLogType.Info, "InitCapture: Tizen 8+ detected, trying SecVideoCaptureT8...");
                try
                {
                    SDK.SecVideoCaptureT8.Init();
                    Helper.Log.Write(Helper.eLogType.Info, "InitCapture: SecVideoCaptureT8 OK");
                    _t8SdkAvailable = true;
                }
                catch (Exception ex)
                {
                    Helper.Log.Write(Helper.eLogType.Warning,
                        $"InitCapture: T8 SDK failed ({ex.Message}), trying T7 API as fallback...");
                    // Let it fall through to use T7 API even on Tizen 8
                    Helper.Log.Write(Helper.eLogType.Info, "InitCapture: Using T7 API on Tizen 8 (compatibility mode)");
                    _t8SdkAvailable = false;
                }
            }
            else
            {
                Helper.Log.Write(Helper.eLogType.Info, $"InitCapture: Tizen {SystemInfo.TizenVersionMajor} detected, using SecVideoCaptureT7");
                _t8SdkAvailable = false;
            }

            // Initialize VideoEnhanceCapture (pixel sampling) as last resort fallback
            Helper.Log.Write(Helper.eLogType.Info, "InitCapture: Initializing VideoEnhanceCapture (pixel sampling fallback)...");
            try
            {
                _pixelSamplingAvailable = SDK.VideoEnhanceCapture.Initialize();
                if (_pixelSamplingAvailable)
                {
                    _capturePoints = SDK.VideoEnhanceCapture.GetDefaultCapturePoints();
                    Helper.Log.Write(Helper.eLogType.Info,
                        $"InitCapture: VideoEnhanceCapture OK - {_capturePoints.Length} sample points ready");
                }
                else
                {
                    Helper.Log.Write(Helper.eLogType.Warning, "InitCapture: VideoEnhanceCapture initialization failed");
                }
            }
            catch (Exception ex)
            {
                Helper.Log.Write(Helper.eLogType.Warning,
                    $"InitCapture: VideoEnhanceCapture exception: {ex.Message}");
                _pixelSamplingAvailable = false;
            }

            int NV12ySize = Globals.Instance.Width * Globals.Instance.Height;
            int NV12uvSize = (Globals.Instance.Width * Globals.Instance.Height) / 2; // UV-Plane is half as big as Y-Plane in NV12
            
            Helper.Log.Write(Helper.eLogType.Info, 
                $"InitCapture: Allocating buffers ({NV12ySize} + {NV12uvSize} = {NV12ySize + NV12uvSize} bytes)...");
            
            pImageY = Marshal.AllocHGlobal(NV12ySize);
            pImageUV = Marshal.AllocHGlobal(NV12uvSize);
            managedArrayY = new byte[NV12ySize];
            managedArrayUV = new byte[NV12uvSize];
            
            Helper.Log.Write(Helper.eLogType.Info, "InitCapture: Buffer allocation OK");

            int TizenVersionMajor = SystemInfo.TizenVersionMajor;
            int TizenVersionMinor = SystemInfo.TizenVersionMinor;
            bool ImageCapture = SystemInfo.ImageCapture;
            bool VideoRecording = SystemInfo.VideoRecording;
            int ScreenWidth = SystemInfo.ScreenWidth;
            int ScreenHeight = SystemInfo.ScreenHeight;
            string ModelName = SystemInfo.ModelName;
            
            Helper.Log.Write(Helper.eLogType.Info, 
                $"InitCapture: System Info - Model:{ModelName} Screen:{ScreenWidth}x{ScreenHeight} ImgCap:{ImageCapture} VidRec:{VideoRecording}");

        }

        static bool isRunning = true;
        unsafe public static void DoCapture()
        {
            //These lines need to stay here somehow - they arent used but when i delete them the service breaks ??? weird tizen stuff...
            var width = 480;
            var height = 270;
            var uvBufferSizeYUV420 = (width / 2) * (height / 2);

            int NV12ySize = Globals.Instance.Width * Globals.Instance.Height;
            int NV12uvSize = (Globals.Instance.Width * Globals.Instance.Height) / 2; // UV-Plane is half as big as Y-Plane in NV12

            // ===== PRIORITY 1: Try T8/T7 SDK (full frame capture) =====
            SDK.SecVideoCapture.Info_t info = new SDK.SecVideoCapture.Info_t();
            info.iGivenBufferSize1 = NV12ySize;
            info.iGivenBufferSize2 = NV12uvSize;
            info.pImageY = pImageY;
            info.pImageUV = pImageUV;

            var watchFPS = System.Diagnostics.Stopwatch.StartNew();
            int result = SDK.SecVideoCapture.CaptureScreen(Globals.Instance.Width, Globals.Instance.Height, ref info);
            watchFPS.Stop();

            // If T8/T7 capture succeeded, use it!
            if (result >= 0)
            {
                if (_currentMethod != CaptureMethod.T8SDK && _currentMethod != CaptureMethod.T7SDK)
                {
                    _currentMethod = _t8SdkAvailable ? CaptureMethod.T8SDK : CaptureMethod.T7SDK;
                    Helper.Log.Write(Helper.eLogType.Info,
                        $"DoCapture: Using {_currentMethod} (full frame capture)");
                }

                var elapsedFPS = 1 / watchFPS.Elapsed.TotalSeconds;
                Helper.Log.Write(Helper.eLogType.Performance, "SDK.SecVideoCapture.CaptureScreen FPS: " + elapsedFPS);
                Helper.Log.Write(Helper.eLogType.Performance, "SDK.SecVideoCapture.CaptureScreen elapsed ms: " + watchFPS.ElapsedMilliseconds);

                isRunning = true;

                Marshal.Copy(info.pImageY, managedArrayY, 0, NV12ySize);
                Marshal.Copy(info.pImageUV, managedArrayUV, 0, NV12uvSize);

                bool hasAllZeroes1 = managedArrayY.All(singleByte => singleByte == 0);
                bool hasAllZeroes2 = managedArrayUV.All(singleByte => singleByte == 0);
                if (hasAllZeroes1 && hasAllZeroes2)
                    throw new Exception("Sanity check Error");

                Helper.Log.Write(Helper.eLogType.Info, "DoCapture: NV12ySize: " + managedArrayY.Length);

                // ENHANCED NULL SAFETY: Wrap network call in try-catch
                try
                {
                    _ = Networking.SendImageAsync(managedArrayY, managedArrayUV, Globals.Instance.Width, Globals.Instance.Height);
                }
                catch (NullReferenceException ex)
                {
                    Helper.Log.Write(Helper.eLogType.Error, $"DoCapture: NullRef in SendImageAsync: {ex.Message}\nStack: {ex.StackTrace}");
                    throw; // Re-throw to be caught by capture loop
                }
                catch (Exception ex)
                {
                    Helper.Log.Write(Helper.eLogType.Error, $"DoCapture: Error in SendImageAsync: {ex.GetType().Name}: {ex.Message}");
                    throw; // Re-throw to be caught by capture loop
                }
                return;
            }

            // ===== T8/T7 FAILED - Log the error =====
            if (isRunning)
            {
                switch (result)
                {
                    case -99:
                        Helper.Log.Write(Helper.eLogType.Warning, "SDK.SecVideoCapture.CaptureScreen Result: -99 [SDK not initialized]. Falling back to pixel sampling...");
                        break;
                    case -95:
                        Helper.Log.Write(Helper.eLogType.Warning, "SDK.SecVideoCapture.CaptureScreen Result: -95 [Firmware blocked]. Falling back to pixel sampling...");
                        break;
                    case -4:
                        Helper.Log.Write(Helper.eLogType.Warning, "SDK.SecVideoCapture.CaptureScreen Result: -4 [DRM protected content]. Falling back to pixel sampling...");
                        break;
                    case -1:
                        Helper.Log.Write(Helper.eLogType.Warning, "SDK.SecVideoCapture.CaptureScreen Result: -1 [Input param wrong]. Falling back to pixel sampling...");
                        break;
                    case -2:
                        Helper.Log.Write(Helper.eLogType.Warning, "SDK.SecVideoCapture.CaptureScreen Result: -2 [Scaler capture failed]. Falling back to pixel sampling...");
                        break;
                    default:
                        Helper.Log.Write(Helper.eLogType.Warning, $"SDK.SecVideoCapture.CaptureScreen Result: {result} [Unknown error]. Falling back to pixel sampling...");
                        break;
                }
            }

            // ===== PRIORITY 2: Try Pixel Sampling (last resort) =====
            if (_pixelSamplingAvailable)
            {
                if (_currentMethod != CaptureMethod.PixelSampling)
                {
                    Helper.Log.Write(Helper.eLogType.Info,
                        "DoCapture: Switching to VideoEnhanceCapture (pixel sampling - LAST RESORT)");
                    _currentMethod = CaptureMethod.PixelSampling;
                }

                Helper.Log.Write(Helper.eLogType.Debug,
                    $"DoCapture: Calling CapturePixels with {_capturePoints.Length} points...");

                var watchPixel = System.Diagnostics.Stopwatch.StartNew();
                SDK.VideoEnhanceCapture.Color[] colors = null;
                bool pixelSuccess = false;
                bool timedOut = false;

                // Run CapturePixels on a separate task with timeout protection
                var captureTask = Task.Run(() =>
                {
                    try
                    {
                        SDK.VideoEnhanceCapture.Color[] tempColors;
                        bool captureResult = SDK.VideoEnhanceCapture.CapturePixels(_capturePoints, out tempColors);
                        colors = tempColors;
                        return captureResult;
                    }
                    catch (Exception ex)
                    {
                        Helper.Log.Write(Helper.eLogType.Error,
                            $"DoCapture: CapturePixels exception: {ex.Message}");
                        return false;
                    }
                });

                // Wait up to 5 seconds for capture to complete
                if (captureTask.Wait(5000))
                {
                    pixelSuccess = captureTask.Result;
                    watchPixel.Stop();
                    Helper.Log.Write(Helper.eLogType.Debug,
                        $"DoCapture: CapturePixels returned {pixelSuccess} in {watchPixel.ElapsedMilliseconds}ms");
                }
                else
                {
                    watchPixel.Stop();
                    timedOut = true;
                    Helper.Log.Write(Helper.eLogType.Error,
                        $"DoCapture: CapturePixels TIMED OUT after {watchPixel.ElapsedMilliseconds}ms!");
                }

                if (pixelSuccess && !timedOut && colors != null)
                {
                    Helper.Log.Write(Helper.eLogType.Debug,
                        $"DoCapture (pixel): SUCCESS - Captured {colors.Length} pixels in {watchPixel.ElapsedMilliseconds}ms");

                    // Convert pixel samples to NV12 format
                    Helper.Log.Write(Helper.eLogType.Debug,
                        $"DoCapture (pixel): Converting {colors.Length} RGB samples to NV12 ({Globals.Instance.Width}x{Globals.Instance.Height})...");

                    ConvertPixelSamplesToNV12(colors, managedArrayY, managedArrayUV,
                        Globals.Instance.Width, Globals.Instance.Height);

                    Helper.Log.Write(Helper.eLogType.Debug,
                        $"DoCapture (pixel): NV12 conversion complete (Y={managedArrayY.Length} bytes, UV={managedArrayUV.Length} bytes)");

                    isRunning = true;

                    // ENHANCED NULL SAFETY: Wrap network call in try-catch
                    Helper.Log.Write(Helper.eLogType.Debug,
                        $"DoCapture (pixel): Calling SendImageAsync...");

                    try
                    {
                        _ = Networking.SendImageAsync(managedArrayY, managedArrayUV, Globals.Instance.Width, Globals.Instance.Height);
                        Helper.Log.Write(Helper.eLogType.Debug,
                            $"DoCapture (pixel): SendImageAsync called (fire-and-forget)");
                    }
                    catch (NullReferenceException ex)
                    {
                        Helper.Log.Write(Helper.eLogType.Error, $"DoCapture (pixel): NullRef in SendImageAsync: {ex.Message}\nStack: {ex.StackTrace}");
                        throw; // Re-throw to be caught by capture loop
                    }
                    catch (Exception ex)
                    {
                        Helper.Log.Write(Helper.eLogType.Error, $"DoCapture (pixel): Error in SendImageAsync: {ex.GetType().Name}: {ex.Message}");
                        throw; // Re-throw to be caught by capture loop
                    }
                    return;
                }
                else
                {
                    if (timedOut)
                    {
                        Helper.Log.Write(Helper.eLogType.Error,
                            "DoCapture: VideoEnhanceCapture timed out - native library may be blocking!");
                    }
                    else
                    {
                        Helper.Log.Write(Helper.eLogType.Error,
                            $"DoCapture: VideoEnhanceCapture failed! (success={pixelSuccess}, colors={(colors == null ? "null" : "valid")})");
                    }
                }
            }

            // ===== ALL CAPTURE METHODS FAILED =====
            Helper.Log.Write(Helper.eLogType.Error,
                "DoCapture: ALL capture methods failed! (T8/T7 SDK + Pixel Sampling)");
            isRunning = false;
            return;
        }

        /// <summary>
        /// Convert 16 sampled edge pixels (RGB 10-bit) to NV12 format for ambient lighting
        /// Creates a low-resolution ambient frame perfect for HyperHDR LED strips
        /// </summary>
        private static void ConvertPixelSamplesToNV12(SDK.VideoEnhanceCapture.Color[] colors,
            byte[] yData, byte[] uvData, int width, int height)
        {
            // The 16 capture points are arranged as:
            // Top edge: 4 points (indices 0-3)
            // Right edge: 3 points (indices 4-6)
            // Bottom edge: 4 points (indices 7-10)
            // Left edge: 3 points (indices 11-13)
            // Center: 2 points (indices 14-15)

            // For ambient lighting, we'll create a simple grid mapping
            // Divide the frame into regions and fill each with sampled color

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

            // Create ambient lighting frame by mapping samples to regions
            // Simple approach: divide frame into edge zones and center

            int regionHeight = height / 4; // Divide into 4 horizontal zones
            int regionWidth = width / 4;   // Divide into 4 vertical zones

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
        private static int GetColorIndexForPosition(int x, int y, int width, int height)
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
                // Divide top edge into 4 segments
                int segment = (x * 4) / width;
                return Math.Min(3, segment); // Indices 0-3
            }

            // Top-right corner
            if (isTop && isRight)
                return 3; // Index 3

            // Right edge (3 points: indices 4-6)
            if (isRight && !isTop && !isBottom)
            {
                // Divide right edge into 3 segments
                int segment = (y * 3) / height;
                return 4 + Math.Min(2, segment); // Indices 4-6
            }

            // Bottom-right corner
            if (isBottom && isRight)
                return 6; // Index 6

            // Bottom edge (4 points: indices 7-10)
            if (isBottom && !isLeft && !isRight)
            {
                // Divide bottom edge into 4 segments (right to left)
                int segment = ((width - x) * 4) / width;
                return 7 + Math.Min(3, segment); // Indices 7-10
            }

            // Bottom-left corner
            if (isBottom && isLeft)
                return 10; // Index 10

            // Left edge (3 points: indices 11-13)
            if (isLeft && !isTop && !isBottom)
            {
                // Divide left edge into 3 segments (bottom to top)
                int segment = ((height - y) * 3) / height;
                return 11 + Math.Min(2, segment); // Indices 11-13
            }

            // Top-left corner
            if (isTop && isLeft)
                return 13; // Index 13

            // Center region (2 points: indices 14-15)
            // Use center-left for left half, center-right for right half
            if (x < width / 2)
                return 14; // Index 14 (center-left)
            else
                return 15; // Index 15 (center-right)
        }

        unsafe public static void DoDummyCapture()
        {
            int NV12ySize = Globals.Instance.Width * Globals.Instance.Height;
            int NV12uvSize = (Globals.Instance.Width * Globals.Instance.Height) / 2; // UV-Plane is half as big as Y-Plane in NV12
            byte[] managedArrayY = new byte[NV12ySize];
            byte[] managedArrayUV = new byte[NV12uvSize];
            (managedArrayY, managedArrayUV) = GenerateDummyYUVColor(Globals.Instance.Width, Globals.Instance.Height);
            _ = Networking.SendImageAsync(managedArrayY, managedArrayUV, Globals.Instance.Width, Globals.Instance.Height);
            return;
        }

        public static (byte[] yData, byte[] uvData) GenerateDummyYUVRandom(int width, int height)
        {
            int ySize = width * height;
            int uvSize = (width * height) / 2;

            byte[] yData = new byte[ySize]; 
            byte[] uvData = new byte[uvSize];

            Random rnd = new Random();
            rnd.NextBytes(yData);
            rnd.NextBytes(uvData);

            return (yData, uvData);
        }

        public static (byte[] yData, byte[] uvData) GenerateDummyYUVColor(int width, int height)
        {
            int ySize = width * height;
            int uvSize = (width * height) / 2;

            byte[] yData = new byte[ySize]; 
            byte[] uvData = new byte[uvSize];


            for (int i = 0; i < ySize; i++)
            {
                yData[i] = 128; 
            }

            for (int i = 0; i < uvSize; i += 2)
            {
                uvData[i] = 128;
                uvData[i + 1] = 255;
            }

            return (yData, uvData);
        }

    }

}