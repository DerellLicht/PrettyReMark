# PrettyMark

A lightweight, native Markdown viewer for Windows with live reload and syntax highlighting.

![.NET 8](https://img.shields.io/badge/.NET-8.0-512BD4?logo=dotnet)
![Windows](https://img.shields.io/badge/platform-Windows-0078D6?logo=windows)
![License](https://img.shields.io/badge/license-MIT-green)

## Features

- **GitHub-style rendering** — tables, fenced code blocks, task lists, TOC via [marked.js](https://marked.js.org/)
- **Syntax highlighting** — automatic language detection via [highlight.js](https://highlightjs.org/) (GitHub theme)
- **Live reload** — preview updates instantly when the file is saved
- **Multi-tab** — open multiple files in a single window
- **Sidebar** — collapsible drawer with list of open files and paths
- **Drag & drop** — drop `.md` files directly into the window
- **Find in page** — `Ctrl+F` with match highlighting and navigation
- **Dark mode** — toggle with `Ctrl+D`, preference is remembered
- **Print** — `Ctrl+P` opens the native print dialog, prints only the rendered content
- **Recent files** — File → Recent Files submenu, remembers the last 10 opened files
- **Session persistence** — open tabs are restored when the app is relaunched
- **Multi-language** — 12 languages (EN, IT, ES, PT, FR, DE, ZH, JA, KO, RU, TR, UK), auto-detects system language, switchable at runtime via View → Language
- **Full screen** — `F11` toggle
- **Zoom** — `Ctrl++` / `Ctrl+-` / `Ctrl+0`
- **Native** — single `.exe`, no Electron, no browser required (uses WebView2/EdgeChromium)

---
## Existing history and readme from original author:

[original eagle1 readme](https://gitlab.com/eagle1/prettymark/-/blob/main/README.md?ref_type=heads)

## License

MIT
