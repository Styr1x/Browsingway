Browsingway
===========

Dalamud plugin for rendering browser overlays in-game.

Enables you to play in fullscreen (and G-SYNC) while having access to e.g. ACT overlays.

This project is a fork off ackwell's BrowserHost plugin (https://github.com/ackwell/BrowserHost). The original scope of the project was to add Endwalker support and nothing more. However since then a few new features and improvements have been added:

* DPI awareness
    * The displayed browser overlays are scaled correctly in regards to your display's dpi. This essentially means you get the same sized output as you get from your browser.
* Zoom support
    * Overlays can be zoomed in and out to make them smaller and bigger, same way your browser's zoom works.
* Framerate configurable
    * The rendering framerate for each individual overlay can now be set.
* Updated Chromium version
    * 96.0.4664.110
* Project cleanup
    * Everything now uses .net 5
    * Nullable enabled
    * DalamudPackager for easier deployment
    * Some source cleanups
* Minor stability improvements


### Future (Roadmap)
The initial release focused on making the overlays work again, the focus now rests on rewriting core parts to make the plugin more robust and easier to maintain.

You can also open an issue for new feature requests.
