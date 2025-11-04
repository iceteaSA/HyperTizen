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

        [UnmanagedFunctionPointer(CallingConvention.ThisCall)]
        public unsafe delegate int CaptureScreenDelegate(IntPtr @this, int w, int h, ref Info_t pInfo);
        public unsafe struct IVideoCapture
        {
            public IntPtr* vtable;
        }

        private static IVideoCapture* instance;
        private static CaptureScreenDelegate captureScreen;
        private static bool isInitialized = false;

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

            const int CaptureScreenVTableIndex = 3;

            Helper.Log.Write(Helper.eLogType.Info, $"T8 SDK: Getting function pointer at vtable[{CaptureScreenVTableIndex}]...");
            IntPtr fp = instance->vtable[CaptureScreenVTableIndex];
            
            if (fp == IntPtr.Zero)
            {
                Helper.Log.Write(Helper.eLogType.Error, "T8 SDK: Function pointer is NULL!");
                throw new NullReferenceException($"Function pointer at vtable[{CaptureScreenVTableIndex}] is null");
            }

            Helper.Log.Write(Helper.eLogType.Info, "T8 SDK: Creating delegate from function pointer...");
            captureScreen = (CaptureScreenDelegate)Marshal.GetDelegateForFunctionPointer(fp, typeof(CaptureScreenDelegate));
            
            isInitialized = true;
            Helper.Log.Write(Helper.eLogType.Info, "T8 SDK: Initialized successfully!");
        }

        public static int CaptureScreen(int w, int h, ref Info_t pInfo)
        {
            if (captureScreen == null)
            {
                Helper.Log.Write(Helper.eLogType.Error, "SecVideoCaptureNew not initialized");
                return -99; // Return error code instead of crashing
            }

            return captureScreen((IntPtr)instance, w, h, ref pInfo);
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
