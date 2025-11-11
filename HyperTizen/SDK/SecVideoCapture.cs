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
        [UnmanagedFunctionPointer(CallingConvention.ThisCall)]
        public unsafe delegate int LockDelegate(IntPtr @this, int param1, int param2);

        [UnmanagedFunctionPointer(CallingConvention.ThisCall)]
        public unsafe delegate int GetVideoMainYUVDelegate(IntPtr @this, ref InputParams input, ref OutputParams output);

        [UnmanagedFunctionPointer(CallingConvention.ThisCall)]
        public unsafe delegate int UnlockDelegate(IntPtr @this, int param1, int param2);

        [UnmanagedFunctionPointer(CallingConvention.ThisCall)]
        public unsafe delegate int CaptureScreenDelegate(IntPtr @this, int w, int h, ref Info_t pInfo);

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

        // Muss importiert sein, wenn getInstance exportiert wird
        [DllImport("/usr/lib/libvideo-capture.so.0.1.0", CallingConvention = CallingConvention.Cdecl, EntryPoint = "getInstance")]
        private static extern IVideoCapture* GetInstance();

        public static void Init()
        {
            if (isInitialized)
            {
                Helper.Log.Write(Helper.eLogType.Info, "SecVideoCaptureT8 already initialized, skipping");
                return;
            }

            // Check if library file exists
            if (!System.IO.File.Exists("/usr/lib/libvideo-capture.so.0.1.0"))
            {
                Helper.Log.Write(Helper.eLogType.Error, "T8 SDK: libvideo-capture.so.0.1.0 NOT FOUND!");
                throw new System.IO.FileNotFoundException("Tizen 8 SDK library not found");
            }

            Helper.Log.Write(Helper.eLogType.Info, "T8 SDK: Library found, calling GetInstance()...");
            
            // Wrap GetInstance in timeout protection
            IVideoCapture* tempInstance = null;
            bool getInstanceCompleted = false;
            Exception getInstanceError = null;
            
            var getInstanceTask = System.Threading.Tasks.Task.Run(() =>
            {
                try
                {
                    tempInstance = GetInstance();
                    getInstanceCompleted = true;
                    Helper.Log.Write(Helper.eLogType.Info, "T8 SDK: GetInstance() returned!");
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

        public static int CaptureScreen(int w, int h, ref Info_t pInfo)
        {
            if (!isInitialized)
            {
                Helper.Log.Write(Helper.eLogType.Error, "SecVideoCaptureT8 not initialized");
                return -99;
            }

            // Try new API first (getVideoMainYUV with lock/unlock)
            if (useNewApi && getVideoMainYUV != null && lockFunc != null && unlockFunc != null)
            {
                int result = CaptureScreenNewApi(w, h, ref pInfo);

                // If new API fails, try to fall back to old API
                if (result != 0 && captureScreen != null)
                {
                    Helper.Log.Write(Helper.eLogType.Warning, $"T8 SDK: New API failed with code {result}, trying old API...");
                    useNewApi = false;
                    return captureScreen((IntPtr)instance, w, h, ref pInfo);
                }

                return result;
            }
            // Fall back to old API if available
            else if (captureScreen != null)
            {
                return captureScreen((IntPtr)instance, w, h, ref pInfo);
            }
            else
            {
                Helper.Log.Write(Helper.eLogType.Error, "T8 SDK: No capture methods available");
                return -99;
            }
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
                // Step 1: Call lock function (vtable[13])
                Helper.Log.Write(Helper.eLogType.Debug, "T8 SDK: Calling lock function...");
                int lockResult = lockFunc((IntPtr)instance, 1, 0);
                if (lockResult != 0)
                {
                    Helper.Log.Write(Helper.eLogType.Error, $"T8 SDK: Lock failed with code {lockResult}");
                    return lockResult;
                }

                // Step 2: Prepare input and output parameters
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

                // Output is zeroed by default (fixed array)

                // Step 3: Call getVideoMainYUV (vtable[3])
                Helper.Log.Write(Helper.eLogType.Debug, "T8 SDK: Calling getVideoMainYUV...");
                int captureResult = getVideoMainYUV((IntPtr)instance, ref input, ref output);

                // Step 4: Call unlock function (vtable[14])
                Helper.Log.Write(Helper.eLogType.Debug, "T8 SDK: Calling unlock function...");
                int unlockResult = unlockFunc((IntPtr)instance, 1, 0);
                if (unlockResult != 0)
                {
                    Helper.Log.Write(Helper.eLogType.Warning, $"T8 SDK: Unlock returned code {unlockResult}");
                }

                if (captureResult != 0 && captureResult != 4 && captureResult != -4)
                {
                    Helper.Log.Write(Helper.eLogType.Error, $"T8 SDK: getVideoMainYUV failed with code {captureResult}");
                    return captureResult;
                }

                // Step 5: Parse output parameters
                // The output structure should contain width/height at specific offsets
                // Based on the decompiled code: local_54 and uStack_50 contain dimensions
                fixed (byte* pOutput = output.data)
                {
                    // Try to extract width and height from output
                    // This is speculative - may need adjustment based on actual output format
                    int* pInts = (int*)pOutput;
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
