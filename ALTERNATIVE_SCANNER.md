# Alternative Capture Method Scanner

## What It Does

The **LibraryScanner** automatically searches your TV for alternative ways to capture the screen when the standard T8 API is blocked.

## What We're Looking For

### 1. 🎯 T7 API (HIGHEST PRIORITY)
**File:** `/usr/lib/libsec-video-capture.so.0`

If this exists, it's the **old Tizen 7 capture API** that might still work on Tizen 8!

**What happens if found:**
- ✅ Library is loaded and probed for symbols
- ✅ **Actual capture test is performed!**
- ✅ If successful, you can use T7 API instead of blocked T8

**Expected results:**
- `result = 0` → **SUCCESS! Use this API!**
- `result = -4` → DRM content, but API works
- `result = -95` → Also blocked (unlikely)

### 2. 📁 Alternative Video/Capture Libraries

Scans for ANY library containing:
- `*video*.so*`
- `*capture*.so*`
- `*screen*.so*`
- `*scaler*.so*`
- `*display*.so*`

For each found library, checks for these symbols:
```
captureScreen, capture_screen, CaptureScreen
getFrame, get_frame, GetFrame
screenCapture, screen_capture
getVideoMainYUV, getVideoPostYUV
fb_capture, fbCapture
screenshot, Screenshot
... and many more
```

**If symbols are found** → Library has capture-related functions we can try!

### 3. 🖼️ Framebuffer Devices

Checks for direct screen access:
- `/dev/fb0`, `/dev/fb1`
- `/dev/graphics/fb0`

**If readable** → Can potentially read screen pixels directly from framebuffer!

**Note:** Usually requires root access, but worth checking.

### 4. 🔧 Developer/Debug Libraries

Looks for:
- `libdeveloper.so` - Developer API
- `libdebug.so` - Debug API
- `libinternal.so` - Internal API
- `libtv-service.so` - TV service API

These might have **unrestricted capture** for debugging purposes!

### 5. 📚 Samsung-Specific Libraries

From reference binaries, these are known to use capture:
- `libvideo-capture-impl-sec.so` - Implementation library
- `libscaler.so` - Image scaler (used in capture)
- `libtbm.so` - Tizen Buffer Manager
- `libtdm.so` - Tizen Display Manager
- `libsamsungplatform.so` - Samsung platform API

## How It Works

Scanner runs **automatically** after the T8 capture test fails:

```
1. T8 test fails (-95 error)
2. Scanner starts automatically
3. Searches all known paths for libraries
4. Loads each library with dlopen()
5. Probes for capture-related symbols
6. Tests T7 API if found
7. Reports all findings to logs
```

## What to Look For in Logs

### ✅ SUCCESS Signs:

```
✓ Found: libsec-video-capture.so.0
✅✅✅ T7 API WORKS ON TIZEN 8! ✅✅✅
⭐⭐⭐ USE T7 API INSTEAD OF T8! ⭐⭐⭐
```
**→ T7 API works! Can implement fallback!**

```
✓ Symbol found: captureScreen @ 0xABCDEF
⭐ PROMISING: libfoobar.so has 5 capture-related symbols!
```
**→ Found alternative library to investigate!**

```
✓ CAN READ /dev/fb0! Size: 8294400 bytes
```
**→ Framebuffer access works! Can try direct capture!**

### ❌ Blocked Signs:

```
✗ T7 API not found
✗ /dev/fb0 exists but no read permission
✗ Cannot load library: Permission denied
```
**→ Keep looking for other alternatives**

## Implementation Priority

If scanner finds alternatives:

### Priority 1: T7 API Works
- Implement T7 fallback in SecVideoCapture
- Use `secvideo_api_capture_screen_video_only()`
- Should work immediately!

### Priority 2: Alternative Library Found
- Analyze symbols found
- Create P/Invoke declarations
- Test each function

### Priority 3: Framebuffer Readable
- Read `/dev/fb0` directly
- Parse framebuffer data format
- Extract pixel data

### Priority 4: Developer Library
- Load developer library
- Explore available APIs
- Test capture functions

## Example Output

```
=== Scanning for Alternative Capture Methods ===

--- Checking for T7 API (libsec-video-capture.so.0) ---
  ✓ FOUND: /usr/lib/libsec-video-capture.so.0
  ⭐ T7 API exists! May be able to use old API on Tizen 8!
  ✓ Loaded: libsec-video-capture.so.0
    ✓ Symbol found: secvideo_api_capture_screen @ 0xAA123456
    ✓ Symbol found: secvideo_api_capture_screen_video_only @ 0xAA123789
    ✓ Symbol found: secvideo_api_capture_screen_unlock @ 0xAA123ABC
  ⭐ PROMISING: libsec-video-capture.so.0 has 3 capture-related symbols!
  Testing if T7 API works on Tizen 8...
  ✅✅✅ T7 API WORKS ON TIZEN 8! ✅✅✅
  Captured resolution: 1920x1080
  ⭐⭐⭐ USE T7 API INSTEAD OF T8! ⭐⭐⭐

--- Searching for video/capture libraries ---
  Found: /usr/lib/libvideo-capture.so.0.1.0
  ✓ Loaded: libvideo-capture.so.0.1.0
  Found: /usr/lib/libscaler.so.0
  ✓ Loaded: libscaler.so.0

--- Checking framebuffer devices ---
  ✓ Found: /dev/fb0
  ✗ /dev/fb0 exists but no read permission (requires root)

--- Checking for developer/debug libraries ---
  ✓ Found: libdlog.so

--- Checking alternative Samsung libraries ---
  ✓ Found: libscaler.so
  ✓ Loaded: libscaler.so
  ✓ Found: libtbm.so
  ✓ Loaded: libtbm.so

=== End Alternative Scan ===
```

## Next Steps After Scan

1. **Review WebSocket logs** - Check what was found
2. **If T7 works** - Implement T7 fallback immediately
3. **If libraries found** - Investigate symbol functions
4. **If framebuffer works** - Implement direct capture
5. **If nothing found** - May need root/developer mode

## Why This Matters

Even if T8 API is blocked, we have multiple fallback options:

1. ✅ T7 API might still work
2. ✅ Alternative libraries might exist
3. ✅ Framebuffer access might be available
4. ✅ Developer APIs might be unrestricted

**Don't give up just because T8 is blocked!** There may be other ways. 🚀
