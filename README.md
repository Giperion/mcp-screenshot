# screenshot MCP

Local Windows MCP: capture the screen or a window, query window bounds, and move focus.

One file, Python standard library only, no Puppeteer/npm. The PNG is returned in the tool result — nothing is written to disk.

Russian: [README_RU.md](README_RU.md)

## Tools

| Tool | Purpose |
|------|---------|
| `screenshot_screen` | Primary monitor; `monitor` (1, 2, …) or `all_monitors` for every display; or a region `x/y/width/height` |
| `screenshot_window` | Window by `hwnd`, `title` substring, or process name |
| `find_window` | Visible windows with rectangles; no filter lists all of them |
| `focus_window` | Bring a window to the foreground (and restore it if minimized) |

Prefer `hwnd` from `find_window`. If `title`/`process` matches more than one window, the tool errors and asks for `hwnd`.

PNGs are scaled to 1920px wide by default (`max_width`). `0` keeps native resolution.

## Run without Cursor

Python 3.10+ (tested on 3.12). No dependencies.

```bat
python server.py --list
python server.py --screenshot test.png
```

With no arguments the process speaks MCP over stdio.

## Cursor

In the client's `mcp.json`:

```json
{
  "mcpServers": {
    "screenshot": {
      "command": "C:\\Program Files\\Python312\\python.exe",
      "args": ["-u", "E:/EUREKA/mcp_screenshot/server.py"]
    }
  }
}
```

Reload MCP / Cursor after adding it.

## Notes

- Window capture uses on-screen pixels (what you see). If the window is covered, call `focus_window` first or pass `bring_to_front: true`.
- A minimized window cannot be captured until it is restored.
- Windows only.
