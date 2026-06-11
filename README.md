# TabSpool — Browser Tab Wheel Switcher (C# .NET 10)

**TabSpool** is a lightweight, high-performance background utility for Windows that lets you switch browser tabs (Chrome, Edge, Brave, Opera, etc.) by scrolling your mouse wheel while hovering directly over the native window tab bar.

---

## How It Works

1.  **Low-level Scroll Interception**: The utility installs a global Win32 mouse hook (`WH_MOUSE_LL`) to monitor mouse wheel events.
2.  **Smart Target Detection**: On scroll, it inspects the window class name under the cursor. If it matches `Chrome_WidgetWin_1` (the window class for Chromium-based browsers) and the cursor is inside the tab strip bounding box (top 40px of the window), it intercepts the event.
3.  **Simulated Shortcuts**: It consumes the scroll and sends standard browser keyboard shortcuts:
    *   **Scroll Down** -> Sends `Ctrl + Tab` (Switches to the **next** tab).
    *   **Scroll Up** -> Sends `Ctrl + Shift + Tab` (Switches to the **previous** tab).
4.  **Zero-dependency Compiled Binary**: It compiles to a tiny standalone single-file executable in C# using native Windows P/Invoke APIs, taking less than 15MB of RAM and 0% CPU.

---

## Quick Start

### 🛠️ Compilation

To compile this on a machine with the **.NET 10 SDK** installed:
1.  Open your terminal in the workspace directory.
2.  Run the compile batch script:
    ```powershell
    .\build.bat
    ```
    *Or manually compile and publish with:*
    ```powershell
    dotnet publish -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -p:PublishTrimmed=false
    ```
3.  This creates **`TabSpool.exe`** in the root directory.

### 🏃 Running the App
1.  Double-click **`TabSpool.exe`**.
    *   The app runs silently in the background (no console window).
    *   An icon will appear in your **System Tray** (bottom right of your screen).
2.  Open Chrome, Edge, or Brave, hover your cursor over the native tab strip at the top, and scroll your mouse wheel!

---

## Configuration Settings

When run for the first time, TabSpool creates a **`config.txt`** file in the same directory. You can edit this file to customize:

*   `TabBarHeight`: Bounding box height of your tab strip from the top of the window (default: `40` pixels).
*   `ExcludeRightMargin`: Bounding box margin from the right edge to avoid triggering on window controls like Minimize, Maximize, or Close (default: `140` pixels).
*   `ScrollCooldownMs`: Debounce delay between scroll triggers (default: `150` milliseconds).
*   `ReverseDirection`: Reverses scroll directions (default: `false`).

### How to apply settings:
1.  Right-click the **TabSpool** icon in the system tray.
2.  Select **Open Config** (opens `config.txt` in Notepad).
3.  Make your changes and save the file.
4.  Right-click the icon again and select **Reload Config**.

---

## Exit the Utility
To close TabSpool and remove all system hooks, right-click the system tray icon and select **Exit**.
