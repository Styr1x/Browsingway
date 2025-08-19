Browsingway
===========

Dalamud plugin for rendering browser overlays in-game.

Enables you to play in fullscreen (and G-SYNC) while having access to e.g. ACT overlays.

This project is a fork off ackwell's BrowserHost plugin (https://github.com/ackwell/BrowserHost). The original scope of the project was to add Endwalker support and nothing more. However since then a few new features and improvements have been added:

* DPI awareness
    * The displayed browser overlays are scaled correctly in regards to your display's dpi. This essentially means you get the same sized output as you get from your browser.
* Zoom support
    * Overlays can be zoomed in and out to make them smaller and bigger, same way your browser's zoom works.
* Opacity support
    * Make your overlays as transparent as you like.
* Framerate configurable
    * The rendering framerate for each individual overlay can now be set.
* Disable support
    * Completly disable an overlay without deleting it.
* Mute support
   * Can mute specific overlays.
* ACT optimizations
   * Optimizes the overlay handling for ACT overlays. Also enables and disables them depending on if ACT itself is running.
* Updated Chromium version
    * 108.0.5359.125
* Project cleanup
    * Everything now uses .net 7
    * Nullable enabled
    * DalamudPackager for easier deployment
    * Some source cleanups
* Minor stability improvements


### Future (Roadmap)
The initial release focused on making the overlays work again, the focus now rests on rewriting core parts to make the plugin more robust and easier to maintain.

You can also open an issue for new feature requests.

## ACT support ##
For ACT overlays to work correctly the overlay WSServer has to be enabled. ACT also provides an URL generator that will create the correct URLs to use within Browsingway:

![image](https://user-images.githubusercontent.com/569324/148692825-f29e41ae-cec5-4144-974f-394e14ec108a.png)

You might also want to enable 'ACT optimizations' inside Browsingway for the specifc overlays, this will also enable and disable them automatically if ACT is running or not running.

## Linux Support ##

Using Browsingway in Linux is experimental and *not supported*. However, you may have some success if you try the following steps:

- Download the .NET Framework for Windows (not a typo) version 9.0.3, both [x64](https://builds.dotnet.microsoft.com/dotnet/Runtime/9.0.3/dotnet-runtime-9.0.3-win-x64.exe) and [x86](https://builds.dotnet.microsoft.com/dotnet/Runtime/9.0.3/dotnet-runtime-9.0.3-win-x86.exe) versions.
- In XIVLauncher's settings, navigate to the `Wine` tab, then click `Open Wine explorer (run apps in prefix)`.
- In the new window, find the two .NET installers you downloaded, and run them, leaving everything set to defaults. (You can also run the installers from the command line if you prefer, but make sure to set `WINEPREFIX=~/.xlcore/wineprefix` and use the Wine executable in `~/xlcore/compatibilitytool/wine/` that matches what you're trying to use with FFXIV.

### Troubleshooting
- Delete the contents of `~/.xlcore/pluginConfigs/Browsingway`. This should cause the plugin to reinstall CEF on next launch. This is most likely to help if you've switched Wine/Proton versions since installing CEF (XIVLauncher may do this on its own as part of its own updates, though this is rare.)
- If your Wine version has changed (see above), you may need to clear your Wine prefix. This can be done from the `Wine` tab in XIVLauncher's settings.
- After clearing your Wine prefix, you **will** need to repeat steps in which you install the .NET Framework.
