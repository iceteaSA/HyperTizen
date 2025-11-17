using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using Tizen.System;

namespace HyperTizen.Capture
{
    /// <summary>
    /// Pixel sampling capture method using libvideoenhance.so
    /// Samples individual pixels from screen edges for ambient lighting
    /// Adapted from original HyperTizen Capturer.cs to use NV12/FlatBuffers format
    /// </summary>
    public class PixelSamplingCaptureMethod : ICaptureMethod
    {
        private bool _isInitialized = false;
        private Condition _condition;

        // Track which API variant and library path works
        private string _workingVariant = null; // T6, T7, T9A, T9B, T9C, T9 (ppi_ve_*)
        private string _workingLibPath = null; // SO, SO0 (only .so and .so.0 exist on Tizen 9)

        // Pre-calculated pixel coordinates (calculated once during initialization)
        private struct PixelCoordinate
        {
            public int X;
            public int Y;
        }
        private PixelCoordinate[] _pixelCoordinates = null;

        // 16-point sampling grid (normalized coordinates 0.0-1.0)
        // 4 points per edge for better color representation
        // With synchronized batching, we achieve 40 FPS even with 16 points
        private CapturePoint[] _capturedPoints = new CapturePoint[] {
            // Top edge (4 points) - left to right
            new CapturePoint(0.20, 0.05),
            new CapturePoint(0.40, 0.05),
            new CapturePoint(0.60, 0.05),
            new CapturePoint(0.80, 0.05),
            // Right edge (4 points) - top to bottom
            new CapturePoint(0.95, 0.20),
            new CapturePoint(0.95, 0.40),
            new CapturePoint(0.95, 0.60),
            new CapturePoint(0.95, 0.80),
            // Bottom edge (4 points) - right to left
            new CapturePoint(0.80, 0.95),
            new CapturePoint(0.60, 0.95),
            new CapturePoint(0.40, 0.95),
            new CapturePoint(0.20, 0.95),
            // Left edge (4 points) - bottom to top
            new CapturePoint(0.05, 0.80),
            new CapturePoint(0.05, 0.60),
            new CapturePoint(0.05, 0.40),
            new CapturePoint(0.05, 0.20)
        };

        public string Name => "Pixel Sampling";
        public CaptureMethodType Type => CaptureMethodType.PixelSampling;

        #region P/Invoke Declarations

        // ===== Tizen 6 API (cs_ve_* prefix) - Test all library paths =====

        // Library: /usr/lib/libvideoenhance.so
        [DllImport("/usr/lib/libvideoenhance.so", CallingConvention = CallingConvention.Cdecl,
            EntryPoint = "cs_ve_get_rgb_measure_condition")]
        private static extern int MeasureCondition_T6_SO(out Condition condition);
        [DllImport("/usr/lib/libvideoenhance.so", CallingConvention = CallingConvention.Cdecl,
            EntryPoint = "cs_ve_set_rgb_measure_position")]
        private static extern int MeasurePosition_T6_SO(int index, int x, int y);
        [DllImport("/usr/lib/libvideoenhance.so", CallingConvention = CallingConvention.Cdecl,
            EntryPoint = "cs_ve_get_rgb_measure_pixel")]
        private static extern int MeasurePixel_T6_SO(int index, out Color color);

        // Library: /usr/lib/libvideoenhance.so.0
        [DllImport("/usr/lib/libvideoenhance.so.0", CallingConvention = CallingConvention.Cdecl,
            EntryPoint = "cs_ve_get_rgb_measure_condition")]
        private static extern int MeasureCondition_T6_SO0(out Condition condition);
        [DllImport("/usr/lib/libvideoenhance.so.0", CallingConvention = CallingConvention.Cdecl,
            EntryPoint = "cs_ve_set_rgb_measure_position")]
        private static extern int MeasurePosition_T6_SO0(int index, int x, int y);
        [DllImport("/usr/lib/libvideoenhance.so.0", CallingConvention = CallingConvention.Cdecl,
            EntryPoint = "cs_ve_get_rgb_measure_pixel")]
        private static extern int MeasurePixel_T6_SO0(int index, out Color color);

        // ===== Tizen 7 API (ve_* prefix) - Test all library paths =====

        // Library: /usr/lib/libvideoenhance.so
        [DllImport("/usr/lib/libvideoenhance.so", CallingConvention = CallingConvention.Cdecl,
            EntryPoint = "ve_get_rgb_measure_condition")]
        private static extern int MeasureCondition_T7_SO(out Condition condition);
        [DllImport("/usr/lib/libvideoenhance.so", CallingConvention = CallingConvention.Cdecl,
            EntryPoint = "ve_set_rgb_measure_position")]
        private static extern int MeasurePosition_T7_SO(int index, int x, int y);
        [DllImport("/usr/lib/libvideoenhance.so", CallingConvention = CallingConvention.Cdecl,
            EntryPoint = "ve_get_rgb_measure_pixel")]
        private static extern int MeasurePixel_T7_SO(int index, out Color color);

        // Library: /usr/lib/libvideoenhance.so.0
        [DllImport("/usr/lib/libvideoenhance.so.0", CallingConvention = CallingConvention.Cdecl,
            EntryPoint = "ve_get_rgb_measure_condition")]
        private static extern int MeasureCondition_T7_SO0(out Condition condition);
        [DllImport("/usr/lib/libvideoenhance.so.0", CallingConvention = CallingConvention.Cdecl,
            EntryPoint = "ve_set_rgb_measure_position")]
        private static extern int MeasurePosition_T7_SO0(int index, int x, int y);
        [DllImport("/usr/lib/libvideoenhance.so.0", CallingConvention = CallingConvention.Cdecl,
            EntryPoint = "ve_get_rgb_measure_pixel")]
        private static extern int MeasurePixel_T7_SO0(int index, out Color color);

        // ===== Tizen 9+ API variant A (tizen_ve_* prefix) - Test all library paths =====

        // Library: /usr/lib/libvideoenhance.so
        [DllImport("/usr/lib/libvideoenhance.so", CallingConvention = CallingConvention.Cdecl,
            EntryPoint = "tizen_ve_get_rgb_measure_condition")]
        private static extern int MeasureCondition_T9A_SO(out Condition condition);
        [DllImport("/usr/lib/libvideoenhance.so", CallingConvention = CallingConvention.Cdecl,
            EntryPoint = "tizen_ve_set_rgb_measure_position")]
        private static extern int MeasurePosition_T9A_SO(int index, int x, int y);
        [DllImport("/usr/lib/libvideoenhance.so", CallingConvention = CallingConvention.Cdecl,
            EntryPoint = "tizen_ve_get_rgb_measure_pixel")]
        private static extern int MeasurePixel_T9A_SO(int index, out Color color);

        // Library: /usr/lib/libvideoenhance.so.0
        [DllImport("/usr/lib/libvideoenhance.so.0", CallingConvention = CallingConvention.Cdecl,
            EntryPoint = "tizen_ve_get_rgb_measure_condition")]
        private static extern int MeasureCondition_T9A_SO0(out Condition condition);
        [DllImport("/usr/lib/libvideoenhance.so.0", CallingConvention = CallingConvention.Cdecl,
            EntryPoint = "tizen_ve_set_rgb_measure_position")]
        private static extern int MeasurePosition_T9A_SO0(int index, int x, int y);
        [DllImport("/usr/lib/libvideoenhance.so.0", CallingConvention = CallingConvention.Cdecl,
            EntryPoint = "tizen_ve_get_rgb_measure_pixel")]
        private static extern int MeasurePixel_T9A_SO0(int index, out Color color);

        // ===== Tizen 9+ API variant B (samsung_ve_* prefix) - Test all library paths =====

        // Library: /usr/lib/libvideoenhance.so
        [DllImport("/usr/lib/libvideoenhance.so", CallingConvention = CallingConvention.Cdecl,
            EntryPoint = "samsung_ve_get_rgb_measure_condition")]
        private static extern int MeasureCondition_T9B_SO(out Condition condition);
        [DllImport("/usr/lib/libvideoenhance.so", CallingConvention = CallingConvention.Cdecl,
            EntryPoint = "samsung_ve_set_rgb_measure_position")]
        private static extern int MeasurePosition_T9B_SO(int index, int x, int y);
        [DllImport("/usr/lib/libvideoenhance.so", CallingConvention = CallingConvention.Cdecl,
            EntryPoint = "samsung_ve_get_rgb_measure_pixel")]
        private static extern int MeasurePixel_T9B_SO(int index, out Color color);

        // Library: /usr/lib/libvideoenhance.so.0
        [DllImport("/usr/lib/libvideoenhance.so.0", CallingConvention = CallingConvention.Cdecl,
            EntryPoint = "samsung_ve_get_rgb_measure_condition")]
        private static extern int MeasureCondition_T9B_SO0(out Condition condition);
        [DllImport("/usr/lib/libvideoenhance.so.0", CallingConvention = CallingConvention.Cdecl,
            EntryPoint = "samsung_ve_set_rgb_measure_position")]
        private static extern int MeasurePosition_T9B_SO0(int index, int x, int y);
        [DllImport("/usr/lib/libvideoenhance.so.0", CallingConvention = CallingConvention.Cdecl,
            EntryPoint = "samsung_ve_get_rgb_measure_pixel")]
        private static extern int MeasurePixel_T9B_SO0(int index, out Color color);

        // ===== Tizen 9+ API variant C (no prefix) - Test all library paths =====

        // Library: /usr/lib/libvideoenhance.so
        [DllImport("/usr/lib/libvideoenhance.so", CallingConvention = CallingConvention.Cdecl,
            EntryPoint = "get_rgb_measure_condition")]
        private static extern int MeasureCondition_T9C_SO(out Condition condition);
        [DllImport("/usr/lib/libvideoenhance.so", CallingConvention = CallingConvention.Cdecl,
            EntryPoint = "set_rgb_measure_position")]
        private static extern int MeasurePosition_T9C_SO(int index, int x, int y);
        [DllImport("/usr/lib/libvideoenhance.so", CallingConvention = CallingConvention.Cdecl,
            EntryPoint = "get_rgb_measure_pixel")]
        private static extern int MeasurePixel_T9C_SO(int index, out Color color);

        // Library: /usr/lib/libvideoenhance.so.0
        [DllImport("/usr/lib/libvideoenhance.so.0", CallingConvention = CallingConvention.Cdecl,
            EntryPoint = "get_rgb_measure_condition")]
        private static extern int MeasureCondition_T9C_SO0(out Condition condition);
        [DllImport("/usr/lib/libvideoenhance.so.0", CallingConvention = CallingConvention.Cdecl,
            EntryPoint = "set_rgb_measure_position")]
        private static extern int MeasurePosition_T9C_SO0(int index, int x, int y);
        [DllImport("/usr/lib/libvideoenhance.so.0", CallingConvention = CallingConvention.Cdecl,
            EntryPoint = "get_rgb_measure_pixel")]
        private static extern int MeasurePixel_T9C_SO0(int index, out Color color);

        // ===== Tizen 9 API (ppi_ve_* prefix) - CONFIRMED via analysis =====

        // Library: /usr/lib/libvideoenhance.so
        [DllImport("/usr/lib/libvideoenhance.so", CallingConvention = CallingConvention.Cdecl,
            EntryPoint = "ppi_ve_get_rgb_measure_condition")]
        private static extern int MeasureCondition_T9_SO(out Condition condition);
        [DllImport("/usr/lib/libvideoenhance.so", CallingConvention = CallingConvention.Cdecl,
            EntryPoint = "ppi_ve_set_rgb_measure_position")]
        private static extern int MeasurePosition_T9_SO(int index, int x, int y);
        [DllImport("/usr/lib/libvideoenhance.so", CallingConvention = CallingConvention.Cdecl,
            EntryPoint = "ppi_ve_get_rgb_measure_pixel")]
        private static extern int MeasurePixel_T9_SO(int index, out Color color);

        // Library: /usr/lib/libvideoenhance.so.0
        [DllImport("/usr/lib/libvideoenhance.so.0", CallingConvention = CallingConvention.Cdecl,
            EntryPoint = "ppi_ve_get_rgb_measure_condition")]
        private static extern int MeasureCondition_T9_SO0(out Condition condition);
        [DllImport("/usr/lib/libvideoenhance.so.0", CallingConvention = CallingConvention.Cdecl,
            EntryPoint = "ppi_ve_set_rgb_measure_position")]
        private static extern int MeasurePosition_T9_SO0(int index, int x, int y);
        [DllImport("/usr/lib/libvideoenhance.so.0", CallingConvention = CallingConvention.Cdecl,
            EntryPoint = "ppi_ve_get_rgb_measure_pixel")]
        private static extern int MeasurePixel_T9_SO0(int index, out Color color);

        #endregion

        #region Native Structs

        /// <summary>
        /// Color struct for 10-bit RGB values (0-1023)
        /// </summary>
        [StructLayout(LayoutKind.Sequential)]
        public struct Color
        {
            public int R;
            public int G;
            public int B;
        }

        /// <summary>
        /// Condition struct containing screen parameters and sampling configuration
        /// </summary>
        [StructLayout(LayoutKind.Sequential)]
        public struct Condition
        {
            public int ScreenCapturePoints;  // Max number of points that can be sampled simultaneously
            public int PixelDensityX;         // Pixel density in X direction
            public int PixelDensityY;         // Pixel density in Y direction
            public int SleepMS;               // Milliseconds to sleep between position set and pixel read
            public int Width;                 // Screen width in pixels
            public int Height;                // Screen height in pixels
        }

        /// <summary>
        /// Capture point with normalized coordinates (0.0-1.0)
        /// </summary>
        public struct CapturePoint
        {
            public CapturePoint(double x, double y)
            {
                this.X = x;
                this.Y = y;
            }

            public double X;
            public double Y;
        }

        #endregion

        /// <summary>
        /// Check if pixel sampling library is available
        /// </summary>
        public bool IsAvailable()
        {
            Helper.Log.Write(Helper.eLogType.Debug, "PixelSampling: Checking availability...");

            // Check if library file exists
            if (!System.IO.File.Exists("/usr/lib/libvideoenhance.so"))
            {
                Helper.Log.Write(Helper.eLogType.Debug, "PixelSampling: libvideoenhance.so not found");
                return false;
            }

            Helper.Log.Write(Helper.eLogType.Debug, "PixelSampling: Library found, available");
            return true;
        }

        /// <summary>
        /// Test pixel sampling by attempting to get screen condition
        /// </summary>
        public bool Test()
        {
            if (!IsAvailable())
                return false;

            try
            {
                Helper.Log.Write(Helper.eLogType.Info, "PixelSampling: Testing capture...");

                bool success = GetCondition();

                if (success)
                {
                    // Pre-calculate coordinates during test
                    PreCalculateCoordinates();

                    Helper.Log.Write(Helper.eLogType.Info,
                        $"PixelSampling Test: SUCCESS - Screen: {_condition.Width}x{_condition.Height}, " +
                        $"Points: {_condition.ScreenCapturePoints}, Sleep: {_condition.SleepMS}ms");
                    _isInitialized = true;
                    return true;
                }
                else
                {
                    Helper.Log.Write(Helper.eLogType.Warning, "PixelSampling Test: GetCondition failed");
                    return false;
                }
            }
            catch (Exception ex)
            {
                Helper.Log.Write(Helper.eLogType.Error, $"PixelSampling Test exception: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Get screen condition parameters from VideoEnhance library
        /// Tests ALL combinations of API variants and library paths systematically
        /// </summary>
        private bool GetCondition()
        {
            Helper.Log.Write(Helper.eLogType.Info,
                "PixelSampling: Testing ALL combinations of entry points and library paths...");

            // Test Tizen 6 (cs_ve_*) with existing library paths
            if (TryVariant(
                () => MeasureCondition_T6_SO(out _condition),
                (idx, x, y) => MeasurePosition_T6_SO(idx, x, y),
                MeasurePixel_T6_SO,
                "T6", "SO", "cs_ve_*", ".so")) return true;
            if (TryVariant(
                () => MeasureCondition_T6_SO0(out _condition),
                (idx, x, y) => MeasurePosition_T6_SO0(idx, x, y),
                MeasurePixel_T6_SO0,
                "T6", "SO0", "cs_ve_*", ".so.0")) return true;

            // Test Tizen 7 (ve_*) with existing library paths
            if (TryVariant(
                () => MeasureCondition_T7_SO(out _condition),
                (idx, x, y) => MeasurePosition_T7_SO(idx, x, y),
                MeasurePixel_T7_SO,
                "T7", "SO", "ve_*", ".so")) return true;
            if (TryVariant(
                () => MeasureCondition_T7_SO0(out _condition),
                (idx, x, y) => MeasurePosition_T7_SO0(idx, x, y),
                MeasurePixel_T7_SO0,
                "T7", "SO0", "ve_*", ".so.0")) return true;

            // Test Tizen 9+ variant A (tizen_ve_*) with existing library paths
            if (TryVariant(
                () => MeasureCondition_T9A_SO(out _condition),
                (idx, x, y) => MeasurePosition_T9A_SO(idx, x, y),
                MeasurePixel_T9A_SO,
                "T9A", "SO", "tizen_ve_*", ".so")) return true;
            if (TryVariant(
                () => MeasureCondition_T9A_SO0(out _condition),
                (idx, x, y) => MeasurePosition_T9A_SO0(idx, x, y),
                MeasurePixel_T9A_SO0,
                "T9A", "SO0", "tizen_ve_*", ".so.0")) return true;

            // Test Tizen 9+ variant B (samsung_ve_*) with existing library paths
            if (TryVariant(
                () => MeasureCondition_T9B_SO(out _condition),
                (idx, x, y) => MeasurePosition_T9B_SO(idx, x, y),
                MeasurePixel_T9B_SO,
                "T9B", "SO", "samsung_ve_*", ".so")) return true;
            if (TryVariant(
                () => MeasureCondition_T9B_SO0(out _condition),
                (idx, x, y) => MeasurePosition_T9B_SO0(idx, x, y),
                MeasurePixel_T9B_SO0,
                "T9B", "SO0", "samsung_ve_*", ".so.0")) return true;

            // Test Tizen 9+ variant C (no prefix) with existing library paths
            if (TryVariant(
                () => MeasureCondition_T9C_SO(out _condition),
                (idx, x, y) => MeasurePosition_T9C_SO(idx, x, y),
                MeasurePixel_T9C_SO,
                "T9C", "SO", "no prefix", ".so")) return true;
            if (TryVariant(
                () => MeasureCondition_T9C_SO0(out _condition),
                (idx, x, y) => MeasurePosition_T9C_SO0(idx, x, y),
                MeasurePixel_T9C_SO0,
                "T9C", "SO0", "no prefix", ".so.0")) return true;

            // Test Tizen 9 actual API (ppi_ve_*) - CONFIRMED via library analysis
            if (TryVariant(
                () => MeasureCondition_T9_SO(out _condition),
                (idx, x, y) => MeasurePosition_T9_SO(idx, x, y),
                MeasurePixel_T9_SO,
                "T9", "SO", "ppi_ve_*", ".so")) return true;
            if (TryVariant(
                () => MeasureCondition_T9_SO0(out _condition),
                (idx, x, y) => MeasurePosition_T9_SO0(idx, x, y),
                MeasurePixel_T9_SO0,
                "T9", "SO0", "ppi_ve_*", ".so.0")) return true;

            // All combinations failed
            Helper.Log.Write(Helper.eLogType.Error,
                "PixelSampling: ALL 12 combinations failed (6 entry point variants × 2 library paths)");
            Helper.Log.Write(Helper.eLogType.Error,
                "PixelSampling: libvideoenhance.so does not support RGB pixel sampling on this Tizen version");
            return false;
        }

        /// <summary>
        /// Try a specific API variant + library path combination
        /// Tests ALL 3 entry points to ensure complete API surface exists
        /// </summary>
        private delegate int MeasurePixelDelegate(int index, out Color color);

        private bool TryVariant(
            Func<int> conditionFunc,
            Func<int, int, int, int> positionFunc,
            MeasurePixelDelegate pixelFunc,
            string variant,
            string libPath,
            string entryPrefix,
            string libSuffix)
        {
            try
            {
                Helper.Log.Write(Helper.eLogType.Debug,
                    $"PixelSampling: Testing {variant} ({entryPrefix}) with libvideoenhance{libSuffix}");

                // Test 1: MeasureCondition
                int conditionResult = conditionFunc();
                if (conditionResult < 0)
                {
                    Helper.Log.Write(Helper.eLogType.Debug,
                        $"PixelSampling: {variant}/{libPath} condition returned error {conditionResult}");
                    return false;
                }

                // Test 2: MeasurePosition (validate entry point exists with dummy coordinates)
                int positionResult = positionFunc(0, 0, 0);
                // Position may fail if called before proper setup, but entry point should exist
                Helper.Log.Write(Helper.eLogType.Debug,
                    $"PixelSampling: {variant}/{libPath} position entry point exists (result: {positionResult})");

                // Test 3: MeasurePixel (validate entry point exists)
                Color dummyColor;
                int pixelResult = pixelFunc(0, out dummyColor);
                // Pixel may fail if no position set yet, but entry point should exist
                Helper.Log.Write(Helper.eLogType.Debug,
                    $"PixelSampling: {variant}/{libPath} pixel entry point exists (result: {pixelResult})");

                // Success - all three entry points exist and condition succeeded
                _workingVariant = variant;
                _workingLibPath = libPath;
                Helper.Log.Write(Helper.eLogType.Info,
                    $"PixelSampling: ✓ SUCCESS - All 3 entry points validated for {variant} ({entryPrefix}) with libvideoenhance{libSuffix}");
                Helper.Log.Write(Helper.eLogType.Debug, $"PixelSampling: Condition result: {conditionResult}");
                LogConditionDetails();
                return true;
            }
            catch (EntryPointNotFoundException ex)
            {
                Helper.Log.Write(Helper.eLogType.Debug,
                    $"PixelSampling: {variant}/{libPath} entry point not found: {ex.Message}");
                return false;
            }
            catch (DllNotFoundException ex)
            {
                Helper.Log.Write(Helper.eLogType.Debug,
                    $"PixelSampling: {variant}/{libPath} library file not found: {ex.Message}");
                return false;
            }
            catch (Exception ex)
            {
                Helper.Log.Write(Helper.eLogType.Debug,
                    $"PixelSampling: {variant}/{libPath} exception: {ex.GetType().Name}: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Helper method to log condition details
        /// </summary>
        private void LogConditionDetails()
        {
            Helper.Log.Write(Helper.eLogType.Info,
                $"PixelSampling: Condition - Width: {_condition.Width}, Height: {_condition.Height}, " +
                $"Points: {_condition.ScreenCapturePoints}, PixelDensity: {_condition.PixelDensityX}x{_condition.PixelDensityY}, " +
                $"Sleep: {_condition.SleepMS}ms");
        }

        /// <summary>
        /// Call correct MeasurePosition variant based on working combination
        /// </summary>
        private int CallMeasurePosition(int index, int x, int y)
        {
            string key = $"{_workingVariant}_{_workingLibPath}";
            switch (key)
            {
                case "T6_SO": return MeasurePosition_T6_SO(index, x, y);
                case "T6_SO0": return MeasurePosition_T6_SO0(index, x, y);
                case "T7_SO": return MeasurePosition_T7_SO(index, x, y);
                case "T7_SO0": return MeasurePosition_T7_SO0(index, x, y);
                case "T9A_SO": return MeasurePosition_T9A_SO(index, x, y);
                case "T9A_SO0": return MeasurePosition_T9A_SO0(index, x, y);
                case "T9B_SO": return MeasurePosition_T9B_SO(index, x, y);
                case "T9B_SO0": return MeasurePosition_T9B_SO0(index, x, y);
                case "T9C_SO": return MeasurePosition_T9C_SO(index, x, y);
                case "T9C_SO0": return MeasurePosition_T9C_SO0(index, x, y);
                case "T9_SO": return MeasurePosition_T9_SO(index, x, y);
                case "T9_SO0": return MeasurePosition_T9_SO0(index, x, y);
                default:
                    throw new InvalidOperationException($"Unknown variant: {key}");
            }
        }

        /// <summary>
        /// Call correct MeasurePixel variant based on working combination
        /// </summary>
        private int CallMeasurePixel(int index, out Color color)
        {
            string key = $"{_workingVariant}_{_workingLibPath}";
            switch (key)
            {
                case "T6_SO": return MeasurePixel_T6_SO(index, out color);
                case "T6_SO0": return MeasurePixel_T6_SO0(index, out color);
                case "T7_SO": return MeasurePixel_T7_SO(index, out color);
                case "T7_SO0": return MeasurePixel_T7_SO0(index, out color);
                case "T9A_SO": return MeasurePixel_T9A_SO(index, out color);
                case "T9A_SO0": return MeasurePixel_T9A_SO0(index, out color);
                case "T9B_SO": return MeasurePixel_T9B_SO(index, out color);
                case "T9B_SO0": return MeasurePixel_T9B_SO0(index, out color);
                case "T9C_SO": return MeasurePixel_T9C_SO(index, out color);
                case "T9C_SO0": return MeasurePixel_T9C_SO0(index, out color);
                case "T9_SO": return MeasurePixel_T9_SO(index, out color);
                case "T9_SO0": return MeasurePixel_T9_SO0(index, out color);
                default:
                    throw new InvalidOperationException($"Unknown variant: {key}");
            }
        }

        /// <summary>
        /// Pre-calculate pixel coordinates from normalized positions
        /// Called once during initialization to avoid repeated calculations
        /// </summary>
        private void PreCalculateCoordinates()
        {
            _pixelCoordinates = new PixelCoordinate[_capturedPoints.Length];

            for (int i = 0; i < _capturedPoints.Length; i++)
            {
                // Convert normalized coordinates to pixel coordinates
                int x = (int)(_capturedPoints[i].X * (double)_condition.Width) - _condition.PixelDensityX / 2;
                int y = (int)(_capturedPoints[i].Y * (double)_condition.Height) - _condition.PixelDensityY / 2;

                // Clamp coordinates to valid screen bounds
                x = (x >= _condition.Width - _condition.PixelDensityX) ?
                    _condition.Width - (_condition.PixelDensityX + 1) : x;
                y = (y >= _condition.Height - _condition.PixelDensityY) ?
                    (_condition.Height - _condition.PixelDensityY + 1) : y;

                // Ensure coordinates are not negative
                x = Math.Max(0, x);
                y = Math.Max(0, y);

                _pixelCoordinates[i].X = x;
                _pixelCoordinates[i].Y = y;
            }

            Helper.Log.Write(Helper.eLogType.Info,
                $"PixelSampling: Pre-calculated {_pixelCoordinates.Length} pixel coordinates");
        }

        /// <summary>
        /// Sample pixel colors from predefined screen positions
        /// OPTIMIZED: Sets ALL positions first, then ONE sleep, then reads ALL pixels
        /// This ensures all sampling happens at the same moment for temporal consistency
        /// </summary>
        private Color[] GetColors()
        {
            Color[] colorData = new Color[_capturedPoints.Length];

            if (_condition.ScreenCapturePoints == 0)
            {
                Helper.Log.Write(Helper.eLogType.Error, "PixelSampling: ScreenCapturePoints is 0");
                return colorData;
            }

            // PHASE 1: Set ALL measurement positions first (no delays between batches)
            int i = 0;
            while (i < _capturedPoints.Length)
            {
                // Set positions for this batch
                for (int j = 0; j < _condition.ScreenCapturePoints && i < _capturedPoints.Length; j++)
                {
                    // Use pre-calculated pixel coordinates
                    int x = _pixelCoordinates[i].X;
                    int y = _pixelCoordinates[i].Y;

                    // Set the measurement position
                    int res = CallMeasurePosition(j, x, y);

                    if (res < 0)
                    {
                        Helper.Log.Write(Helper.eLogType.Error,
                            $"PixelSampling: MeasurePosition failed for point {i} at ({x}, {y}) with error {res}");
                    }

                    i++;
                }
            }

            // PHASE 2: Single sleep after ALL positions are set
            // This ensures all measurements happen at approximately the same time
            if (_condition.SleepMS > 0)
            {
                Thread.Sleep(_condition.SleepMS);
            }

            // PHASE 3: Read ALL pixel colors in batches
            i = 0;
            while (i < _capturedPoints.Length)
            {
                // Read pixels for this batch
                for (int j = 0; j < _condition.ScreenCapturePoints && i < _capturedPoints.Length; j++)
                {
                    Color color;
                    int res = CallMeasurePixel(j, out color);

                    if (res < 0)
                    {
                        Helper.Log.Write(Helper.eLogType.Error,
                            $"PixelSampling: MeasurePixel failed for point {i} with error {res}");
                        // Use black as fallback
                        color.R = 0;
                        color.G = 0;
                        color.B = 0;
                    }
                    else
                    {
                        // Validate color data (10-bit values should be 0-1023)
                        bool invalidColorData = color.R > 1023 || color.G > 1023 || color.B > 1023 ||
                                                color.R < 0 || color.G < 0 || color.B < 0;

                        if (invalidColorData)
                        {
                            Helper.Log.Write(Helper.eLogType.Warning,
                                $"PixelSampling: Invalid color data at point {i}: R={color.R}, G={color.G}, B={color.B}");
                            // Clamp to valid range
                            color.R = Math.Max(0, Math.Min(1023, color.R));
                            color.G = Math.Max(0, Math.Min(1023, color.G));
                            color.B = Math.Max(0, Math.Min(1023, color.B));
                        }
                    }

                    colorData[i] = color;
                    i++;
                }
            }

            return colorData;
        }

        /// <summary>
        /// Convert sampled pixel colors to NV12 format using BT.2020 color space
        /// Creates a virtual 64x48 image with sampled colors mapped to screen edges
        /// Uses BT.2020 coefficients for HDR10+ compatibility
        /// </summary>
        private (byte[] yData, byte[] uvData) ConvertColorsToNV12(Color[] colors)
        {
            const int width = 64;
            const int height = 48;

            // Allocate NV12 buffers
            byte[] yData = new byte[width * height];
            byte[] uvData = new byte[width * height / 2]; // UV plane is half the size

            // Create virtual RGB image (same logic as original ToImage method)
            byte[] rgbImage = new byte[width * height * 3]; // RGB888

            // Initialize with black
            for (int i = 0; i < rgbImage.Length; i++)
            {
                rgbImage[i] = 0;
            }

            // 16-point color mapping (4 points per edge)
            // colors[0-3]   = Top edge (left to right)
            // colors[4-7]   = Right edge (top to bottom)
            // colors[8-11]  = Bottom edge (right to left)
            // colors[12-15] = Left edge (bottom to top)

            // Top edge (colors 0-3) with linear interpolation
            for (int x = 0; x < 64; x++)
            {
                // Determine which segment this x falls into (4 segments)
                float segmentPos = (x / 63.0f) * 3.0f; // 0.0 to 3.0
                int segment = Math.Min(2, (int)segmentPos); // 0, 1, or 2
                float t = segmentPos - segment; // Position within segment (0.0 to 1.0)

                byte r, g, b;
                if (segment == 0) // colors[0] to colors[1]
                {
                    r = (byte)((1 - t) * ScaleTo8Bit(colors[0].R) + t * ScaleTo8Bit(colors[1].R));
                    g = (byte)((1 - t) * ScaleTo8Bit(colors[0].G) + t * ScaleTo8Bit(colors[1].G));
                    b = (byte)((1 - t) * ScaleTo8Bit(colors[0].B) + t * ScaleTo8Bit(colors[1].B));
                }
                else if (segment == 1) // colors[1] to colors[2]
                {
                    r = (byte)((1 - t) * ScaleTo8Bit(colors[1].R) + t * ScaleTo8Bit(colors[2].R));
                    g = (byte)((1 - t) * ScaleTo8Bit(colors[1].G) + t * ScaleTo8Bit(colors[2].G));
                    b = (byte)((1 - t) * ScaleTo8Bit(colors[1].B) + t * ScaleTo8Bit(colors[2].B));
                }
                else // segment == 2, colors[2] to colors[3]
                {
                    r = (byte)((1 - t) * ScaleTo8Bit(colors[2].R) + t * ScaleTo8Bit(colors[3].R));
                    g = (byte)((1 - t) * ScaleTo8Bit(colors[2].G) + t * ScaleTo8Bit(colors[3].G));
                    b = (byte)((1 - t) * ScaleTo8Bit(colors[2].B) + t * ScaleTo8Bit(colors[3].B));
                }

                for (int y = 0; y < 4; y++)
                {
                    int idx = (y * width + x) * 3;
                    rgbImage[idx + 0] = r;
                    rgbImage[idx + 1] = g;
                    rgbImage[idx + 2] = b;
                }
            }

            // Bottom edge (colors 8-11) with linear interpolation (right to left)
            for (int x = 0; x < 64; x++)
            {
                // Determine which segment this x falls into (4 segments, reversed)
                float segmentPos = (x / 63.0f) * 3.0f; // 0.0 to 3.0
                int segment = Math.Min(2, (int)segmentPos); // 0, 1, or 2
                float t = segmentPos - segment; // Position within segment (0.0 to 1.0)

                byte r, g, b;
                if (segment == 0) // colors[11] to colors[10]
                {
                    r = (byte)((1 - t) * ScaleTo8Bit(colors[11].R) + t * ScaleTo8Bit(colors[10].R));
                    g = (byte)((1 - t) * ScaleTo8Bit(colors[11].G) + t * ScaleTo8Bit(colors[10].G));
                    b = (byte)((1 - t) * ScaleTo8Bit(colors[11].B) + t * ScaleTo8Bit(colors[10].B));
                }
                else if (segment == 1) // colors[10] to colors[9]
                {
                    r = (byte)((1 - t) * ScaleTo8Bit(colors[10].R) + t * ScaleTo8Bit(colors[9].R));
                    g = (byte)((1 - t) * ScaleTo8Bit(colors[10].G) + t * ScaleTo8Bit(colors[9].G));
                    b = (byte)((1 - t) * ScaleTo8Bit(colors[10].B) + t * ScaleTo8Bit(colors[9].B));
                }
                else // segment == 2, colors[9] to colors[8]
                {
                    r = (byte)((1 - t) * ScaleTo8Bit(colors[9].R) + t * ScaleTo8Bit(colors[8].R));
                    g = (byte)((1 - t) * ScaleTo8Bit(colors[9].G) + t * ScaleTo8Bit(colors[8].G));
                    b = (byte)((1 - t) * ScaleTo8Bit(colors[9].B) + t * ScaleTo8Bit(colors[8].B));
                }

                for (int y = 44; y < 48; y++)
                {
                    int idx = (y * width + x) * 3;
                    rgbImage[idx + 0] = r;
                    rgbImage[idx + 1] = g;
                    rgbImage[idx + 2] = b;
                }
            }

            // Left edge (colors 12-15) with linear interpolation (bottom to top)
            for (int y = 0; y < 48; y++)
            {
                // Determine which segment this y falls into (4 segments)
                float segmentPos = (y / 47.0f) * 3.0f; // 0.0 to 3.0
                int segment = Math.Min(2, (int)segmentPos); // 0, 1, or 2
                float t = segmentPos - segment; // Position within segment (0.0 to 1.0)

                byte r, g, b;
                if (segment == 0) // colors[15] to colors[14]
                {
                    r = (byte)((1 - t) * ScaleTo8Bit(colors[15].R) + t * ScaleTo8Bit(colors[14].R));
                    g = (byte)((1 - t) * ScaleTo8Bit(colors[15].G) + t * ScaleTo8Bit(colors[14].G));
                    b = (byte)((1 - t) * ScaleTo8Bit(colors[15].B) + t * ScaleTo8Bit(colors[14].B));
                }
                else if (segment == 1) // colors[14] to colors[13]
                {
                    r = (byte)((1 - t) * ScaleTo8Bit(colors[14].R) + t * ScaleTo8Bit(colors[13].R));
                    g = (byte)((1 - t) * ScaleTo8Bit(colors[14].G) + t * ScaleTo8Bit(colors[13].G));
                    b = (byte)((1 - t) * ScaleTo8Bit(colors[14].B) + t * ScaleTo8Bit(colors[13].B));
                }
                else // segment == 2, colors[13] to colors[12]
                {
                    r = (byte)((1 - t) * ScaleTo8Bit(colors[13].R) + t * ScaleTo8Bit(colors[12].R));
                    g = (byte)((1 - t) * ScaleTo8Bit(colors[13].G) + t * ScaleTo8Bit(colors[12].G));
                    b = (byte)((1 - t) * ScaleTo8Bit(colors[13].B) + t * ScaleTo8Bit(colors[12].B));
                }

                for (int x = 0; x < 3; x++)
                {
                    int idx = (y * width + x) * 3;
                    rgbImage[idx + 0] = r;
                    rgbImage[idx + 1] = g;
                    rgbImage[idx + 2] = b;
                }
            }

            // Right edge (colors 4-7) with linear interpolation (top to bottom)
            for (int y = 0; y < 48; y++)
            {
                // Determine which segment this y falls into (4 segments)
                float segmentPos = (y / 47.0f) * 3.0f; // 0.0 to 3.0
                int segment = Math.Min(2, (int)segmentPos); // 0, 1, or 2
                float t = segmentPos - segment; // Position within segment (0.0 to 1.0)

                byte r, g, b;
                if (segment == 0) // colors[4] to colors[5]
                {
                    r = (byte)((1 - t) * ScaleTo8Bit(colors[4].R) + t * ScaleTo8Bit(colors[5].R));
                    g = (byte)((1 - t) * ScaleTo8Bit(colors[4].G) + t * ScaleTo8Bit(colors[5].G));
                    b = (byte)((1 - t) * ScaleTo8Bit(colors[4].B) + t * ScaleTo8Bit(colors[5].B));
                }
                else if (segment == 1) // colors[5] to colors[6]
                {
                    r = (byte)((1 - t) * ScaleTo8Bit(colors[5].R) + t * ScaleTo8Bit(colors[6].R));
                    g = (byte)((1 - t) * ScaleTo8Bit(colors[5].G) + t * ScaleTo8Bit(colors[6].G));
                    b = (byte)((1 - t) * ScaleTo8Bit(colors[5].B) + t * ScaleTo8Bit(colors[6].B));
                }
                else // segment == 2, colors[6] to colors[7]
                {
                    r = (byte)((1 - t) * ScaleTo8Bit(colors[6].R) + t * ScaleTo8Bit(colors[7].R));
                    g = (byte)((1 - t) * ScaleTo8Bit(colors[6].G) + t * ScaleTo8Bit(colors[7].G));
                    b = (byte)((1 - t) * ScaleTo8Bit(colors[6].B) + t * ScaleTo8Bit(colors[7].B));
                }

                for (int x = 61; x < 64; x++)
                {
                    int idx = (y * width + x) * 3;
                    rgbImage[idx + 0] = r;
                    rgbImage[idx + 1] = g;
                    rgbImage[idx + 2] = b;
                }
            }

            // Convert RGB to NV12 using BT.2020 color space (HDR10+ compatible)
            // Y plane
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    int rgbIdx = (y * width + x) * 3;
                    byte r = rgbImage[rgbIdx + 0];
                    byte g = rgbImage[rgbIdx + 1];
                    byte b = rgbImage[rgbIdx + 2];

                    // BT.2020 Y = 0.2627R + 0.678G + 0.0593B
                    int yVal = (int)(0.2627 * r + 0.678 * g + 0.0593 * b);
                    yData[y * width + x] = (byte)Math.Max(0, Math.Min(255, yVal));
                }
            }

            // UV plane (interleaved, subsampled 2x2)
            for (int y = 0; y < height; y += 2)
            {
                for (int x = 0; x < width; x += 2)
                {
                    // Sample 2x2 block
                    int rgbIdx = (y * width + x) * 3;
                    byte r = rgbImage[rgbIdx + 0];
                    byte g = rgbImage[rgbIdx + 1];
                    byte b = rgbImage[rgbIdx + 2];

                    // BT.2020 U = -0.1396R - 0.36037G + 0.5B + 128
                    // BT.2020 V = 0.5R - 0.4598G - 0.0402B + 128
                    int uVal = (int)(-0.1396 * r - 0.36037 * g + 0.5 * b + 128);
                    int vVal = (int)(0.5 * r - 0.4598 * g - 0.0402 * b + 128);

                    int uvIdx = (y / 2) * width + x;
                    uvData[uvIdx + 0] = (byte)Math.Max(0, Math.Min(255, uVal)); // U
                    uvData[uvIdx + 1] = (byte)Math.Max(0, Math.Min(255, vVal)); // V
                }
            }

            return (yData, uvData);
        }

        /// <summary>
        /// Convert 10-bit color value (0-1023) to 8-bit (0-255) using proper scaling
        /// Uses scaling rather than clamping to preserve color accuracy
        /// </summary>
        private byte ScaleTo8Bit(int value)
        {
            // Scale 10-bit (0-1023) to 8-bit (0-255)
            return (byte)Math.Min(255, value * 255 / 1023);
        }

        /// <summary>
        /// Capture screen using pixel sampling
        /// </summary>
        public CaptureResult Capture(int width, int height)
        {
            try
            {
                // Initialize if not already done
                if (!_isInitialized)
                {
                    if (!GetCondition())
                    {
                        return CaptureResult.CreateFailure("PixelSampling: Failed to get condition");
                    }

                    // Pre-calculate all pixel coordinates once
                    PreCalculateCoordinates();

                    _isInitialized = true;
                }

                // Sample pixels from screen
                Color[] colors = GetColors();

                if (colors == null || colors.Length == 0)
                {
                    return CaptureResult.CreateFailure("PixelSampling: No colors captured");
                }

                // Convert to NV12 format
                var (yData, uvData) = ConvertColorsToNV12(colors);

                // Return success with 64x48 sampled image
                return CaptureResult.CreateSuccess(yData, uvData, 64, 48);
            }
            catch (Exception ex)
            {
                return CaptureResult.CreateFailure($"PixelSampling exception: {ex.Message}");
            }
        }

        /// <summary>
        /// Clean up resources
        /// </summary>
        public void Cleanup()
        {
            _isInitialized = false;
            Helper.Log.Write(Helper.eLogType.Debug, "PixelSampling: Cleaned up");
        }
    }
}
