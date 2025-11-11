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
            try
            {
                Helper.Log.Write(Helper.eLogType.Info, "");
                Helper.Log.Write(Helper.eLogType.Info, "=== Scanning for Alternative Capture Methods ===");

                // DO ALL TESTS FIRST (before heavy library scanning)
                // Each test wrapped in try-catch to ensure others continue if one crashes

                // 1. Try T7 API (might still exist) - TEST FIRST!
                try
                {
                    TryT7API();
                }
                catch (Exception ex)
                {
                    Helper.Log.Write(Helper.eLogType.Error, $"T7 API test crashed: {ex.Message}");
                }

                // 2. Test promising libraries using dynamic loading (safer than DllImport)
                try
                {
                    TestPromisingLibraries();
                }
                catch (Exception ex)
                {
                    Helper.Log.Write(Helper.eLogType.Error, $"Promising libraries test crashed: {ex.Message}");
                }

                // 4. Try framebuffer access
                try
                {
                    TryFrameBuffer();
                }
                catch (Exception ex)
                {
                    Helper.Log.Write(Helper.eLogType.Error, $"Framebuffer check crashed: {ex.Message}");
                }

                // 5. Check for debug/developer libraries (just checks existence)
                try
                {
                    CheckDeveloperLibraries();
                }
                catch (Exception ex)
                {
                    Helper.Log.Write(Helper.eLogType.Error, $"Developer libs check crashed: {ex.Message}");
                }

                // THEN do heavy scanning (can be slow/crash)
                Helper.Log.Write(Helper.eLogType.Info, "");
                Helper.Log.Write(Helper.eLogType.Info, "--- Quick tests complete, starting library scan ---");
                Helper.Log.Write(Helper.eLogType.Warning, "(This may take time and could disconnect WebSocket)");

                // 6. Search for all video/capture related libraries
                try
                {
                    SearchForLibraries();
                }
                catch (Exception ex)
                {
                    Helper.Log.Write(Helper.eLogType.Error, $"Library search crashed: {ex.Message}");
                }

                // 7. Try alternative Samsung libraries
                try
                {
                    TryAlternativeSamsungLibs();
                }
                catch (Exception ex)
                {
                    Helper.Log.Write(Helper.eLogType.Error, $"Alternative Samsung libs check crashed: {ex.Message}");
                }

                Helper.Log.Write(Helper.eLogType.Info, "=== End Alternative Scan ===");
                Helper.Log.Write(Helper.eLogType.Info, "");
            }
            catch (Exception topEx)
            {
                // Top-level catch for any escaped native exceptions
                Helper.Log.Write(Helper.eLogType.Error, $"Alternative scan FATAL ERROR: {topEx.Message}");
                Helper.Log.Write(Helper.eLogType.Error, $"Stack trace: {topEx.StackTrace}");
            }
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
                        try
                        {
                            // Wrap Directory.GetFiles in timeout protection (max 10 seconds)
                            string[] files = null;
                            var searchTask = System.Threading.Tasks.Task.Run(() =>
                            {
                                return Directory.GetFiles(path, pattern, SearchOption.TopDirectoryOnly);
                            });

                            if (searchTask.Wait(10000)) // 10 second timeout
                            {
                                files = searchTask.Result;
                            }
                            else
                            {
                                Helper.Log.Write(Helper.eLogType.Warning, $"  Timeout searching {path} for {pattern}");
                                continue;
                            }

                            foreach (string file in files)
                            {
                                Helper.Log.Write(Helper.eLogType.Debug, $"  Found: {file}");

                                // Wrap ProbeLibrary in timeout (max 5 seconds per library)
                                var probeTask = System.Threading.Tasks.Task.Run(() => ProbeLibrary(file));
                                if (!probeTask.Wait(5000))
                                {
                                    Helper.Log.Write(Helper.eLogType.Warning, $"  Timeout probing {file}");
                                }
                            }
                        }
                        catch (Exception patternEx)
                        {
                            Helper.Log.Write(Helper.eLogType.Debug, $"  Error with pattern {pattern} in {path}: {patternEx.Message}");
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

            try
            {
                foreach (string device in fbDevices)
                {
                    try
                    {
                        if (File.Exists(device))
                        {
                            Helper.Log.Write(Helper.eLogType.Info, $"  ✓ Found: {device}");
                            // NOTE: Not attempting to open - can cause native crashes on Samsung TVs
                            // FileStream on device files can block indefinitely or trigger kernel errors
                            Helper.Log.Write(Helper.eLogType.Debug, $"  (Device exists but not testing read access - can cause crashes)");
                        }
                    }
                    catch (Exception ex)
                    {
                        Helper.Log.Write(Helper.eLogType.Debug, $"  ✗ Error checking {device}: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                Helper.Log.Write(Helper.eLogType.Error, $"Framebuffer check failed: {ex.Message}");
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

        /// <summary>
        /// Test promising libraries found by previous scans using SAFE dynamic loading
        /// Uses dlopen/dlsym to avoid DllImport crashes
        /// </summary>
        private static void TestPromisingLibraries()
        {
            Helper.Log.Write(Helper.eLogType.Info, "--- Testing Promising Libraries (Dynamic Loading) ---");

            // List of libraries that had T7 API functions in previous scans
            string[] promisingLibs = new string[] {
                "/usr/lib/libdali-extension-tv-video-canvas.so.0.1.0",
                "/usr/lib/libdali-extension-tv-video-canvas.so.0",
                "/usr/lib/libdali-extension-tv-video-canvas.so"
            };

            string[] testFunctions = new string[] {
                "secvideo_api_capture_screen_video_only",
                "secvideo_api_capture_screen"
            };

            foreach (string libPath in promisingLibs)
            {
                if (!File.Exists(libPath))
                    continue;

                Helper.Log.Write(Helper.eLogType.Info, $"Testing {Path.GetFileName(libPath)}...");

                IntPtr handle = IntPtr.Zero;
                try
                {
                    // Try to load library with dlopen
                    handle = dlopen(libPath, RTLD_NOW);
                    if (handle == IntPtr.Zero)
                    {
                        string error = dlerror();
                        Helper.Log.Write(Helper.eLogType.Debug, $"  Cannot load: {error}");
                        continue;
                    }

                    Helper.Log.Write(Helper.eLogType.Info, $"  ✓ Library loaded successfully");

                    // Check if test functions exist
                    bool hasCaptureFunctions = false;
                    foreach (string funcName in testFunctions)
                    {
                        IntPtr funcPtr = dlsym(handle, funcName);
                        if (funcPtr != IntPtr.Zero)
                        {
                            Helper.Log.Write(Helper.eLogType.Info, $"  ✓ Found: {funcName} @ 0x{funcPtr.ToInt64():X}");
                            hasCaptureFunctions = true;
                        }
                    }

                    if (hasCaptureFunctions)
                    {
                        Helper.Log.Write(Helper.eLogType.Info, $"  ⭐ {Path.GetFileName(libPath)} HAS CAPTURE FUNCTIONS!");
                        Helper.Log.Write(Helper.eLogType.Info, "  (Not testing execution to avoid crashes - library exists and has symbols)");
                        Helper.Log.Write(Helper.eLogType.Info, "  Next step: Implement safe marshalling to actually call these functions");
                    }
                    else
                    {
                        Helper.Log.Write(Helper.eLogType.Debug, "  No capture functions found");
                    }

                    dlclose(handle);
                    handle = IntPtr.Zero;
                }
                catch (Exception ex)
                {
                    Helper.Log.Write(Helper.eLogType.Error, $"  Error testing {libPath}: {ex.Message}");
                }
                finally
                {
                    if (handle != IntPtr.Zero)
                    {
                        try { dlclose(handle); } catch { }
                    }
                }
            }
        }
    }
}
