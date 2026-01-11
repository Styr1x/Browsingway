# Browsingway IPC API

This document describes the Dalamud IPC interface for interacting with Browsingway from other plugins.

## Overview

Browsingway exposes IPC functions that allow other plugins to:

- Create and manage ephemeral overlays (session-only, not saved to config)
- Add overlays to the persistent configuration

All functions use JSON for input/output to ensure forward compatibility.

The current API Version is **1**.

## Permissions

IPC features are gated by user configuration and are enabled by default:

- **Ephemeral Overlays**: Allows the creation and management of ephemeral overlays.
- **Config Changes**: Allows for Plugins to add overlays to the persistent configuration.

`Browsingway.GetInfo` will return the current permissions.

## Response Format

All functions that accept input return a standard response format:

```json
{
  "success": true,
  "error": "Error message if success is false",
  "...": "Additional fields depending on the function"
}
```

## Functions

### Browsingway.GetInfo

Returns API version and permissions.

**Response**:

```json
{
  "apiVersion": 1,
  "canCreateEphemeralOverlays": true,
  "canAddOverlaysToConfig": true
}
```

**Example**:

```csharp
var getInfo = PluginInterface.GetIpcSubscriber<string>("Browsingway.GetInfo");
var json = getInfo.InvokeFunc();
```

---

### Browsingway.Overlay.Create

Creates an ephemeral overlay that exists only for the current session. Not saved to config and removed when the plugin unloads.

**Request**:

| Field                | Type   | Required | Default | Description                                                                    |
|----------------------|--------|----------|---------|--------------------------------------------------------------------------------|
| `url`                | string | Yes      | -       | URL to load.                                                                   |
| `screenPositionMode` | string | No       | System  | Sets how the window coordinates and sizes are interepreted. See remarks below. |
| `x`                  | float  | No       | -       | Horizontal position. Can also be an offset (negative or positive).             |
| `y`                  | float  | No       | -       | Vertical position. Can also be an offset (negative or positive).               |
| `width`              | float  | No       | -       | Width.                                                                         |
| `height`             | float  | No       | -       | Height.                                                                        |
| `opacity`            | float  | No       | 100     | Opacity, 0-100.                                                                |
| `zoom`               | float  | No       | 100     | Zoom level.                                                                    |
| `muted`              | bool   | No       | false   | Mute audio.                                                                    |
| `clickThrough`       | bool   | No       | false   | Ignore mouse and keyboard input.                                               | 
| `customCss`          | string | No       |         | CSS to inject on page load.                                                    |
| `customJs`           | string | No       |         | JavaScript to execute on page load.                                            |

**Screen Position Modes**:

- **System**: Dalamud's window coordinates.
- **Fullscreen**: Position and size are ignored.

The following modes use a percentage of the screen size (0-100%), starting from a mode specific anchor point:

- **TopLeft**: Anchored to the top left corner of the screen.
- **Top**: Anchored to the top center of the screen.
- **TopRight**: Anchored to the top right corner of the screen.
- **CenterLeft**: Anchored to the center left corner of the screen.
- **Center**: Anchored to the center of the screen.
- **CenterRight**: Anchored to the center right corner of the screen.
- **BottomLeft**: Anchored to the bottom left corner of the screen.
- **BottomCenter**: Anchored to the bottom center of the screen.
- **BottomRight**: Anchored to the bottom right corner of the screen.

**Response**:

```json
{
  "success": true,
  "guid": "aaaaaaaa-bbbb-dddd-eeee-ffffffffffff"
}
```

**Example**:

```csharp
var create = PluginInterface.GetIpcSubscriber<string, string>("Browsingway.Overlay.Create");
var request = JsonSerializer.Serialize(new {
    url = "https://dalamud.dev",
});
var response = create.InvokeFunc(request);
```

---

### Browsingway.Overlay.Remove

Removes an ephemeral overlay.

**Request**:

| Field  | Type   | Required | Description                    |
|--------|--------|----------|--------------------------------|
| `guid` | string | Yes      | GUID of the overlay to remove. |

**Response**:

```json
{
  "success": true
}
```

**Example (C#)**:

```csharp
var remove = PluginInterface.GetIpcSubscriber<string, string>("Browsingway.Overlay.Remove");
var request = JsonSerializer.Serialize(new { guid = "aaaaaaaa-bbbb-dddd-eeee-ffffffffffff" });
var response = remove.InvokeFunc(request);
```

---

### Browsingway.Overlay.Control

Controls an ephemeral overlay. Multiple actions can be performed at once, depending on which fields are set.

**Request**:

| Field           | Type   | Required | Description                     |
|-----------------|--------|----------|---------------------------------|
| `guid`          | string | Yes      | GUID of the overlay to control. |
| `navigate`      | string | No       | Navigate to this Url.           |
| `reload`        | bool   | No       | Reload the current page.        |
| `injectCss`     | string | No       | Inject CSS into the page.       |
| `executeJs`     | string | No       | Execute JavaScript in the page. |
| `setVisibility` | string | No       | Set visibility: `hide`, `show`  | 

**Response**:

```json
{
  "success": true
}
```

**Examples**:

Navigate to URL:

```csharp
var control = PluginInterface.GetIpcSubscriber<string, string>("Browsingway.Overlay.Control");
var request = JsonSerializer.Serialize(new {
    guid = "aaaaaaaa-bbbb-dddd-eeee-ffffffffffff",
    navigate = "navigate",
    value = "https://dalamud.dev"
});
var response = control.InvokeFunc(request);
```

Set visibility:

```csharp
var control = PluginInterface.GetIpcSubscriber<string, string>("Browsingway.Overlay.Control");
var request = JsonSerializer.Serialize(new {
    guid = "aaaaaaaa-bbbb-dddd-eeee-ffffffffffff",
    setVisibility = "hide"
});
var response = control.InvokeFunc(request);
```

Execute JavaScript:

```csharp
var control = PluginInterface.GetIpcSubscriber<string, string>("Browsingway.Overlay.Control");
var request = JsonSerializer.Serialize(new {
    guid = "aaaaaaaa-bbbb-dddd-eeee-ffffffffffff",
    executeJs = "document.body.style.background = 'red';"
});
var response = control.InvokeFunc(request);
```

---

### Browsingway.Config.Add

Adds a new overlay to the persistent configuration. When calling this function the user will be notified by opening the overlay settings and selecting the newly added overlay.

**Request**:

| Field                | Type     | Required | Default | Description                                                                                                    |
|----------------------|----------|----------|---------|----------------------------------------------------------------------------------------------------------------|
| `url`                | string   | Yes      | -       | URL to load.                                                                                                   |
| `name`               | string   | Yes      | -       | A unique name for this overlay. If an overlay with this name already exists an error is returned.              |
| `screenPositionMode` | string   | No       | System  | Sets how the window coordinates and sizes are interepreted. See remarks below.                                 |
| `x`                  | float    | No       | -       | Horizontal position. Can also be an offset (negative or positive).                                             |
| `y`                  | float    | No       | -       | Vertical position. Can also be an offset (negative or positive).                                               |
| `width`              | float    | No       | -       | Width.                                                                                                         |
| `height`             | float    | No       | -       | Height.                                                                                                        | 
| `opacity`            | float    | No       | 100     | Opacity, 0-100.                                                                                                |
| `zoom`               | float    | No       | 100     | Zoom level.                                                                                                    |
| `muted`              | bool     | No       | false   | Mute audio.                                                                                                    |
| `locked`             | bool     | No       | false   | Lock the overlay (can't be moved).                                                                             |
| `typeThrough`        | bool     | No       | false   | Ignore keyboard input.                                                                                         |
| `clickThrough`       | bool     | No       | false   | Ignore mouse and keyboard input (also means the overlay can't be moved).                                       | 
| `baseVisibility`     | string   | No       | visible | Initial visibility (before rules are applied): `visible`, `hidden`, `disabled`.                                |
| `visibilityRules`    | object[] | No       | -       | See remarks below for object definitions. The order in the array is the evaluation order of the rules as well. |
| `customCss`          | string   | No       |         | CSS to inject on page load.                                                                                    |
| `customJs`           | string   | No       |         | JavaScript to execute on page load.                                                                            |

**Visibility Rules object definition**:

| Field     | Type   | Required | Default | Description                                  |
|-----------|--------|----------|---------|----------------------------------------------|
| `trigger` | string | Yes      | -       | One of: `actAvailable`, `inCombat`, `inPvp`. |
| `action`  | string | Yes      | -       | One of: `show`, `hide`, `disable`, `enable`. |
| `enabled` | bool   | No       | false   | Enables this rule.                           |
| `negated` | bool   | No       | false   | Negates this rule.                           |
| `delay`   | int    | No       | 0       | Delay in seconds.                            |

**Response**:

```json
{
  "success": true
}
```

**Example**:

```csharp
var add = PluginInterface.GetIpcSubscriber<string, string>("Browsingway.Config.Add");
var request = JsonSerializer.Serialize(new {
    name = "My_Overlay",
    url = "https://dalamud.dev"
});
var response = add.InvokeFunc(request);
```

---

## Version History

### API Version 1

- Initial release
