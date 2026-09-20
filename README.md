# PrettyReMark revisited

A lightweight, tabbed Markdown viewer for Windows with live reload and syntax highlighting.

---
Summary of added features:
- Fixed image, html, `mailto:` links so they open in appropriate programs
- added version number to About dialog
- save/restore size/position of dialog
- Implement Options dialog; make all program colors settable here;  
  Program colors are stored in `%appdata%\PrettyReMark\colors.json`
- Make program recall current cursor position in *each* file, across document changes 
  as well as program restarts.
- Make sidebar resizeable, add tooltips to files and paths
- Make tab bar optional, controled by Options dialog  

Here is its [Home page](https://derelllicht.42web.io/PrettyReMark.html)  
Download the [installer](https://gitlab.com/DerellLicht/pretty-mark/-/releases/v1.08/downloads/PrettyReMarkV1.08.setup.zip) here  
See [Changelog](CHANGELOG.md) for the full revision history  
`PrettyReMark` uses the [MIT](LICENSE.MIT.txt) license; view the file here.  
Get the [PAD file](PrettyReMark.pad.xml) here

---
Notes on this revisited version of `PrettyReMark` ...  
I did the design specification, testing, and guidance on the revisions in this fork of the program.

However: [Claude AI](https://claude.ai/new) did *all* of the coding and interpretation of the existing code.
Claude is truly amazing; he implements complex designs in seconds, and they generally 
do exactly what was requested... but debugging and analysis still require a human.
The two of us together, can do almost *anything* !!

---
***Notes on build tools used to build/maintain this program***  
A variety of tools are called upon to build and maintain `PrettyReMark` ;  
They are documented in [build tools](build_tools.md) file.  

---
This program is derived from, and expands upon, 
[PrettyMark](https://gitlab.com/eagle1/prettymark),
created by 
[Gianluca Zamagni, aka @eagle1](href="https://gitlab.com/eagle1")  

Existing history and readme for original program are available 
[here](https://gitlab.com/eagle1/prettymark/-/blob/main/README.md?ref_type=heads)

