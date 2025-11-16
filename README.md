# HyperTizen - Tizen 8.0+ Fork

### Color up your Tizen TV with HyperTizen!
HyperTizen is a Hyperion / HyperHDR capturer for Tizen TVs.

---

## ⚠️ Honest Disclaimer

This project **doesn't actually work yet** (or may only partially work). It started with [someone else's excellent work](https://github.com/reisxd/HyperTizen), and most of the "development" was done by burning through way too many AI credits. There's a good chance that half the code is complete gibberish that just *looks* technical. Use at your own risk, and lower your expectations accordingly.

If you somehow find this useful, or just want to support questionable AI-driven development practices:

**[☕ Buy me a coffee](https://ko-fi.com/H2H719VB0U)**

---

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

**New Capture Architecture:** HyperTizen now uses a systematic `ICaptureMethod` interface with automatic fallback selection. The `CaptureMethodSelector` tests available methods on startup (T8SDK → T7SDK → PixelSampling) and automatically selects the best working method for your TV.

---

### VideoEnhance Pixel Sampling (WORKING)

**Key Details:**
- Uses `VideoEnhance_SamplePixel()` from libvideoenhance.so
- Samples individual RGB pixels from the active video stream
- **NOT blocked by Samsung firmware** (unlike official capture APIs)
- Slower than full-frame capture, but proven to work on Tizen 8.0+

**Performance Characteristics:**
- Must sample pixels individually (no batch/frame operations)
- Suitable for ambient lighting applications
- Works on Samsung TVs running Tizen 8.0+ firmware

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

## Browser-Based Control Panel

In addition to logs, HyperTizen provides a **full control panel** accessible from any browser on your network.

### Accessing the Control Panel

1. **Start HyperTizen** on your TV
2. **Open your browser** on any device on the same network
3. **Open the control panel:**
   - **Option 1**: Navigate to `http://<YOUR_TV_IP>:45678/controls.html`
   - **Option 2**: Download `controls.html` from this repository and open it locally, then enter your TV's IP

### Control Panel Features

The control panel (`controls.html`) provides the same functionality as the HyperTizenUI but through a standard browser:

**Service Control:**
- ▶️ Start/Stop capture
- ⏸️ Pause/Resume capture
- 🔄 Restart HyperTizen service
- 🌈 Rainbow border indicator when capturing

**SSDP Device Management:**
- 🔍 Scan for Hyperion/HyperHDR devices on your network
- ✓ Select and apply devices
- View device details (name, URL)

**Live Monitoring:**
- 📊 Real-time service status (state, FPS, frames captured, errors)
- 📋 Live log streaming (same as logs.html)
- ⏱️ Uptime and connection status
- 🔌 Dual WebSocket status indicators (control + logs)

**WebSocket Connections:**
- Port **45677**: Control WebSocket (send commands)
- Port **45678**: Logs WebSocket (receive logs)
- Auto-reconnect with exponential backoff
- Persistent settings (saves TV IP in browser)

### Example

```
http://192.168.1.100:45678/controls.html
```

Or open `controls.html` locally and enter:
```
TV IP: 192.168.1.100
Control Port: 45677
Logs Port: 45678
```

The control panel is perfect for:
- Managing HyperTizen from your phone/tablet/computer
- Testing capture without accessing the TV UI
- Monitoring service status during troubleshooting
- Selecting Hyperion/HyperHDR servers without using the TV remote

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

5. **Install the HyperTizen UI** via TizenBrew's GitHub module manager:

   **Using the Module Manager:**
   - Press the **[GREEN]** button on your remote to open TizenBrew module manager
   - Navigate to "Add GitHub Module"
   - Enter the module path:

   **Install from this fork** (Tizen 8+ with pixel sampling):
   ```
   iceteaSA/HyperTizen/HyperTizenUI
   ```

   **Install from original repo** (Tizen 7 only):
   ```
   reisxd/HyperTizen/HyperTizenUI
   ```

   **Format:**
   ```
   <username>/<repository>/<folder-path>
   ```
   - Installs from the default branch (usually `main`)
   - `username/repository` - GitHub repository owner and name
   - `folder-path` - Path to the app folder within the repository

   > **Note:** To test development branches, you'll need to manually update files on your TV or wait for the branch to be merged to main.

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