# Handoff Document - Tizen 8+ Screen Capture Implementation

## Current Status
**App is still crashing during library search initialization**. The crash happens right after:
```
19:28:28[Info]Search results being written to: /tmp/hypertizen_search.log
19:28:28[Info]Connect to WebSocket to view full results
```
Then the app crashes before any search commands complete.

## Problem
The exhaustive library search (25 commands using find, ls, nm, strings, etc.) is **causing the app to crash** even though:
- Results are being written to `/tmp/hypertizen_search.log` (file-based, not memory)
- Progress updates only every 5 searches
- TV notifications filtered for search-related messages
- 500ms delays between searches
- 3-second timeouts per command

**The TV OS cannot handle the process spawning, even with delays and timeouts.**

## What Needs to Happen Next

### IMMEDIATE ACTION REQUIRED:
**REMOVE THE ENTIRE SearchForLibraries() FUNCTION CALL**

The library search is not essential for initialization. We already know from previous successful runs:
- `/usr/lib/libvideo-capture.so.0.1.0` exists
- `/usr/lib/libvideo-capture-impl-sec.so` does NOT exist
- The getInstance symbol is missing or has an unknown name

### Code Location to Fix:
**File: `/home/user/HyperTizen/HyperTizen/SDK/SecVideoCapture.cs`**
**Line: ~361-369**

```csharp
// REMOVE OR COMMENT OUT THIS ENTIRE BLOCK:
try
{
    SearchForLibraries();
}
catch (Exception ex)
{
    Helper.Log.Write(Helper.eLogType.Warning, $"Library search failed: {ex.Message}");
}
```

Replace with:
```csharp
Helper.Log.Write(Helper.eLogType.Info, "Skipping library search - using known library paths");
Helper.Log.Write(Helper.eLogType.Info, "Known: /usr/lib/libvideo-capture.so.0.1.0 exists");
```

### Next Steps After Removing Search:

1. **Test if app reaches diagnostic countdown** (should work without search)
2. **Check the dlsym probing results** - does it find getInstance?
3. **If getInstance fails**, try the dev's suggestion from references

---

## Key Information from References Folder

The user provided critical files in `/home/user/HyperTizen/references/`:

### 1. `GetCaptureFromTZ.c` - Decompiled Samsung Code
**Shows the ACTUAL working implementation:**

```c
// Line 56: Get instance
piVar1 = (int *)IVideoCapture::getInstance();

// Line 57: Lock (vtable[13] = offset 0x34)
(**(code **)(*piVar1 + 0x34))(piVar1,1,0);

// Line 59: getVideoMainYUV (vtable[3] = offset 0xc)
iVar2 = (**(code **)(*piVar1 + 0xc))(piVar1,&local_dc,auStack_b8);

// Line 74: Unlock (vtable[14] = offset 0x38)
(**(code **)(*piVar1 + 0x38))(piVar1,1,0);
```

**Key discoveries:**
- `local_dc` structure initialized with specific values (0xffff, buffer sizes 0x7e900, etc.)
- `auStack_b8` is 80 bytes (0x50) of zeros for output
- Success check: `(iVar2 + 4U & 0xfffffffb) == 0` means result is 0 or 4
- Width/height checked: `0x3bf < local_54` and `0x21b < uStack_50` (960x540)

### 2. `2.webp` - Library Exports Screenshot
Shows exported functions from `libvideo-capture-impl-sec.so`:
- **getVideoMainYUV** (plain C export, not mangled!)
- **getVideoPostYUV** (plain C export)
- Other functions visible

**BUT** - this library does NOT exist on the user's Tizen 8.0 TV!

### 3. Dev's Key Insight (from conversation)
```
"we only have to import video-capture for getting IVideoCapture instance
we can call methods exported in video-capture-impl-sec using the instance we get,
no need to use VTables"
```

**Translation:**
1. Get instance from `/usr/lib/libvideo-capture.so.0.1.0`
2. Call exported functions (getVideoMainYUV, etc.) directly - pass instance as parameter
3. Don't use vtable method access

---

## Current Implementation Issues

### Issue 1: getInstance Not Found
All attempts to get getInstance() are failing:
- `getInstance` - not found
- `_Z11getInstancev` - not found
- `_ZN13IVideoCapture11getInstanceEv` - not found
- All 7 symbol variants tried - none found

**Possible solutions to try:**
1. Use `nm -D /usr/lib/libvideo-capture.so.0.1.0` to dump ALL symbols (do this manually via SSH)
2. Check if getInstance is in a different library
3. Maybe getInstance doesn't exist - find alternative initialization method

### Issue 2: Library Mismatch
The references show `libvideo-capture-impl-sec.so` but this **does not exist** on the user's TV.
Functions must be exported from the main library instead.

**What to check:**
```bash
# SSH into TV and run:
nm -D /usr/lib/libvideo-capture.so.0.1.0 | grep -i instance
nm -D /usr/lib/libvideo-capture.so.0.1.0 | grep -i video
nm -D /usr/lib/libvideo-capture.so.0.1.0 | grep -i capture
ldd /usr/lib/libvideo-capture.so.0.1.0
```

---

## Architecture Summary

### Current Three-Tier Approach (in CaptureScreen):
1. **Direct function call** - `GetVideoMainYUVDirectMain()` from libvideo-capture.so.0.1.0
2. **VTable method** - Lock → getVideoMainYUV (vtable[3]) → Unlock
3. **Old API fallback** - Original T7 API

### Data Structures Already Implemented:
```csharp
InputParams (56 bytes):
- field0-1: 0
- field2-3: 0xffff
- field4: 1
- bufferSize1/2: 0x7e900
- pYBuffer, pUVBuffer: pointers to user buffers

OutputParams (80 bytes):
- All zeros initially
- Width at offset 0, Height at offset 4 after call
```

---

## Files Modified This Session

1. **HyperTizen/SDK/SecVideoCapture.cs** - Main T8 capture implementation
   - Added SearchForLibraries() - **CAUSING CRASHES**
   - Added ProbeForGetInstance() with dlsym
   - Added direct function imports
   - Added file-based logging

2. **HyperTizen/Helper/Log.cs** - Logging utilities
   - Added BroadcastSearchProgress()
   - Added BroadcastSearchComplete()
   - Added smart notification filtering

3. **logs.html** - WebSocket viewer (working correctly)

---

## Diagnostic Mode Info

**Location:** `HyperTizen/HyperTizen/HyperionClient.cs`
```csharp
const bool DIAGNOSTIC_MODE = true;
```

After initialization, should pause for 10 minutes with countdown every 60 seconds.
**This is NOT being reached** because app crashes during library search.

---

## Next Claude Instance TODO:

1. ✅ **Remove SearchForLibraries() call** - causes crashes
2. ⚠️ **Test if app reaches diagnostic countdown** without search
3. ⚠️ **Check dlsym probing results** for exported functions
4. ⚠️ **Manual symbol inspection** via SSH if getInstance still fails
5. ⚠️ **Try alternative getInstance methods** based on actual exports
6. ⚠️ **Test direct function call approach** once getInstance works

---

## WebSocket Log Viewer

Working correctly on `http://10.1.1.7:45678` with `logs.html`
- Auto-reconnect with exponential backoff
- Can be manually stopped
- Receives all logs in real-time
- Search results WOULD be streamed here IF search didn't crash

---

## Branch Information

Branch: `claude/tizen-screen-capture-vtable-011CV2FcH3gCGpbk8sL9urih`

Latest commits:
- `dc45daf` - Write search results to file (THIS ONE CRASHES)
- `07824f4` - Refine library search with fast ls commands
- `1396ae3` - Replace heavy search with static library info
- Previous work on direct function calls, vtable methods, etc.

---

## Critical References to Review

**Before making changes, review:**
1. `references/GetCaptureFromTZ.c` - Line 56-74 (the working implementation)
2. `references/2.webp` - Shows exported function names
3. Dev's message about not needing vtables

**Key insight:** The decompiled code shows vtable access, but dev says we don't need vtables if we import the functions directly. This suggests:
- The vtable just contains function pointers to exported functions
- We can import those functions directly if we know their names
- The screenshot shows they ARE exported (from impl-sec, which doesn't exist here)
- Need to find where they're ACTUALLY exported on this TV

---

## Success Criteria

App should:
1. Start without crashing ✅ (if search removed)
2. Reach diagnostic countdown ⚠️ (blocked by crash)
3. Find getInstance or alternative ❌
4. Successfully call getVideoMainYUV ❌
5. Return valid screen resolution ❌
6. Capture actual screen data ❌

**Current blocker:** Library search crashes the app before any testing can occur.

---

## Emergency Fallback

If EVERYTHING fails with T8 API:
```csharp
SetT7Fallback(); // Use old API even on Tizen 8
```
This is already implemented in `SecVideoCapture.cs`.
