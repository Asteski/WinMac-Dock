<p align="center">
  <img src="assets/logo.svg" alt="WinMac Dock" width="128">
</p>

<h1 align="center">
  <span>WinMac Dock</span>
</h1>
<h3 align="center">
  <span align="center">WinUI3 dock for Windows that looks and feels native</span>
</h3>
<p align="center">
  <span align="center">A standalone Windows 11 dock built with .NET 10, WinUI3, Windows Composition and the Windows shell. It offers native Windows Acrylic and Mica, solid/transparent backgrounds, system light/dark colors, multi-monitor setup, native WinUI menus, and macOS-style icon magnification.</span>
</p>

<p align="center">
  <a href="https://github.com/Asteski/WinMac-Dock/releases/download/v1.0.0/WinMac-Dock-1.0.0-x64-Setup.exe"><img alt="Download x64" src="https://img.shields.io/badge/Download-x64-0078D6?style=for-the-badge"></a>
  <a href="https://github.com/Asteski/WinMac-Dock/releases/download/v1.0.0/WinMac-Dock-1.0.0-arm64-Setup.exe"><img alt="Download ARM64" src="https://img.shields.io/badge/Download-ARM64-00A86B?style=for-the-badge"></a>
</p>
<p align="center">
  <img alt="Latest Release" src="https://img.shields.io/github/v/release/Asteski/WinMac-Dock">
  <img alt="Release Date" src="https://img.shields.io/github/release-date/Asteski/WinMac-Dock">
</p>
<p align="center">
  <img alt="Downloads (all releases)" src="https://img.shields.io/github/downloads/Asteski/WinMac-Dock/total">
  <img alt="Downloads (latest release)" src="https://img.shields.io/github/downloads/Asteski/WinMac-Dock/latest/total">
</p>

</p>
<p align="center">
  <img src="https://github.com/Asteski/WinMac-Dock/blob/3b64ea8fa7581786ee5bda0c204ddc84bfaeb117/img/img01.png" alt="WinMac Dock Light Mode">
</p>

<p align="center">
  <img src="https://github.com/Asteski/WinMac-Dock/blob/f12d496c8021cd875650275d6865c8c71e558030/img/img02.gif" alt="WinMac Dock Dark Mode">
</p>

**WinMac Dock is a customizable Windows 11 dock with macOS-style magnification, smooth animations, task grouping, window controls, and flexible positioning and alignment on any screen edge or display.**

- **Apps, files, folders and modules:** Pin applications (including _PWA_), folders, shortcuts and separators. Built-in modules include _Downloads, Recycle Bin, Start Menu, Task View_ and _Search_, with app-specific commands for _File Explorer, Settings, Microsoft Store, Terminal, Edge, Xbox_, _Snipping Tool_ and _Task Manager_.

- **Appearance:** Customize icon sizes, magnification, spacing, margins, borders, highlights, running indicators, and light/dark appearance. Choose _Acrylic_, _Mica_, _solid_ or _transparent_ backgrounds, or use _Default_ to match the Windows taskbar’s native background material.

- **Positioning and displays:** Place the dock along the top, bottom, left or right edge; adjust alignment, float it away from the edge, or span the full edge. Show it on one display or across all monitors, with independent positioning and behavior per display.

- **Window management:** Activate, minimize, restore or close windows from the dock. Optionally group windows by application, display only minimized applications, and direct Windows minimize/restore animations toward their dock icons.

- **Visibility:** Configure automatic hiding, edge reveal, fullscreen behavior and exclusions, screen-space reservation, and a customizable show/hide keyboard shortcut.

- **Folders and Downloads:** Browse folder contents in grid, list, or menu views, with sorting and folder-stack previews. Automatically pin newly downloaded files to a separate dock section, limit how many are retained, and optionally unpin them after opening. Automatically pin attached external disks, USB sticks and memory cards to a separate dock section.
  
- **Recycle Bin:** Drop files and folders onto its icon to recycle them, empty the bin from its context menu, and optionally play sounds for both actions. Customize its empty and full icons separately.

- **Per-item customization:** Change names, icons, launch arguments, working directories, and supported administrator-launch options. Customized item and module properties are remembered when unpinned and restored when pinned again.

- **Custom icons:** Use _ICO, PNG, JPG/JPEG, BMP, GIF, WebP_ or _SVG_ images, or select icons embedded in _DLL_ and _EXE_ files. Assign separate icons for light and dark mode.

- **Exclusions:** Exclude any running app from appearing in the dock by window title, process name, or window class.

- **Task View integration:** Keep the dock visible during Task View using the optional [Windhawk bridge mod](https://github.com/Asteski/WinMac-Dock/blob/a1e8460b41cd09a1d9ab7bce7c2282145a4a7200/mods/winmac-dock-task-view.wh.cpp).

## Installation

Windows 11 build **22621 or newer** is required. Choose **x64** for Intel/AMD PCs or **ARM64** for Windows on ARM.

- **NSIS installer:** choose a per-user or per-computer installation, with optional Start menu and desktop shortcuts. Per-computer installation requires administrator permission. The final page can launch the dock.
- **MSIX package:** install the package matching your architecture through Windows App Installer. For the privately signed packages, trust the supplied `WinMac-Dock-1.0.0.cer` certificate under **Local Computer > Trusted Root Certification Authorities** before installation. The package's signing certificate is issued to **WinMac Dock** by **Asteski**. Uninstall through Windows Settings.
- **Portable package:** extract the matching ZIP and run `WinMac.Dock.exe`. Keep the published files and folders together.

Published builds are self-contained: a separate .NET installation is not required. Building from source requires the SDKs listed below.

Running the executable again opens Settings or activates the existing Settings window. Right-click the dock background or use its tray menu to access Settings and other commands.

### Installer commands

The NSIS installer supports silent installation and command-line help. Run the actual installer filename with `/HELP` or `/?` for its supported options.

```powershell
# Silent per-user installation; create the default shortcuts and then launch.
& '.\WinMac-Dock-1.0.0-x64-Setup.exe' /S /RUN

# Silent per-computer installation; requires elevation.
& '.\WinMac-Dock-1.0.0-x64-Setup.exe' /S /Machine

# Show installer or uninstaller help.
& '.\WinMac-Dock-1.0.0-x64-Setup.exe' /HELP
& "$env:LOCALAPPDATA\WinMac\Dock\Uninstall.exe" /HELP

# Silent uninstall while retaining user settings and the startup task.
& "$env:LOCALAPPDATA\WinMac\Dock\Uninstall.exe" /S /KEEP
```

The app stays closed after installation unless you select the launch option or supply `/RUN`. Fresh installations enable Start menu and desktop shortcuts by default. Re-running Setup detects an existing installation and offers maintenance actions. Uninstall stops the installed dock before removing its files. Configuration retention is selected interactively; silent uninstall removes accessible user configuration and its startup task unless `/KEEP` is supplied.

## Using the dock

- Pin applications, shortcuts, files, and folders by dragging them onto the dock or using its context menu. Removing a pin does not delete its target.
- Drag pinned items to reorder them. Lock icon positions to prevent rearranging and pinning by dropping. The optional Shift requirement controls dragging an item outside the dock to remove it.
- Click running applications to activate or restore their windows. Default clicks can minimize an active window; middle-click requests a normal close. Configure individual or default mouse-button actions in Settings or an item's context menu.
- Group windows of the same application, or keep separate window items. Combining pinned launchers with running windows is a separate setting. Windows from other virtual desktops are excluded unless **Show items from all virtual desktops** is enabled.
- **Minimize to dock** supplies Windows with the corresponding icon's minimize/restore destination. **Show minimized applications only** filters unpinned windows and requires Minimize to dock. Pinned launchers remain available.
- Use folder **Grid**, **List**, or native **Menu** views, with per-folder sorting, icon size, grid dimensions, and stacked previews. Custom light/dark icons and SVG artwork are supported.
- Downloads, connected drives, screenshots, and screen recordings can appear automatically. Configure these through Downloads, File Explorer, and Snipping Tool settings. **Automatic item order** controls the order of automatic groups and running windows after pinned items.
- Recycle Bin supports empty/full icons, confirmation, separate recycling/emptying sounds, playback previews, and volume controls up to 200%.

### Modules, spacers, and separators

**Dock items > Modules** contains Start menu, Search, Task View, Downloads, and Recycle Bin. **Folders** and **Applications** provide settings for individual items; Applications separates installed apps from system components. Item preferences are remembered when unpinned and restored when pinned again.

Start menu, Search, and Task View support default Windows animated icons or custom light/dark icons, with a separate animation switch for each module. Start menu also offers monochrome or colorful, rounded or square presets. Search offers **Default** and **Modern Search**. Native animation availability depends on the installed Windows version. These modules use native Windows actions rather than simulated keyboard shortcuts.

Spacers create blank item slots. Their context menus can add another spacer to either side, or above/below on vertical docks. **Expand spacer** fills available dock length and shrinks as items are added; its sizing leaves room for magnification. Separators cannot be added at the dock's ends or beyond the available interior gaps. Removing an end pin also removes a separator left at that edge. An otherwise empty dock retains one blank item-sized slot.

### Appearance and placement

Place the dock at the bottom, top, left, or right, and choose one display or **All Monitors**. Displays can remember separate placement and visibility settings. Crowded docks reduce icon size to fit, then use scrolling when necessary; the preferred size returns when space is available.

**Space between icons** ranges from **−16 to 8 px**. **Gap from screen edge**, in Effects, ranges from **−12 to 12 px**. Screen-edge spanning can fill the available edge; its related settings remain editable while spanning is disabled.

Choose Default, None, Acrylic, or Mica backgrounds, with default, Windows accent, or custom tint. Theme, transparency, and high-contrast settings affect the resulting material. Tray icons support Default, Monochrome, or Windows accent color and update when the theme/accent changes.

Running, grouped-window, and automatically pinned item indicators have independent visibility controls. Customize their shapes, colors, and positions. **Pill-shaped indicator size** offers Small, Medium, and Large lengths for focused application pills; unfocused indicators retain their normal length. Automatic item indicators offer Default, Match item indicators, Pill, and Circle. Default keeps its fixed length; matching or explicit pills follow the selected pill size.

Window thumbnails can show or hide application badges. Explorer location icons are controlled separately. Icon labels, file extensions, highlights, and highlight style are also configurable.

### Visibility and fullscreen applications

Choose Always visible, automatic hiding after a delay, hiding for maximized applications, hiding on active-window overlap, or screen-edge toggling. Mouse reveal options control how the pointer reveals a hidden dock. Start hidden and reservation are configured in Visibility.

**Animate dock on startup** is enabled by default. A dock that starts visible slides in from its screen edge using the reveal animation. Hidden startup and disabled/reduced animations are respected.

While hidden behind a fullscreen app, the dock automatically pauses background refreshes and animations. Deferred changes refresh when it returns. **Fullscreen exclusions** retain normal background updates and allow reveal by shortcut or screen edge. **Allow dock above fullscreen applications**, off by default, enables reveal for all fullscreen apps. The dock still hides using the configured visibility delays. **Hide immediately for fullscreen applications** and **Hide immediately when an allowed fullscreen app becomes active** are enabled by default. The latter hides without delay or animation on fullscreen activation while retaining shortcut and screen-edge reveal.

Fullscreen exclusions allow the dock to be revealed above matching fullscreen applications; they do not keep it permanently visible. Separate process exclusions match window titles, executable names, and window classes. Enter process-exclusion rules separated by semicolons; `*` matches any number of characters and `?` matches one character. Pinned launchers remain available.

**Hide Windows taskbar**, in Effects, saves the Windows taskbar's placement and auto-hide preference, moves it to the bottom when necessary, enables auto-hide, and hides it. Turning the option off or exiting restores the saved settings. Recovery data supports restoration after an interrupted exit.

**Show dock in Task View** is disabled by default. Enabling it requires Windhawk and the **WinMac Dock Task View Bridge** mod. Its source is included in [integrations/windhawk/winmac-dock-task-view.wh.cpp](integrations/windhawk/winmac-dock-task-view.wh.cpp); published packages also include an `integrations` folder.

### Mouse shortcuts

- **Alt+drag:** Rearrange items while items positions are locked.
- **Alt+right-click** open Dock's main menu or separator sub-menu.
- **Ctrl+click:** Minimize or restore all windows belonging to the clicked application.
- **Shift+click:** Launch the item again, opening another window where supported.
- **Ctrl+Shift+click:** Request an administrator launch where supported.

## Commands and configuration

```powershell
& '.\WinMac.Dock.exe' --settings
```

Supported app commands are `--startup`, `--show`, `--hide`, `--toggle`, `--settings`, `--about`, `--reload`, `--elevate`, `--exit`, and `--sync-startup`. Explicit commands are forwarded to an existing instance. A second launch without arguments opens Settings. Elevation uses Windows UAC.

The default activation shortcut is **Ctrl+Alt+D**; it can be changed or disabled. **Run at startup** registers a per-user Task Scheduler logon task named **WinMac Dock**, without a configured startup delay. `--sync-startup` repairs its registration from the saved preference.

Configuration is stored at `%LOCALAPPDATA%\WinMac\Dock\settings.json`. Saves replace it atomically and retain a backup. Existing preferences are retained; new options receive their defaults when missing from older configurations.

Logs are stored beside the configuration as `WinMac.Dock.log` and `WinMac.Dock.old.log`, with up to 1,000 physical lines per file. Windows can restrict interaction with elevated applications when the dock runs without administrator permission.
