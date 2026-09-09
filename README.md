# PrettyReMark revisited

A lightweight, native Markdown viewer for Windows with live reload and syntax highlighting.

---
Summary of added features:
- Fixed links to image and html files, so they open in appropriate programs
- added version number to About dialog (see `AppVersion.cs`)
- moved program colors to `%appdata%\PrettyReMark\colors.json`
- save/restore size/position of dialog
- Implement Options dialog and menu link
- Make program recall current cursor position in *each* file, across document changes 
  as well as program restarts.

See [Changelog](CHANGELOG.md) for the full revision history.

---
Notes on this revisited version of `PrettyReMark` ...
I did the design specification, testing, and guidance on the revisions in this fork of the program.

However: https://claude.ai/new did *all* of the coding and interpretation of the existing code.
Claude is truly amazing; he implements complex designs in seconds, and they generally 
do exactly what was requested... but debugging and analysis still require a human.
The two of us together, can do almost *anything* !!

---
## Existing history and readme from original author:

[Original eagle1 readme](https://gitlab.com/eagle1/prettymark/-/blob/main/README.md?ref_type=heads)

## License

[MIT](LICENSE.txt)

