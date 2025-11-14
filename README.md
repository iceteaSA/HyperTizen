# HyperTizen - Tizen 8.0+ Fork

### Color up your Tizen TV with HyperTizen!
HyperTizen is a Hyperion / HyperHDR capturer for Tizen TVs.

<p align="center">
    <a href="https://discord.gg/m2P7v8Y2qR">
       <picture>
           <source height="24px" media="(prefers-color-scheme: dark)" srcset="https://user-images.githubusercontent.com/13122796/178032563-d4e084b7-244e-4358-af50-26bde6dd4996.png" />
           <img height="24px" src="https://user-images.githubusercontent.com/13122796/178032563-d4e084b7-244e-4358-af50-26bde6dd4996.png" />
       </picture>
       </a>
       <a href="https://www.youtube.com/@tizenbrew">
      <picture>
         <source height="24px" media="(prefers-color-scheme: dark)" srcset="https://user-images.githubusercontent.com/13122796/178032714-c51c7492-0666-44ac-99c2-f003a695ab50.png" />
         <img height="24px" src="https://user-images.githubusercontent.com/13122796/178032714-c51c7492-0666-44ac-99c2-f003a695ab50.png" />
     </picture>
     </a>
</p>

---

## About This Fork

This is an **experimental fork** of [HyperTizen](https://github.com/reisxd/HyperTizen) focused on implementing screen capture functionality for **Tizen 8.0+ TVs**. Samsung's firmware blocks the standard capture APIs on Tizen 8.0+, making the original HyperTizen unable to capture video frames on newer TV models. This fork explores alternative capture methods that work around these firmware restrictions.

### Status: Research & Development

This fork is primarily focused on research and proof-of-concept implementations for Tizen 8+ capture methods. While we have achieved a working breakthrough (see below), the implementation is slower than traditional capture methods and is still being optimized.

---

## BREAKTHROUGH: Working Capture on Tizen 8!

### VideoEnhance Pixel Sampling (WORKING)

We've successfully implemented a working capture method for Tizen 8.0+ using **libvideoenhance.so** to sample RGB pixels from the video stream.

**Key Details:**
- Uses `VideoEnhance_SamplePixel()` from libvideoenhance.so
- Samples individual RGB pixels from the active video stream
- **NOT blocked by Samsung firmware** (unlike official capture APIs)
- Slower than full-frame capture, but proven to work on Tizen 8.0+
- See commit: [922ffed](https://github.com/iceteaSA/HyperTizen/commit/922ffed)

**Performance Characteristics:**
- Must sample pixels individually (no batch/frame operations)
- Suitable for ambient lighting applications
- Works on Samsung TVs running Tizen 8.0+ firmware

This breakthrough demonstrates that screen capture IS possible on Tizen 8+, despite Samsung's API restrictions.

---

## How to Use the WebSocket Log Viewer

This fork includes a **real-time browser-based log viewer** that's essential for debugging on your TV.

### Accessing Logs

1. **Start HyperTizen** on your TV
2. **Open your browser** on any device on the same network
3. **Navigate to:** `http://<YOUR_TV_IP>:45678`
4. The log viewer will automatically connect and display real-time logs

### Log Viewer Features

- **Real-time streaming**: See logs as they happen
- **Auto-reconnect**: Automatically reconnects if connection is lost
- **Exponential backoff**: Smart retry logic prevents connection spam
- **Color-coded output**: Easy to read and filter
- **Persistent across sessions**: Reconnects when TV restarts HyperTizen

### Finding Your TV's IP Address

You can find your TV's IP address in:
- **Settings** → **General** → **Network** → **Network Status** → **IP Settings**

Or use your router's admin panel to find connected devices.

### Example

```
http://192.168.1.100:45678
```

The log viewer (`logs.html`) is served automatically when HyperTizen is running. This is particularly useful for debugging capture issues, monitoring performance, and understanding what's happening on the TV without needing SSH or console access.

---

## What Works (and What Doesn't)

### Working on Tizen 8.0+

- **VideoEnhance Pixel Sampling**: Proven working capture method
- **WebSocket Log Streaming**: Real-time debugging via browser
- **Comprehensive Library Scanner**: Automatic detection of available capture methods
- **Safe Dynamic Loading**: Prevents crashes from missing libraries

### Technically Complete (But Blocked)

- **T8 API Implementation**: Complete vtable-based IVideoCapture implementation
  - Correctly implements all Tizen 8 capture interfaces
  - Blocked by firmware feature flags (returns `-95 EOPNOTSUPP`)
  - See technical docs below for details

### Not Working on Tizen 8.0+

- **Standard Tizen 7 APIs**: Blocked by Samsung firmware
- **Framebuffer Access**: Restricted by permissions
- **Hardware Accelerated Capture**: Feature-flagged out by Samsung

---

## Technical Documentation

Detailed technical documentation for developers:

- **[ALTERNATIVE_SCANNER.md](./ALTERNATIVE_SCANNER.md)** - Comprehensive library scanner that searches for alternative capture methods
- **[TIZEN8_CAPTURE_API.md](./TIZEN8_CAPTURE_API.md)** - Complete vtable-based T8 API implementation
- **[CAPTURE_BLOCKED_ANALYSIS.md](./CAPTURE_BLOCKED_ANALYSIS.md)** - Analysis of why official APIs return `-95 EOPNOTSUPP`
- **[VTABLE_ANALYSIS.md](./VTABLE_ANALYSIS.md)** - Reverse engineering notes on Tizen 8 virtual tables
- **[WEBSOCKET_LOGS.md](./WEBSOCKET_LOGS.md)** - WebSocket log streaming implementation details
- **[HANDOFF_FOR_NEXT_CLAUDE.md](./HANDOFF_FOR_NEXT_CLAUDE.md)** - Development notes and next steps

---

## Installation

To install HyperTizen, you need to have a Samsung TV running Tizen (works on both Tizen 7 and Tizen 8+, though capture methods differ).

You'll need Tizen Studio to install the app on your TV. You can download it from the [official website](https://developer.samsung.com/smarttv/develop/getting-started/setting-up-sdk/installing-tv-sdk.html).

### Installation Steps

1. Download the latest release from the [releases page](https://github.com/reisxd/HyperTizen/releases/latest) (or build from this fork).

2. Change the Host PC IP address to your PC's IP address by following [this guide](https://developer.samsung.com/smarttv/develop/getting-started/using-sdk/tv-device.html#Connecting-the-TV-and-SDK)

3. Install the package:
```bash
tizen install -n path/to/io.gh.reisxd.HyperTizen.tpk
```

Note that `tizen` is in `C:\tizen-studio\tools\ide\bin` on Windows and in `~/tizen-studio/tools/ide/bin` on Linux.

If you get `install failed[118, -12], reason: Check certificate error` error, you'll have to resign the package (see below).

4. Install TizenBrew to your TV. Follow [this guide](https://github.com/reisxd/TizenBrew/blob/main/docs/README.md).

5. Add `reisxd/HyperTizen/HyperTizenUI` as a GitHub module to the module manager. You can access the module manager by pressing the [GREEN] button on the remote.

### Resigning the Package

1. Change the Host PC IP address to your PC's IP address by following [this guide](https://developer.samsung.com/smarttv/develop/getting-started/using-sdk/tv-device.html#Connecting-the-TV-and-SDK)

2. After following the guide for the Tizen Studio installation, you have to create a certificate profile. You can follow [this guide](https://developer.samsung.com/smarttv/develop/getting-started/setting-up-sdk/creating-certificates.html).

3. Sign the package:
```bash
tizen package -t tpk -s YourProfileName -o path/to/output/dir -- path/to/io.gh.reisxd.HyperTizen.tpk

# Example:
# tizen package -t tpk -s HyperTizen -o release -- io.gh.reisxd.HyperTizen.tpk
```

4. You should now be able to install the package.

---

## Building from Source

See the original [HyperTizen documentation](./docs/README.md) for general build instructions.

For this fork, additional development tools may be required for testing and debugging the Tizen 8+ capture methods.

---

## Credits

### Original HyperTizen Project

This fork is based on [HyperTizen by reisxd](https://github.com/reisxd/HyperTizen).

Original HyperTizen provides Hyperion/HyperHDR capture support for Tizen TVs running Tizen 7.0 and earlier firmware versions.

### This Fork

Tizen 8.0+ capture research and implementation by the community. Special thanks to:
- Original HyperTizen contributors for the foundational codebase
- TizenBrew project for enabling homebrew development on Samsung TVs
- Everyone testing and contributing to Tizen 8+ capture research

### Related Projects

- **[HyperTizen](https://github.com/reisxd/HyperTizen)** - Original project (Tizen 7 support)
- **[TizenBrew](https://github.com/reisxd/TizenBrew)** - Homebrew for Samsung Tizen TVs
- **[Hyperion](https://hyperion-project.org/)** - Ambient lighting software
- **[HyperHDR](https://github.com/awawa-dev/HyperHDR)** - HDR-capable fork of Hyperion

---

## Contributing

Contributions are welcome! If you have ideas for improving Tizen 8+ capture performance or alternative capture methods, please:

1. Check the technical documentation first
2. Test your changes on actual Tizen 8+ hardware
3. Submit pull requests with detailed explanations
4. Use the WebSocket log viewer to document behavior

---

## License

Same as original HyperTizen project.

---

## Disclaimer

This is experimental software for research and educational purposes. Use at your own risk. This fork is not affiliated with Samsung or the official Tizen project.

Samsung has intentionally blocked capture APIs on Tizen 8.0+ firmware. This fork explores alternative methods that work around these restrictions, but cannot guarantee compatibility with all TV models or future firmware updates.