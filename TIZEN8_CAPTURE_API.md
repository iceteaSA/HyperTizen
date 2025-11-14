# Tizen 8.0+ Screen Capture Implementation

## Overview

This document describes the implementation of screen capture for Tizen 8.0+ TVs using the `libvideo-capture.so` library with vtable-based method calls.

## Background

On Tizen 8.0+, the old `libsec-video-capture.so.0` library is not available. Instead, the system provides:
- `libvideo-capture.so.0.1.0` - Exports `getInstance()` function
- `libvideo-capture-impl-sec.so` - Contains the actual implementation

## The Problem

The old approach tried to directly call a `CaptureScreen` method from the vtable, which didn't work reliably on Tizen 8.0+.

## The Solution (Discord Community Findings)

A Discord community member reverse-engineered a working implementation and found that the correct approach uses **three vtable methods in sequence**:

### VTable Method Layout

| Index | Offset | Method Name | Parameters | Purpose |
|-------|--------|-------------|------------|---------|
| 3 | 0x0C | `getVideoMainYUV` | `(instance, InputParams*, OutputParams*)` | Captures the screen |
| 13 | 0x34 | Lock function | `(instance, 1, 0)` | Locks before capture |
| 14 | 0x38 | Unlock function | `(instance, 1, 0)` | Unlocks after capture |

### Capture Flow

```
1. Get IVideoCapture instance via getInstance()
2. Call vtable[13] - Lock(instance, 1, 0)
3. Call vtable[3]  - getVideoMainYUV(instance, &inputParams, &outputParams)
4. Call vtable[14] - Unlock(instance, 1, 0)
5. Parse output parameters for width/height
```

## Implementation Details

### InputParams Structure

Based on the reverse-engineered code, the input structure contains:

```csharp
struct InputParams {
    long field0;           // 0
    long field1;           // 0
    int field2;            // 0xFFFF
    int field3;            // 0xFFFF
    byte field4;           // 1
    byte field5-7;         // 0
    int field8;            // 0
    int field9;            // 0
    int bufferSize1;       // 0x7E900 (518,400 bytes)
    int bufferSize2;       // 0x7E900 (518,400 bytes)
    IntPtr pYBuffer;       // Y plane buffer pointer
    IntPtr pUVBuffer;      // UV plane buffer pointer
}
```

### OutputParams Structure

- **Size**: 80 bytes (0x50)
- **Initialization**: Zeroed out with `memset()`
- **Content**: Contains capture result information including width/height at specific offsets

### Example Usage

```csharp
// Initialize once at startup
SecVideoCaptureT8.Init();

// Capture a frame
Info_t captureInfo = new Info_t();
captureInfo.iGivenBufferSize1 = 0x7E900;
captureInfo.iGivenBufferSize2 = 0x7E900;
captureInfo.pImageY = /* Y buffer pointer */;
captureInfo.pImageUV = /* UV buffer pointer */;

int result = SecVideoCaptureT8.CaptureScreen(1920, 1080, ref captureInfo);

if (result == 0 || result == 4) {
    // Success! captureInfo.iWidth and iHeight contain actual dimensions
    Console.WriteLine($"Captured: {captureInfo.iWidth}x{captureInfo.iHeight}");
}
```

## API Control Methods

### Toggle Between Old and New API

```csharp
// Enable new API (default)
SecVideoCaptureT8.EnableNewApi();

// Disable new API and use old method
SecVideoCaptureT8.DisableNewApi();
```

### Diagnostic Output

```csharp
// Dump vtable information for debugging
SecVideoCaptureT8.DumpVTableInfo();
```

This will output:
- Instance and vtable memory addresses
- First 20 vtable entries with their addresses
- Helps verify which methods are available

## Error Codes

| Code | Meaning |
|------|---------|
| 0 | Success |
| 4 | Success (alternate code) |
| -4 | DRM content (Netflix/Widevine) |
| -99 | Internal error / not initialized |
| Other negative | Various capture failures |

## Fallback Mechanism

The implementation includes automatic fallback:

1. **First attempt**: New API (getVideoMainYUV with lock/unlock)
2. **If fails**: Old API (direct CaptureScreen call from vtable[3])
3. **If both fail**: Fall back to Tizen 7 API (`libsec-video-capture.so.0`)

## Decompiled Reference Code

The Discord community member provided this decompiled code from a working Samsung app:

```c
piVar1 = (int *)IVideoCapture::getInstance();
(**(code **)(*piVar1 + 0x34))(piVar1,1,0);          // Lock

piVar1 = (int *)IVideoCapture::getInstance();
iVar2 = (**(code **)(*piVar1 + 0xc))(piVar1,&local_dc,auStack_b8);  // getVideoMainYUV

piVar1 = (int *)IVideoCapture::getInstance();
(**(code **)(*piVar1 + 0x38))(piVar1,1,0);          // Unlock

if (((iVar2 == 0) && (0x3bf < local_54)) && (0x21b < uStack_50)) {
    // Success - dimensions are valid (>960x540)
    memcpy(outputY, capturedY, 0x7e900);
    memcpy(outputUV, capturedUV, 0x7e900);
}
```

## Troubleshooting

### Check Library Availability

```bash
ls -la /usr/lib/libvideo-capture*
ls -la /usr/lib/libvideo-capture-impl-sec*
```

### Check Logs

The implementation provides detailed logging:
- Initialization status
- VTable dump
- Lock/Unlock results
- Capture success/failure
- Dimension validation

### Common Issues

1. **GetInstance() timeout**: SDK incompatible with TV model
   - Try different vtable indices
   - Check if library exists but has different API

2. **Lock returns non-zero**: Capture system busy or unavailable
   - Wait and retry
   - Check if other capture process is running

3. **Dimensions invalid**: OutputParams structure offset incorrect
   - Check actual output structure layout
   - May need to adjust parsing logic

4. **Capture returns -4**: DRM-protected content
   - Cannot capture DRM content (Netflix, etc.)
   - This is expected behavior

## Testing Recommendations

1. Test on different Tizen versions (8.0, 8.5, 9.0, etc.)
2. Test on different Samsung TV models
3. Compare output between new API and old API (if both work)
4. Verify NV12 image data is correctly captured
5. Check performance (capture time, CPU usage)

## Credits

- Implementation: HyperTizen project
- Reverse engineering: Discord community member (ID: 216474671426699264)
- Original finding: vtable-haystack branch research

## References

- Original vtable-haystack branch: https://github.com/iceteaSA/HyperTizen/tree/vtable-haystack
- Discord discussion: [Community findings on libvideo-capture-impl-sec.so]
