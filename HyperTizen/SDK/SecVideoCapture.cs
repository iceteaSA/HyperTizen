using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using static HyperTizen.SDK.SecVideoCapture;

namespace HyperTizen.SDK
{
    public static unsafe class SecVideoCaptureT7 //for Tizen 7 and below
    {
        [DllImport("/usr/lib/libsec-video-capture.so.0", CallingConvention = CallingConvention.Cdecl, EntryPoint = "secvideo_api_capture_screen_unlock")]
        unsafe public static extern int CaptureScreenUnlock();
        [DllImport("/usr/lib/libsec-video-capture.so.0", CallingConvention = CallingConvention.Cdecl, EntryPoint = "secvideo_api_capture_screen")] //record with ui
        unsafe public static extern int CaptureScreen(int w, int h, ref Info_t pInfo);
        [DllImport("/usr/lib/libsec-video-capture.so.0", CallingConvention = CallingConvention.Cdecl, EntryPoint = "secvideo_api_capture_screen_video_only")] // without ui
        unsafe public static extern int CaptureScreenVideo(int w, int h, ref Info_t pInfo);
        [DllImport("/usr/lib/libsec-video-capture.so.0", CallingConvention = CallingConvention.Cdecl, EntryPoint = "secvideo_api_capture_screen_video_only_crop")] // cropped
        unsafe public static extern int CaptureScreenCrop(int w, int h, ref Info_t pInfo, int iCapture3DMode, int cropW, int cropH);
        [DllImport("/usr/lib/libsec-video-capture.so.0", CallingConvention = CallingConvention.Cdecl, EntryPoint = "secvideo_api_capture_screen_no_lock_no_copy")] // unknown
        unsafe public static extern int CaptureScreenNoLocknoCopy(int w, int h, ref Info_t pInfo);
        //Main Func Errors:
        //-1 "Input Pram is Wrong"
        //-1,-2,-3,-5...negative numbers without 4 "Failed scaler_capture"

        //Sub Func Errors
        //-2 Error: capture type %s, plane %s video only %d
        //-1 req size less or equal that crop size or non video for videoonly found
        //-1 Home Screen & yt crop size, capture lock, video info related
        //-4 Netflix/ Widevine Drm Stuff

    }

    public static unsafe class SecVideoCaptureT8 //for Tizen 8 and above
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
        private const int RTLD_GLOBAL = 256;

        // Structure for input parameters to getVideoMainYUV
        [StructLayout(LayoutKind.Sequential)]
        public unsafe struct InputParams
        {
            public long field0;           // local_dc (8 bytes)
            public long field1;           // uStack_d4 (8 bytes)
            public int field2;            // local_cc (set to 0xffff)
            public int field3;            // uStack_c8 (set to 0xffff)
            public byte field4;           // local_c4 (set to 1)
            public byte field5;
            public byte field6;
            public byte field7;
            public int field8;            // uStack_c0
            public int field9;            // uStack_bc
            // Additional fields based on stack layout
            public int bufferSize1;       // local_90 = 0x7e900
            public int bufferSize2;       // uStack_8c = 0x7e900
            public IntPtr pYBuffer;       // local_88 = ybuff
            public IntPtr pUVBuffer;      // local_84 = cbuff
        }

        // Structure for output parameters (80 bytes = 0x50)
        [StructLayout(LayoutKind.Sequential)]
        public unsafe struct OutputParams
        {
            public fixed byte data[80];   // auStack_b8 - 80 bytes initialized to 0
        }

        // Vtable method delegates
        // ARM uses Cdecl calling convention, not ThisCall
        // The 'this' pointer is passed as the first parameter in r0
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        public unsafe delegate int LockDelegate(IVideoCapture* @this, int param1, int param2);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        public unsafe delegate int GetVideoMainYUVDelegate(IVideoCapture* @this, InputParams* input, OutputParams* output);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        public unsafe delegate int UnlockDelegate(IVideoCapture* @this, int param1, int param2);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        public unsafe delegate int CaptureScreenDelegate(IVideoCapture* @this, int w, int h, ref Info_t pInfo);

        public unsafe struct IVideoCapture
        {
            public IntPtr* vtable;
        }

        private static IVideoCapture* instance;
        private static LockDelegate lockFunc;
        private static GetVideoMainYUVDelegate getVideoMainYUV;
        private static UnlockDelegate unlockFunc;
        private static CaptureScreenDelegate captureScreen;
        private static bool isInitialized = false;
        private static bool useNewApi = true; // Try new API first

        // getInstance - try from both libraries
        [DllImport("/usr/lib/libvideo-capture.so.0.1.0", CallingConvention = CallingConvention.Cdecl, EntryPoint = "_Z11getInstancev")]
        private static extern IVideoCapture* GetInstanceMangled();

        [DllImport("/usr/lib/libvideo-capture-impl-sec.so", CallingConvention = CallingConvention.Cdecl, EntryPoint = "getInstance")]
        private static extern IVideoCapture* GetInstanceImplSec();

        [DllImport("/usr/lib/libvideo-capture-impl-sec.so", CallingConvention = CallingConvention.Cdecl, EntryPoint = "_Z11getInstancev")]
        private static extern IVideoCapture* GetInstanceImplSecMangled();

        // Direct function imports - try from multiple libraries
        // Try 1: From libvideo-capture.so.0.1.0 (the library that successfully loads)
        [DllImport("/usr/lib/libvideo-capture.so.0.1.0", CallingConvention = CallingConvention.Cdecl, EntryPoint = "getVideoMainYUV")]
        private static extern int GetVideoMainYUVDirectMain(IVideoCapture* instance, ref InputParams input, byte* output);

        [DllImport("/usr/lib/libvideo-capture.so.0.1.0", CallingConvention = CallingConvention.Cdecl, EntryPoint = "getVideoPostYUV")]
        private static extern int GetVideoPostYUVDirectMain(IVideoCapture* instance, ref InputParams input, byte* output);

        // Try 2: From libvideo-capture-impl-sec.so (if it exists)
        [DllImport("/usr/lib/libvideo-capture-impl-sec.so", CallingConvention = CallingConvention.Cdecl, EntryPoint = "getVideoMainYUV")]
        private static extern int GetVideoMainYUVDirectImpl(IVideoCapture* instance, ref InputParams input, byte* output);

        [DllImport("/usr/lib/libvideo-capture-impl-sec.so", CallingConvention = CallingConvention.Cdecl, EntryPoint = "getVideoPostYUV")]
        private static extern int GetVideoPostYUVDirectImpl(IVideoCapture* instance, ref InputParams input, byte* output);

        private static IVideoCapture* ProbeForGetInstance()
        {
            // Try to load the library dynamically and probe for getInstance
            IntPtr handle = dlopen("/usr/lib/libvideo-capture.so.0.1.0", RTLD_NOW);
            if (handle == IntPtr.Zero)
            {
                string error = dlerror();
                Helper.Log.Write(Helper.eLogType.Error, $"T8 SDK: dlopen failed: {error}");
                return null;
            }

            Helper.Log.Write(Helper.eLogType.Info, "T8 SDK: Library loaded with dlopen, probing for getInstance...");

            // Try different getInstance symbol names
            string[] symbolsToTry = new string[] {
                "getInstance",                          // Plain C name
                "_Z11getInstancev",                     // C++ mangled, no namespace
                "_ZN13IVideoCapture11getInstanceEv",    // C++ with IVideoCapture namespace
                "_ZN13IVideoCapture11getInstanceERv",   // Alternative mangling
                "_ZL11getInstancev",                    // Static function
                "IVideoCapture_getInstance",            // Alternative naming
                "_getInstance"                           // Underscore prefix
            };

            IntPtr funcPtr = IntPtr.Zero;
            string workingSymbol = null;

            foreach (string symbol in symbolsToTry)
            {
                funcPtr = dlsym(handle, symbol);
                if (funcPtr != IntPtr.Zero)
                {
                    workingSymbol = symbol;
                    Helper.Log.Write(Helper.eLogType.Info, $"T8 SDK: ✓ Found getInstance at symbol '{symbol}'!");
                    break;
                }
            }

            if (funcPtr == IntPtr.Zero)
            {
                Helper.Log.Write(Helper.eLogType.Error, "T8 SDK: getInstance symbol not found with any name variant");
                dlclose(handle);
                return null;
            }

            // Call the function pointer
            try
            {
                // Cast function pointer to delegate
                var getInstanceFunc = (GetInstanceDelegate)Marshal.GetDelegateForFunctionPointer(funcPtr, typeof(GetInstanceDelegate));
                IVideoCapture* instance = getInstanceFunc();

                Helper.Log.Write(Helper.eLogType.Info, $"T8 SDK: getInstance() called successfully via dlsym! Symbol: {workingSymbol}");
                return instance;
            }
            catch (Exception ex)
            {
                Helper.Log.Write(Helper.eLogType.Error, $"T8 SDK: getInstance() call failed: {ex.Message}");
                dlclose(handle);
                return null;
            }
        }

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate IVideoCapture* GetInstanceDelegate();

        private static string searchLogPath = "/tmp/hypertizen_search.log";

        private static void SearchForLibraries()
        {
            // Write to file instead of memory to avoid crashes
            try
            {
                System.IO.File.WriteAllText(searchLogPath, "=== EXHAUSTIVE Library Search - Starting ===\n");
                System.IO.File.AppendAllText(searchLogPath, $"Started at: {DateTime.Now:yyyy-MM-dd HH:mm:ss}\n\n");
            }
            catch (Exception ex)
            {
                Helper.Log.Write(Helper.eLogType.Error, $"Failed to create search log file: {ex.Message}");
                return;
            }

            Helper.Log.Write(Helper.eLogType.Info, $"Search results being written to: {searchLogPath}");
            Helper.Log.Write(Helper.eLogType.Info, "Connect to WebSocket to view full results");

            // EXHAUSTIVE search - write directly to file
            string[] searchCommands = new string[] {
                // === Direct ls checks ===
                "ls -lh /usr/lib/libvideo-capture* 2>/dev/null",
                "ls -lh /usr/lib/libcapi*video*capture* 2>/dev/null",
                "ls -lh /usr/lib/*capture*.so* 2>/dev/null",
                "ls -lh /usr/lib/*video*.so* 2>/dev/null",

                // === Find searches with maxdepth ===
                "find /usr/lib -maxdepth 3 -name '*video*' -o -name '*capture*' 2>/dev/null",
                "find /opt -maxdepth 4 -name '*video*capture*' 2>/dev/null",
                "find /lib -maxdepth 2 -name '*video*' -o -name '*capture*' 2>/dev/null",

                // === Check vendor/system paths ===
                "test -d /usr/lib64 && find /usr/lib64 -maxdepth 2 -name '*video*' 2>/dev/null || echo '/usr/lib64 not present'",
                "test -d /vendor && find /vendor -maxdepth 3 -name '*video*capture*' 2>/dev/null || echo '/vendor not present'",
                "test -d /system && find /system -maxdepth 3 -name '*video*capture*' 2>/dev/null || echo '/system not present'",
                "test -d /usr/vendor && find /usr/vendor -maxdepth 3 -name '*video*capture*' 2>/dev/null || echo '/usr/vendor not present'",

                // === System information ===
                "ldconfig -p 2>/dev/null | grep -i video",
                "ldconfig -p 2>/dev/null | grep -i capture",
                "cat /proc/self/maps 2>/dev/null | grep -i video",
                "cat /proc/self/maps 2>/dev/null | grep '\\.so'",

                // === Library dependencies and symbols ===
                "ldd /usr/lib/libvideo-capture.so.0.1.0 2>/dev/null",
                "readelf -d /usr/lib/libvideo-capture.so.0.1.0 2>/dev/null | grep NEEDED",
                "nm -D /usr/lib/libvideo-capture.so.0.1.0 2>/dev/null | grep -i instance | head -20",
                "nm -D /usr/lib/libvideo-capture.so.0.1.0 2>/dev/null | grep -i video | head -20",
                "nm -D /usr/lib/libvideo-capture.so.0.1.0 2>/dev/null | grep -i capture | head -20",

                // === Package information ===
                "rpm -qf /usr/lib/libvideo-capture.so.0.1.0 2>/dev/null || echo 'rpm not available'",
                "dpkg -S /usr/lib/libvideo-capture.so.0.1.0 2>/dev/null || echo 'dpkg not available'",

                // === File type and strings ===
                "file /usr/lib/libvideo-capture.so.0.1.0 2>/dev/null",
                "strings /usr/lib/libvideo-capture.so.0.1.0 2>/dev/null | grep -i 'getInstance' | head -10",
                "strings /usr/lib/libvideo-capture.so.0.1.0 2>/dev/null | grep -i 'VideoCapture' | head -15"
            };

            int searchNum = 0;
            int totalSearches = searchCommands.Length;

            foreach (string cmd in searchCommands)
            {
                searchNum++;
                try
                {
                    // Log progress to console (short message)
                    if (searchNum % 5 == 0)
                    {
                        Helper.Log.Write(Helper.eLogType.Info, $"Search progress: {searchNum}/{totalSearches}");
                    }

                    // Write to file
                    System.IO.File.AppendAllText(searchLogPath, $"\n[{searchNum}/{totalSearches}] Command: {cmd}\n");
                    System.IO.File.AppendAllText(searchLogPath, new string('-', 80) + "\n");

                    var process = new System.Diagnostics.Process();
                    process.StartInfo.FileName = "/bin/sh";
                    process.StartInfo.Arguments = $"-c \"{cmd}\"";
                    process.StartInfo.RedirectStandardOutput = true;
                    process.StartInfo.RedirectStandardError = true;
                    process.StartInfo.UseShellExecute = false;
                    process.Start();

                    // 3 second timeout
                    if (process.WaitForExit(3000))
                    {
                        string output = process.StandardOutput.ReadToEnd();
                        if (!string.IsNullOrWhiteSpace(output))
                        {
                            System.IO.File.AppendAllText(searchLogPath, output + "\n");
                        }
                        else
                        {
                            System.IO.File.AppendAllText(searchLogPath, "(no output)\n");
                        }
                    }
                    else
                    {
                        try { process.Kill(); } catch { }
                        System.IO.File.AppendAllText(searchLogPath, "(timed out after 3s)\n");
                    }

                    // 500ms delay
                    System.Threading.Thread.Sleep(500);

                    // Notify WebSocket clients of progress
                    if (searchNum % 3 == 0)
                    {
                        Helper.Log.BroadcastSearchProgress(searchNum, totalSearches, searchLogPath);
                    }
                }
                catch (Exception ex)
                {
                    System.IO.File.AppendAllText(searchLogPath, $"FAILED: {ex.Message}\n");
                }
            }

            System.IO.File.AppendAllText(searchLogPath, $"\n=== End Exhaustive Search ===\n");
            System.IO.File.AppendAllText(searchLogPath, $"Completed: {DateTime.Now:yyyy-MM-dd HH:mm:ss}\n");
            System.IO.File.AppendAllText(searchLogPath, $"Total searches: {searchNum}\n");

            Helper.Log.Write(Helper.eLogType.Info, $"Search complete! Results in: {searchLogPath}");
            Helper.Log.BroadcastSearchComplete(searchLogPath);
        }

        private static IVideoCapture* TryGetInstance()
        {
            IVideoCapture* inst = null;

            // Try 1: Dynamic symbol probing (most flexible, tries all symbol variants)
            // This is the best approach as it tests multiple symbol name variations
            try
            {
                Helper.Log.Write(Helper.eLogType.Info, "T8 SDK: Attempting dynamic symbol probing...");
                inst = ProbeForGetInstance();
                if (inst != null)
                {
                    Helper.Log.Write(Helper.eLogType.Info, "T8 SDK: ✓ Success with dynamic probing!");
                    return inst;
                }
            }
            catch (Exception ex)
            {
                Helper.Log.Write(Helper.eLogType.Debug, $"T8 SDK: Dynamic probing failed: {ex.Message}");
            }

            // Try 2: Standard P/Invoke with mangled name
            try
            {
                Helper.Log.Write(Helper.eLogType.Info, "T8 SDK: Trying _Z11getInstancev via P/Invoke...");
                inst = GetInstanceMangled();
                if (inst != null)
                {
                    Helper.Log.Write(Helper.eLogType.Info, "T8 SDK: ✓ Success with P/Invoke mangled name!");
                    return inst;
                }
            }
            catch (Exception ex)
            {
                Helper.Log.Write(Helper.eLogType.Debug, $"T8 SDK: P/Invoke failed: {ex.Message}");
            }

            Helper.Log.Write(Helper.eLogType.Error, "T8 SDK: All getInstance attempts failed!");
            Helper.Log.Write(Helper.eLogType.Error, "T8 SDK: The getInstance symbol may not exist, or requires different calling convention");
            return null;
        }

        public static void Init()
        {
            if (isInitialized)
            {
                Helper.Log.Write(Helper.eLogType.Info, "SecVideoCaptureT8 already initialized, skipping");
                return;
            }

            // Library search removed - was causing app crashes due to process spawning
            // Using known library paths from previous successful runs
            Helper.Log.Write(Helper.eLogType.Info, "Skipping library search - using known library paths");
            Helper.Log.Write(Helper.eLogType.Info, "Known: /usr/lib/libvideo-capture.so.0.1.0 exists");

            // Probe the main library (libvideo-capture.so.0.1.0) for exported functions
            try
            {
                Helper.Log.Write(Helper.eLogType.Info, "=== Probing libvideo-capture.so.0.1.0 for exported functions ===");
                IntPtr mainHandle = dlopen("/usr/lib/libvideo-capture.so.0.1.0", RTLD_NOW | RTLD_GLOBAL);
                if (mainHandle == IntPtr.Zero)
                {
                    string error = dlerror();
                    Helper.Log.Write(Helper.eLogType.Warning, $"  ✗ Cannot load with dlopen: {error}");
                }
                else
                {
                    Helper.Log.Write(Helper.eLogType.Info, "  ✓ Loaded successfully with dlopen!");

                    // Check for exported functions
                    string[] functionsToCheck = new string[] {
                        "getVideoMainYUV",
                        "getVideoPostYUV",
                        "Lock",
                        "Unlock",
                        "getInstance",
                        "_Z11getInstancev",
                        "_ZN13IVideoCapture11getInstanceEv"
                    };

                    foreach (string func in functionsToCheck)
                    {
                        try
                        {
                            IntPtr funcPtr = dlsym(mainHandle, func);
                            if (funcPtr != IntPtr.Zero)
                            {
                                Helper.Log.Write(Helper.eLogType.Info, $"    ✓ {func}: FOUND at 0x{funcPtr.ToInt64():X}");
                            }
                            else
                            {
                                Helper.Log.Write(Helper.eLogType.Debug, $"    ✗ {func}: NOT FOUND");
                            }
                        }
                        catch (Exception ex)
                        {
                            Helper.Log.Write(Helper.eLogType.Debug, $"    ✗ {func}: dlsym failed - {ex.Message}");
                        }
                    }

                    // Don't close the handle yet - we might need it for getInstance
                    // dlclose(mainHandle);
                }
                Helper.Log.Write(Helper.eLogType.Info, "=== End Probing ===");
            }
            catch (Exception ex)
            {
                Helper.Log.Write(Helper.eLogType.Warning, $"Symbol probing failed: {ex.Message}");
            }

            // Check if main library file exists
            if (!System.IO.File.Exists("/usr/lib/libvideo-capture.so.0.1.0"))
            {
                Helper.Log.Write(Helper.eLogType.Error, "T8 SDK: libvideo-capture.so.0.1.0 NOT FOUND!");
                throw new System.IO.FileNotFoundException("Tizen 8 SDK library not found");
            }

            Helper.Log.Write(Helper.eLogType.Info, "T8 SDK: Main library found, trying multiple getInstance methods...");

            // Wrap GetInstance in timeout protection
            IVideoCapture* tempInstance = null;
            bool getInstanceCompleted = false;
            Exception getInstanceError = null;

            var getInstanceTask = System.Threading.Tasks.Task.Run(() =>
            {
                try
                {
                    tempInstance = TryGetInstance();
                    getInstanceCompleted = true;
                    if (tempInstance != null)
                    {
                        Helper.Log.Write(Helper.eLogType.Info, "T8 SDK: GetInstance() returned successfully!");
                    }
                }
                catch (Exception ex)
                {
                    getInstanceError = ex;
                    Helper.Log.Write(Helper.eLogType.Error, $"T8 SDK: GetInstance() threw exception: {ex.Message}");
                }
            });

            // Wait max 5 seconds for GetInstance
            if (!getInstanceTask.Wait(5000))
            {
                Helper.Log.Write(Helper.eLogType.Error, "T8 SDK: GetInstance() TIMEOUT after 5 seconds!");
                throw new TimeoutException("GetInstance() hung - SDK incompatible with this TV model");
            }

            if (getInstanceError != null)
            {
                throw getInstanceError;
            }

            if (!getInstanceCompleted)
            {
                Helper.Log.Write(Helper.eLogType.Error, "T8 SDK: GetInstance() did not complete!");
                throw new InvalidOperationException("GetInstance() failed silently");
            }

            instance = tempInstance;

            if (instance == null)
            {
                Helper.Log.Write(Helper.eLogType.Error, "T8 SDK: GetInstance() returned NULL!");
                throw new NullReferenceException("GetInstance() returned null - SDK not available");
            }

            Helper.Log.Write(Helper.eLogType.Info, "T8 SDK: Instance OK, checking vtable...");

            if (instance->vtable == null)
            {
                Helper.Log.Write(Helper.eLogType.Error, "T8 SDK: vtable is NULL!");
                throw new NullReferenceException("vtable is null - SDK initialization failed");
            }

            // Set up vtable method pointers based on Discord findings
            // vtable[3] (offset 0xc): getVideoMainYUV
            // vtable[13] (offset 0x34): Lock function
            // vtable[14] (offset 0x38): Unlock function
            const int GetVideoMainYUVIndex = 3;
            const int LockIndex = 13;
            const int UnlockIndex = 14;

            Helper.Log.Write(Helper.eLogType.Info, $"T8 SDK: Setting up vtable methods...");

            // Get getVideoMainYUV function pointer
            IntPtr fpGetVideo = instance->vtable[GetVideoMainYUVIndex];
            if (fpGetVideo == IntPtr.Zero)
            {
                Helper.Log.Write(Helper.eLogType.Error, $"T8 SDK: getVideoMainYUV at vtable[{GetVideoMainYUVIndex}] is NULL!");
                throw new NullReferenceException($"Function pointer at vtable[{GetVideoMainYUVIndex}] is null");
            }
            getVideoMainYUV = (GetVideoMainYUVDelegate)Marshal.GetDelegateForFunctionPointer(fpGetVideo, typeof(GetVideoMainYUVDelegate));
            Helper.Log.Write(Helper.eLogType.Info, $"T8 SDK: getVideoMainYUV initialized at vtable[{GetVideoMainYUVIndex}]");

            // Get lock function pointer
            IntPtr fpLock = instance->vtable[LockIndex];
            if (fpLock == IntPtr.Zero)
            {
                Helper.Log.Write(Helper.eLogType.Error, $"T8 SDK: Lock function at vtable[{LockIndex}] is NULL!");
                throw new NullReferenceException($"Function pointer at vtable[{LockIndex}] is null");
            }
            lockFunc = (LockDelegate)Marshal.GetDelegateForFunctionPointer(fpLock, typeof(LockDelegate));
            Helper.Log.Write(Helper.eLogType.Info, $"T8 SDK: Lock function initialized at vtable[{LockIndex}]");

            // Get unlock function pointer
            IntPtr fpUnlock = instance->vtable[UnlockIndex];
            if (fpUnlock == IntPtr.Zero)
            {
                Helper.Log.Write(Helper.eLogType.Error, $"T8 SDK: Unlock function at vtable[{UnlockIndex}] is NULL!");
                throw new NullReferenceException($"Function pointer at vtable[{UnlockIndex}] is null");
            }
            unlockFunc = (UnlockDelegate)Marshal.GetDelegateForFunctionPointer(fpUnlock, typeof(UnlockDelegate));
            Helper.Log.Write(Helper.eLogType.Info, $"T8 SDK: Unlock function initialized at vtable[{UnlockIndex}]");

            // Also try to get the old CaptureScreen method as fallback
            IntPtr fpCapture = instance->vtable[3];
            if (fpCapture != IntPtr.Zero)
            {
                captureScreen = (CaptureScreenDelegate)Marshal.GetDelegateForFunctionPointer(fpCapture, typeof(CaptureScreenDelegate));
                Helper.Log.Write(Helper.eLogType.Info, "T8 SDK: Old CaptureScreen method also available as fallback");
            }

            isInitialized = true;
            Helper.Log.Write(Helper.eLogType.Info, "T8 SDK: Initialized successfully with new API!");

            // Dump vtable for diagnostics
            DumpVTableInfo();
        }

        /// <summary>
        /// Test function to verify screen capture works with the VTable API
        /// This should be called during diagnostic mode to validate the implementation
        /// </summary>
        public static bool TestCapture()
        {
            if (!isInitialized)
            {
                Helper.Log.Write(Helper.eLogType.Error, "TestCapture: SecVideoCaptureT8 not initialized");
                return false;
            }

            Helper.Log.Write(Helper.eLogType.Info, "=== Testing Screen Capture ===");

            try
            {
                // Allocate test buffers (same size as reference code)
                int bufferSize = 0x7e900; // 518,400 bytes (from GetCaptureFromTZ.c)
                byte[] yBuffer = new byte[bufferSize];
                byte[] uvBuffer = new byte[bufferSize];

                fixed (byte* pY = yBuffer, pUV = uvBuffer)
                {
                    // Create Info_t structure
                    Info_t testInfo = new Info_t();
                    testInfo.pImageY = (IntPtr)pY;
                    testInfo.pImageUV = (IntPtr)pUV;
                    testInfo.iGivenBufferSize1 = bufferSize;
                    testInfo.iGivenBufferSize2 = bufferSize;

                    // Test 1: VTable API (Lock -> getVideoMainYUV -> Unlock)
                    Helper.Log.Write(Helper.eLogType.Info, "Test 1: VTable API with Lock/Unlock");
                    int result = CaptureScreenNewApi(1920, 1080, ref testInfo);

                    if (result == 0 || result == 4)
                    {
                        Helper.Log.Write(Helper.eLogType.Info, $"✓ VTable API SUCCESS! Result: {result}");
                        Helper.Log.Write(Helper.eLogType.Info, $"  Captured resolution: {testInfo.iWidth}x{testInfo.iHeight}");
                        Helper.Log.Write(Helper.eLogType.Info, $"  Y buffer first 16 bytes: {BitConverter.ToString(yBuffer, 0, 16)}");
                        Helper.Log.Write(Helper.eLogType.Info, $"  UV buffer first 16 bytes: {BitConverter.ToString(uvBuffer, 0, 16)}");

                        // Check if we got actual data (not all zeros)
                        bool hasData = false;
                        for (int i = 0; i < 1000; i++)
                        {
                            if (yBuffer[i] != 0 || uvBuffer[i] != 0)
                            {
                                hasData = true;
                                break;
                            }
                        }

                        if (hasData)
                        {
                            Helper.Log.Write(Helper.eLogType.Info, "✓ Buffers contain non-zero data - capture appears valid!");
                        }
                        else
                        {
                            Helper.Log.Write(Helper.eLogType.Warning, "⚠ Buffers are all zeros - may need investigation");
                        }

                        Helper.Log.Write(Helper.eLogType.Info, "=== Screen Capture Test PASSED ===");
                        return true;
                    }
                    else if (result == -4)
                    {
                        Helper.Log.Write(Helper.eLogType.Warning, "✗ VTable API returned -4 (DRM content)");
                        Helper.Log.Write(Helper.eLogType.Info, "=== Screen Capture Test: DRM protected content ===");
                        return false;
                    }
                    else
                    {
                        Helper.Log.Write(Helper.eLogType.Error, $"✗ VTable API FAILED with code: {result}");
                        Helper.Log.Write(Helper.eLogType.Info, "=== Screen Capture Test FAILED ===");
                        return false;
                    }
                }
            }
            catch (Exception ex)
            {
                Helper.Log.Write(Helper.eLogType.Error, $"TestCapture exception: {ex.Message}");
                Helper.Log.Write(Helper.eLogType.Error, $"Stack trace: {ex.StackTrace}");
                Helper.Log.Write(Helper.eLogType.Info, "=== Screen Capture Test FAILED (Exception) ===");
                return false;
            }
        }

        public static int CaptureScreen(int w, int h, ref Info_t pInfo)
        {
            if (!isInitialized)
            {
                Helper.Log.Write(Helper.eLogType.Error, "SecVideoCaptureT8 not initialized");
                return -99;
            }

            // Try 1: Direct function call (based on exports)
            try
            {
                Helper.Log.Write(Helper.eLogType.Info, "T8 SDK: Trying direct function call to getVideoMainYUV...");
                int result = CaptureScreenDirect(w, h, ref pInfo);
                if (result == 0 || result == 4)
                {
                    Helper.Log.Write(Helper.eLogType.Info, "T8 SDK: Direct function call succeeded!");
                    return result;
                }
                Helper.Log.Write(Helper.eLogType.Warning, $"T8 SDK: Direct function call returned {result}, trying vtable...");
            }
            catch (Exception ex)
            {
                Helper.Log.Write(Helper.eLogType.Warning, $"T8 SDK: Direct function call failed: {ex.Message}, trying vtable...");
            }

            // Try 2: VTable API (getVideoMainYUV with lock/unlock)
            if (useNewApi && getVideoMainYUV != null && lockFunc != null && unlockFunc != null)
            {
                int result = CaptureScreenNewApi(w, h, ref pInfo);

                // If vtable API fails, try to fall back to old API
                if (result != 0 && captureScreen != null)
                {
                    Helper.Log.Write(Helper.eLogType.Warning, $"T8 SDK: VTable API failed with code {result}, trying old API...");
                    useNewApi = false;
                    return captureScreen(instance, w, h, ref pInfo);
                }

                return result;
            }
            // Try 3: Fall back to old API if available
            else if (captureScreen != null)
            {
                return captureScreen(instance, w, h, ref pInfo);
            }
            else
            {
                Helper.Log.Write(Helper.eLogType.Error, "T8 SDK: No capture methods available");
                return -99;
            }
        }

        private static int CaptureScreenDirect(int w, int h, ref Info_t pInfo)
        {
            // Prepare input parameters (same as vtable method)
            InputParams input = new InputParams();
            byte[] outputBuffer = new byte[80]; // 0x50 bytes

            // Initialize input structure based on decompiled code
            input.field0 = 0;
            input.field1 = 0;
            input.field2 = 0xffff;
            input.field3 = 0xffff;
            input.field4 = 1;
            input.field5 = 0;
            input.field6 = 0;
            input.field7 = 0;
            input.field8 = 0;
            input.field9 = 0;
            input.bufferSize1 = pInfo.iGivenBufferSize1;  // 0x7e900 typically
            input.bufferSize2 = pInfo.iGivenBufferSize2;  // 0x7e900 typically
            input.pYBuffer = pInfo.pImageY;
            input.pUVBuffer = pInfo.pImageUV;

            // Call getVideoMainYUV from libvideo-capture.so.0.1.0
            // (libvideo-capture-impl-sec.so does not exist on this TV)
            try
            {
                Helper.Log.Write(Helper.eLogType.Info, "T8 SDK: Calling getVideoMainYUV directly...");
                fixed (byte* pOutput = outputBuffer)
                {
                    int result = GetVideoMainYUVDirectMain(instance, ref input, pOutput);
                    Helper.Log.Write(Helper.eLogType.Debug, $"T8 SDK: getVideoMainYUV returned: {result}");

                    if (result == 0 || result == 4)
                    {
                        return ParseDirectCaptureResult(result, pOutput, ref pInfo, "libvideo-capture.so.0.1.0");
                    }
                    else if (result == -4)
                    {
                        Helper.Log.Write(Helper.eLogType.Warning, "T8 SDK: DRM content detected (result: -4)");
                        return result;
                    }
                    else
                    {
                        Helper.Log.Write(Helper.eLogType.Warning, $"T8 SDK: getVideoMainYUV returned unexpected code: {result}");
                        throw new Exception($"Direct function call returned {result}");
                    }
                }
            }
            catch (Exception ex)
            {
                Helper.Log.Write(Helper.eLogType.Error, $"T8 SDK: Direct function call failed: {ex.Message}");
                throw; // Re-throw to let caller try vtable method
            }
        }

        private static int ParseDirectCaptureResult(int result, byte* pOutput, ref Info_t pInfo, string source)
        {
            // Parse output - width and height should be at offsets 0 and 4
            int* pInts = (int*)pOutput;
            int outWidth = pInts[0];
            int outHeight = pInts[1];

            if (outWidth > 960 && outHeight > 540)
            {
                pInfo.iWidth = outWidth;
                pInfo.iHeight = outHeight;
                Helper.Log.Write(Helper.eLogType.Info, $"T8 SDK: Direct capture from {source} successful! Resolution: {outWidth}x{outHeight}");
            }
            else
            {
                Helper.Log.Write(Helper.eLogType.Warning, $"T8 SDK: Direct capture from {source} dimensions invalid: {outWidth}x{outHeight}");
            }

            return result;
        }

        public static void EnableNewApi()
        {
            useNewApi = true;
            Helper.Log.Write(Helper.eLogType.Info, "T8 SDK: Enabled new API (getVideoMainYUV with lock/unlock)");
        }

        public static void DisableNewApi()
        {
            useNewApi = false;
            Helper.Log.Write(Helper.eLogType.Info, "T8 SDK: Disabled new API, using old CaptureScreen method");
        }

        // Diagnostic method to dump vtable information
        public static void DumpVTableInfo()
        {
            if (!isInitialized || instance == null || instance->vtable == null)
            {
                Helper.Log.Write(Helper.eLogType.Error, "T8 SDK: Cannot dump vtable - not initialized");
                return;
            }

            Helper.Log.Write(Helper.eLogType.Info, "=== T8 SDK VTable Dump ===");
            Helper.Log.Write(Helper.eLogType.Info, $"Instance address: 0x{((long)instance):X}");
            Helper.Log.Write(Helper.eLogType.Info, $"VTable address: 0x{((long)instance->vtable):X}");

            // Dump first 20 vtable entries
            for (int i = 0; i < 20; i++)
            {
                try
                {
                    IntPtr fp = instance->vtable[i];
                    Helper.Log.Write(Helper.eLogType.Info, $"  vtable[{i,2}] (offset 0x{i * IntPtr.Size:X2}): 0x{fp.ToInt64():X}");
                }
                catch (Exception ex)
                {
                    Helper.Log.Write(Helper.eLogType.Warning, $"  vtable[{i,2}]: Error reading - {ex.Message}");
                    break;
                }
            }

            Helper.Log.Write(Helper.eLogType.Info, "=== End VTable Dump ===");
        }

        private static int CaptureScreenNewApi(int w, int h, ref Info_t pInfo)
        {
            try
            {
                // Step 1: Prepare input and output parameters FIRST
                InputParams input = new InputParams();
                OutputParams output = new OutputParams();

                // Initialize input structure based on decompiled code
                input.field0 = 0;
                input.field1 = 0;
                input.field2 = 0xffff;
                input.field3 = 0xffff;
                input.field4 = 1;
                input.field5 = 0;
                input.field6 = 0;
                input.field7 = 0;
                input.field8 = 0;
                input.field9 = 0;
                input.bufferSize1 = pInfo.iGivenBufferSize1;  // 0x7e900 typically
                input.bufferSize2 = pInfo.iGivenBufferSize2;  // 0x7e900 typically
                input.pYBuffer = pInfo.pImageY;
                input.pUVBuffer = pInfo.pImageUV;

                Helper.Log.Write(Helper.eLogType.Debug, $"T8 SDK: Input params - bufSize1=0x{input.bufferSize1:X}, bufSize2=0x{input.bufferSize2:X}");
                Helper.Log.Write(Helper.eLogType.Debug, $"T8 SDK: Input params - field2=0x{input.field2:X}, field3=0x{input.field3:X}, field4={input.field4}");
                Helper.Log.Write(Helper.eLogType.Debug, $"T8 SDK: Input params - pY=0x{input.pYBuffer.ToInt64():X}, pUV=0x{input.pUVBuffer.ToInt64():X}");
                Helper.Log.Write(Helper.eLogType.Debug, $"T8 SDK: Instance pointer=0x{((IntPtr)instance).ToInt64():X}");

                // Step 2: Try WITHOUT Lock first (simpler test)
                Helper.Log.Write(Helper.eLogType.Info, "T8 SDK: Attempting capture WITHOUT Lock/Unlock...");
                int captureResult = getVideoMainYUV(instance, &input, &output);
                Helper.Log.Write(Helper.eLogType.Debug, $"T8 SDK: getVideoMainYUV returned: {captureResult}");

                if (captureResult == 0 || captureResult == 4)
                {
                    Helper.Log.Write(Helper.eLogType.Info, "T8 SDK: getVideoMainYUV succeeded WITHOUT Lock!");
                }
                else
                {
                    // If no-lock fails, try WITH lock
                    Helper.Log.Write(Helper.eLogType.Warning, $"T8 SDK: No-lock capture failed ({captureResult}), trying WITH Lock...");

                    // Reset output
                    output = new OutputParams();

                    Helper.Log.Write(Helper.eLogType.Debug, "T8 SDK: Calling lock function...");
                    int lockResult = lockFunc(instance, 1, 0);
                    Helper.Log.Write(Helper.eLogType.Debug, $"T8 SDK: Lock returned: {lockResult}");

                    Helper.Log.Write(Helper.eLogType.Debug, "T8 SDK: Calling getVideoMainYUV...");
                    captureResult = getVideoMainYUV(instance, &input, &output);
                    Helper.Log.Write(Helper.eLogType.Debug, $"T8 SDK: getVideoMainYUV returned: {captureResult}");

                    Helper.Log.Write(Helper.eLogType.Debug, "T8 SDK: Calling unlock function...");
                    int unlockResult = unlockFunc(instance, 1, 0);
                    Helper.Log.Write(Helper.eLogType.Debug, $"T8 SDK: Unlock returned: {unlockResult}");
                }

                if (captureResult != 0 && captureResult != 4 && captureResult != -4)
                {
                    Helper.Log.Write(Helper.eLogType.Error, $"T8 SDK: getVideoMainYUV failed with code {captureResult}");
                    return captureResult;
                }

                // Step 5: Parse output parameters
                // The output structure should contain width/height at specific offsets
                // Based on the decompiled code: local_54 and uStack_50 contain dimensions
                // Note: output.data is already a fixed buffer, so we can access it directly
                int* pInts = (int*)output.data;
                int outWidth = pInts[0];
                int outHeight = pInts[1];

                if (outWidth > 960 && outHeight > 540)  // 0x3bf=959, 0x21b=539 from decompiled code
                {
                    pInfo.iWidth = outWidth;
                    pInfo.iHeight = outHeight;
                    Helper.Log.Write(Helper.eLogType.Info, $"T8 SDK: Capture successful! Resolution: {outWidth}x{outHeight}");
                }
                else
                {
                    Helper.Log.Write(Helper.eLogType.Warning, $"T8 SDK: Output dimensions seem invalid: {outWidth}x{outHeight}");
                    // Still return success code if getVideoMainYUV succeeded
                }

                return captureResult;
            }
            catch (Exception ex)
            {
                Helper.Log.Write(Helper.eLogType.Error, $"T8 SDK: Exception in CaptureScreenNewApi: {ex.Message}");
                return -99;
            }
        }

    }

    public static class SecVideoCapture
    {
        private static bool useT7Fallback = false;
        private static bool initAttempted = false;

        public static void SetT7Fallback()
        {
            useT7Fallback = true;
            Helper.Log.Write(Helper.eLogType.Warning, "T8 SDK init failed - using T7 fallback API");
        }

        public static unsafe int CaptureScreen(int w, int h, ref Info_t pInfo)
        {
            // Use T7 if explicitly set OR if Tizen 7 or below
            if (useT7Fallback || SystemInfo.TizenVersionMajor < 8)
            {
                return SecVideoCaptureT7.CaptureScreen(w, h, ref pInfo);
            }
            else
            {
                // Only init once for T8
                if (!initAttempted)
                {
                    try
                    {
                        SecVideoCaptureT8.Init();
                        initAttempted = true;
                    }
                    catch (Exception ex)
                    {
                        Helper.Log.Write(Helper.eLogType.Error, $"T8 Init failed: {ex.Message}");
                        SetT7Fallback();
                        return SecVideoCaptureT7.CaptureScreen(w, h, ref pInfo);
                    }
                }
                
                return SecVideoCaptureT8.CaptureScreen(w, h, ref pInfo);
            }
        }
        [StructLayout(LayoutKind.Sequential)]
        unsafe public struct Info_t
        {
            unsafe public Int32 iGivenBufferSize1 { get; set; }   //a6[0] = 0 //ref: "C Buffer Size is too small. needed %d bytes but given %d bytes [%d:%s]" needs to be = iGivenBufferSize2
            unsafe public Int32 iGivenBufferSize2 { get; set; }   //a6[1] = 4 //ref: "C Buffer Size is too small. needed %d bytes but given %d bytes [%d:%s]" needs to be = iGivenBufferSize1
            unsafe public Int32 iWidth { get; set; }        //a6[2] = 8 //ref: IceWater "caputre_param.ret_width"
            unsafe public Int32 iHeight { get; set; }        //a6[3] = 12  //ref: IceWater "caputre_param.ret_height"
            unsafe public IntPtr pImageY { get; set; }      //a6[4] // = 16 dest of memcopy copys v31 in adress with sizeof(needed buffer size(i think)) into this
            unsafe public IntPtr pImageUV { get; set; }      //a6[5] // = 20 use this! dest of memcopy copys v223 in adress with sizeof(needed buffer size) into this
            unsafe public Int32 iRetColorFormat { get; set; }       //a6[6] // = 24  //ref: IceWater "color format is"(YUV420 = 0, YUV422 = 1, YUV444 = 2 , None = 3, Everything else = Error)
            unsafe public Int32 unknown2 { get; set; }       //a6[7] // = 28 
            unsafe public Int32 capture3DMode { get; set; }       // = 32  //ref: "Capture 3D Mode is DRM_SDP_3D_2D [%d:%s]" (DRM_SDP_3D_2D = 0, DRM_SDP_3D_FRAMEPACKING = 1, DRM_SDP_3D_FRAMESEQ = 2, DRM_SDP_3D_TOPBOTTOM = 3, DRM_SDP_3D_SIDEBYSIDE = 4)
                                                                  //unk3 a6[15] // = 60
        }

    }
}
