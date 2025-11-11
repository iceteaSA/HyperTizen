using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;

namespace HyperTizen.SDK
{
    public static unsafe class LibraryScanner
    {
        // dlopen/dlsym for dynamic library inspection
        [DllImport("libdl.so.2", CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr dlopen(string filename, int flags);

        [DllImport("libdl.so.2", CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr dlsym(IntPtr handle, string symbol);

        [DllImport("libdl.so.2", CallingConvention = CallingConvention.Cdecl)]
        private static extern string dlerror();

        [DllImport("libdl.so.2", CallingConvention = CallingConvention.Cdecl)]
        private static extern int dlclose(IntPtr handle);

        private const int RTLD_NOW = 2;
        private const int RTLD_LAZY = 1;
        private const int RTLD_NOLOAD = 4;

        /// <summary>
        /// Scan for alternative capture libraries and methods
        /// </summary>
        public static void ScanForAlternatives()
        {
            Helper.Log.Write(Helper.eLogType.Info, "");
            Helper.Log.Write(Helper.eLogType.Info, "=== Scanning for Alternative Capture Methods ===");

            // DO ALL TESTS FIRST (before heavy library scanning)
            // These are quick and won't crash

            // 1. Try T7 API (might still exist) - TEST FIRST!
            TryT7API();

            // 2. Test CAPI library directly (found by previous scan)
            TestCapiVideoCapture();

            // 3. Try framebuffer access
            TryFrameBuffer();

            // 4. Check for debug/developer libraries (just checks existence)
            CheckDeveloperLibraries();

            // THEN do heavy scanning (can be slow/crash)
            Helper.Log.Write(Helper.eLogType.Info, "");
            Helper.Log.Write(Helper.eLogType.Info, "--- Quick tests complete, starting library scan ---");
            Helper.Log.Write(Helper.eLogType.Warning, "(This may take time and could disconnect WebSocket)");

            // 5. Search for all video/capture related libraries
            SearchForLibraries();

            // 6. Try alternative Samsung libraries
            TryAlternativeSamsungLibs();

            Helper.Log.Write(Helper.eLogType.Info, "=== End Alternative Scan ===");
            Helper.Log.Write(Helper.eLogType.Info, "");
        }

        private static void SearchForLibraries()
        {
            Helper.Log.Write(Helper.eLogType.Info, "--- Searching for video/capture libraries ---");

            string[] searchPaths = new string[] {
                "/usr/lib",
                "/usr/lib/tizen-tv",
                "/opt/usr/lib",
                "/lib"
            };

            string[] patterns = new string[] {
                "*video*.so*",
                "*capture*.so*",
                "*screen*.so*",
                "*fb*.so*",
                "*display*.so*",
                "*scaler*.so*"
            };

            foreach (string path in searchPaths)
            {
                if (!Directory.Exists(path))
                    continue;

                try
                {
                    foreach (string pattern in patterns)
                    {
                        string[] files = Directory.GetFiles(path, pattern, SearchOption.TopDirectoryOnly);
                        foreach (string file in files)
                        {
                            Helper.Log.Write(Helper.eLogType.Debug, $"  Found: {file}");
                            ProbeLibrary(file);
                        }
                    }
                }
                catch (Exception ex)
                {
                    Helper.Log.Write(Helper.eLogType.Debug, $"  Error searching {path}: {ex.Message}");
                }
            }
        }

        private static void ProbeLibrary(string libraryPath)
        {
            try
            {
                // Try to load the library
                IntPtr handle = dlopen(libraryPath, RTLD_LAZY);
                if (handle == IntPtr.Zero)
                {
                    string error = dlerror();
                    Helper.Log.Write(Helper.eLogType.Debug, $"    Cannot load {Path.GetFileName(libraryPath)}: {error}");
                    return;
                }

                Helper.Log.Write(Helper.eLogType.Info, $"  ✓ Loaded: {Path.GetFileName(libraryPath)}");

                // Try to find interesting symbols
                string[] interestingSymbols = new string[] {
                    // Capture functions
                    "captureScreen", "capture_screen", "CaptureScreen",
                    "getFrame", "get_frame", "GetFrame",
                    "screenCapture", "screen_capture", "ScreenCapture",

                    // T7 API
                    "secvideo_api_capture_screen",
                    "secvideo_api_capture_screen_video_only",
                    "secvideo_api_capture_screen_unlock",

                    // T8 API variants
                    "getVideoMainYUV", "getVideoPostYUV",
                    "getVideoMain", "getVideoPost",

                    // Frame buffer
                    "fb_capture", "fbCapture", "FbCapture",
                    "getFrameBuffer", "get_frame_buffer",

                    // Generic
                    "capture", "Capture", "grab", "Grab",
                    "screenshot", "Screenshot"
                };

                List<string> foundSymbols = new List<string>();
                foreach (string symbol in interestingSymbols)
                {
                    IntPtr funcPtr = dlsym(handle, symbol);
                    if (funcPtr != IntPtr.Zero)
                    {
                        foundSymbols.Add(symbol);
                        Helper.Log.Write(Helper.eLogType.Info, $"    ✓ Symbol found: {symbol} @ 0x{funcPtr.ToInt64():X}");
                    }
                }

                if (foundSymbols.Count > 0)
                {
                    Helper.Log.Write(Helper.eLogType.Info, $"  ⭐ PROMISING: {Path.GetFileName(libraryPath)} has {foundSymbols.Count} capture-related symbols!");
                }

                dlclose(handle);
            }
            catch (Exception ex)
            {
                Helper.Log.Write(Helper.eLogType.Debug, $"    Error probing {libraryPath}: {ex.Message}");
            }
        }

        private static void TryT7API()
        {
            Helper.Log.Write(Helper.eLogType.Info, "--- Checking for T7 API (libsec-video-capture.so.0) ---");

            string[] possiblePaths = new string[] {
                "/usr/lib/libsec-video-capture.so.0",
                "/usr/lib/libsec-video-capture.so",
                "/usr/lib/libsec-video-capture.so.0.0.0",
                "/opt/usr/lib/libsec-video-capture.so.0"
            };

            foreach (string path in possiblePaths)
            {
                if (File.Exists(path))
                {
                    Helper.Log.Write(Helper.eLogType.Info, $"  ✓ FOUND: {path}");
                    Helper.Log.Write(Helper.eLogType.Info, "  ⭐ T7 API exists! May be able to use old API on Tizen 8!");

                    ProbeLibrary(path);

                    // Try to actually test the T7 API!
                    Helper.Log.Write(Helper.eLogType.Info, "  Testing if T7 API works on Tizen 8...");
                    TestT7Capture();
                    return;
                }
            }

            Helper.Log.Write(Helper.eLogType.Debug, "  ✗ T7 API not found");
        }

        [DllImport("/usr/lib/libsec-video-capture.so.0", CallingConvention = CallingConvention.Cdecl, EntryPoint = "secvideo_api_capture_screen_video_only")]
        private static extern int T7CaptureScreenVideo(int w, int h, ref SecVideoCapture.Info_t pInfo);

        private static void TestT7Capture()
        {
            try
            {
                // Allocate small test buffers
                int bufferSize = 0x7e900; // 518,400 bytes
                byte[] yBuffer = new byte[bufferSize];
                byte[] uvBuffer = new byte[bufferSize];

                fixed (byte* pY = yBuffer, pUV = uvBuffer)
                {
                    SecVideoCapture.Info_t testInfo = new SecVideoCapture.Info_t();
                    testInfo.pImageY = (IntPtr)pY;
                    testInfo.pImageUV = (IntPtr)pUV;
                    testInfo.iGivenBufferSize1 = bufferSize;
                    testInfo.iGivenBufferSize2 = bufferSize;

                    int result = T7CaptureScreenVideo(1920, 1080, ref testInfo);

                    if (result == 0)
                    {
                        Helper.Log.Write(Helper.eLogType.Info, "  ✅✅✅ T7 API WORKS ON TIZEN 8! ✅✅✅");
                        Helper.Log.Write(Helper.eLogType.Info, $"  Captured resolution: {testInfo.iWidth}x{testInfo.iHeight}");
                        Helper.Log.Write(Helper.eLogType.Info, "  ⭐⭐⭐ USE T7 API INSTEAD OF T8! ⭐⭐⭐");
                    }
                    else if (result == -4)
                    {
                        Helper.Log.Write(Helper.eLogType.Warning, "  T7 API works but returned -4 (DRM protected content)");
                        Helper.Log.Write(Helper.eLogType.Info, "  ⭐ T7 API is functional - may work on non-DRM content");
                    }
                    else
                    {
                        Helper.Log.Write(Helper.eLogType.Warning, $"  T7 API failed with code: {result}");
                    }
                }
            }
            catch (DllNotFoundException)
            {
                Helper.Log.Write(Helper.eLogType.Debug, "  T7 library exists as file but cannot be loaded");
            }
            catch (Exception ex)
            {
                Helper.Log.Write(Helper.eLogType.Debug, $"  T7 test failed: {ex.Message}");
            }
        }

        private static void TryFrameBuffer()
        {
            Helper.Log.Write(Helper.eLogType.Info, "--- Checking framebuffer devices ---");

            string[] fbDevices = new string[] {
                "/dev/fb0",
                "/dev/fb1",
                "/dev/graphics/fb0",
                "/dev/graphics/fb1"
            };

            foreach (string device in fbDevices)
            {
                if (File.Exists(device))
                {
                    Helper.Log.Write(Helper.eLogType.Info, $"  ✓ Found: {device}");

                    // Try to open it (read-only, non-blocking)
                    try
                    {
                        using (FileStream fs = new FileStream(device, FileMode.Open, FileAccess.Read, FileShare.Read))
                        {
                            Helper.Log.Write(Helper.eLogType.Info, $"  ⭐ CAN READ {device}! Size: {fs.Length} bytes");
                            Helper.Log.Write(Helper.eLogType.Info, "  This might be usable for direct framebuffer capture!");
                        }
                    }
                    catch (UnauthorizedAccessException)
                    {
                        Helper.Log.Write(Helper.eLogType.Debug, $"  ✗ {device} exists but no read permission (requires root)");
                    }
                    catch (Exception ex)
                    {
                        Helper.Log.Write(Helper.eLogType.Debug, $"  ✗ {device} error: {ex.Message}");
                    }
                }
            }
        }

        private static void CheckDeveloperLibraries()
        {
            Helper.Log.Write(Helper.eLogType.Info, "--- Checking for developer/debug libraries ---");

            string[] devLibs = new string[] {
                "/usr/lib/libdlog.so",           // Debug logging
                "/usr/lib/libdeveloper.so",       // Developer API
                "/usr/lib/libdebug.so",           // Debug API
                "/usr/lib/libtest.so",            // Test API
                "/usr/lib/libinternal.so",        // Internal API
                "/usr/lib/libtv-service.so",      // TV service
                "/usr/lib/libtv-service-internal.so"
            };

            foreach (string lib in devLibs)
            {
                if (File.Exists(lib))
                {
                    Helper.Log.Write(Helper.eLogType.Info, $"  ✓ Found: {Path.GetFileName(lib)}");
                }
            }
        }

        private static void TryAlternativeSamsungLibs()
        {
            Helper.Log.Write(Helper.eLogType.Info, "--- Checking alternative Samsung libraries ---");

            // From the reference binaries, these apps use video capture
            string[] samsungLibs = new string[] {
                // Video capture variants
                "/usr/lib/libvideo-capture-impl-sec.so",
                "/usr/lib/libvideo-capture-impl.so",
                "/usr/lib/libvideo-capture.so",
                "/usr/lib/libvideo-capture.so.0",

                // Scaler (used in reference code)
                "/usr/lib/libscaler.so",
                "/usr/lib/libscaler.so.0",

                // Display/screen
                "/usr/lib/libdisplay.so",
                "/usr/lib/libtbm.so",              // Tizen Buffer Manager
                "/usr/lib/libtdm.so",              // Tizen Display Manager

                // DRM (might have capture if we have permissions)
                "/usr/lib/libdrm.so",
                "/usr/lib/libdrm.so.2",

                // Media
                "/usr/lib/libmedia-utils.so",
                "/usr/lib/libmedia-thumbnail.so",

                // Samsung specific
                "/usr/lib/libsamsungplatform.so",
                "/usr/lib/libsamsung.so"
            };

            foreach (string lib in samsungLibs)
            {
                if (File.Exists(lib))
                {
                    Helper.Log.Write(Helper.eLogType.Info, $"  ✓ Found: {Path.GetFileName(lib)}");
                    ProbeLibrary(lib);
                }
            }
        }

        // Test the libcapi-video-capture.so functions we found
        [DllImport("/usr/lib/libcapi-video-capture.so.0.1.0", CallingConvention = CallingConvention.Cdecl, EntryPoint = "secvideo_api_capture_screen_video_only")]
        private static extern int CapiCaptureScreenVideo(int w, int h, ref SecVideoCapture.Info_t pInfo);

        [DllImport("/usr/lib/libcapi-video-capture.so.0.1.0", CallingConvention = CallingConvention.Cdecl, EntryPoint = "secvideo_api_capture_screen")]
        private static extern int CapiCaptureScreen(int w, int h, ref SecVideoCapture.Info_t pInfo);

        [DllImport("/usr/lib/libcapi-video-capture.so.0.1.0", CallingConvention = CallingConvention.Cdecl, EntryPoint = "secvideo_api_capture_screen_unlock")]
        private static extern int CapiCaptureScreenUnlock();

        private static void TestCapiVideoCapture()
        {
            Helper.Log.Write(Helper.eLogType.Info, "Testing libcapi-video-capture.so.0.1.0...");

            try
            {
                // Allocate test buffers
                int bufferSize = 0x7e900; // 518,400 bytes
                byte[] yBuffer = new byte[bufferSize];
                byte[] uvBuffer = new byte[bufferSize];

                fixed (byte* pY = yBuffer, pUV = uvBuffer)
                {
                    SecVideoCapture.Info_t testInfo = new SecVideoCapture.Info_t();
                    testInfo.pImageY = (IntPtr)pY;
                    testInfo.pImageUV = (IntPtr)pUV;
                    testInfo.iGivenBufferSize1 = bufferSize;
                    testInfo.iGivenBufferSize2 = bufferSize;

                    // Test 1: secvideo_api_capture_screen_video_only (most reliable)
                    Helper.Log.Write(Helper.eLogType.Info, "  Test 1: secvideo_api_capture_screen_video_only()");
                    int result = CapiCaptureScreenVideo(1920, 1080, ref testInfo);

                    if (result == 0)
                    {
                        Helper.Log.Write(Helper.eLogType.Info, "  ✅✅✅ CAPI VIDEO CAPTURE WORKS! ✅✅✅");
                        Helper.Log.Write(Helper.eLogType.Info, $"  Captured resolution: {testInfo.iWidth}x{testInfo.iHeight}");
                        Helper.Log.Write(Helper.eLogType.Info, $"  Y buffer first 16 bytes: {BitConverter.ToString(yBuffer, 0, 16)}");
                        Helper.Log.Write(Helper.eLogType.Info, "  ⭐⭐⭐ USE LIBCAPI-VIDEO-CAPTURE.SO! ⭐⭐⭐");
                        return;
                    }
                    else if (result == -4)
                    {
                        Helper.Log.Write(Helper.eLogType.Warning, "  Result: -4 (DRM protected content)");
                        Helper.Log.Write(Helper.eLogType.Info, "  ⭐ CAPI API works - try on non-DRM content!");
                        return;
                    }
                    else
                    {
                        Helper.Log.Write(Helper.eLogType.Warning, $"  Result: {result}");
                    }

                    // Test 2: secvideo_api_capture_screen (with UI)
                    Helper.Log.Write(Helper.eLogType.Info, "  Test 2: secvideo_api_capture_screen() [with UI]");
                    result = CapiCaptureScreen(1920, 1080, ref testInfo);

                    if (result == 0)
                    {
                        Helper.Log.Write(Helper.eLogType.Info, "  ✅✅✅ CAPI CAPTURE SCREEN WORKS! ✅✅✅");
                        Helper.Log.Write(Helper.eLogType.Info, $"  Captured resolution: {testInfo.iWidth}x{testInfo.iHeight}");
                        Helper.Log.Write(Helper.eLogType.Info, "  ⭐⭐⭐ USE LIBCAPI-VIDEO-CAPTURE.SO! ⭐⭐⭐");
                        return;
                    }
                    else if (result == -4)
                    {
                        Helper.Log.Write(Helper.eLogType.Warning, "  Result: -4 (DRM protected content)");
                        Helper.Log.Write(Helper.eLogType.Info, "  ⭐ CAPI API works!");
                        return;
                    }
                    else
                    {
                        Helper.Log.Write(Helper.eLogType.Warning, $"  Result: {result}");
                    }

                    Helper.Log.Write(Helper.eLogType.Error, "  ✗ CAPI functions also blocked/failed");
                }
            }
            catch (DllNotFoundException)
            {
                Helper.Log.Write(Helper.eLogType.Debug, "  Library exists but cannot be loaded");
            }
            catch (Exception ex)
            {
                Helper.Log.Write(Helper.eLogType.Error, $"  Test failed: {ex.Message}");
            }
        }
    }
}
