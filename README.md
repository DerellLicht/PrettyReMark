# PrettyReMark (PRM) revisited

A lightweight, tabbed Markdown viewer for Windows with live reload and syntax highlighting.

---
Summary of added features:
- Fixed image, html, `mailto:` links so they open in appropriate programs
- added version number to About dialog
- save/restore size and position of main window
- Implement Options dialog; make all program colors settable here;  
  Program colors are stored in `%appdata%\PrettyReMark\colors.json`
- Make program recall current cursor position in *each* file, across document changes 
  as well as program restarts.
- Make sidebar resizeable, add tooltips to files and paths
- Make tab bar optional, controled by Options dialog  

Here is its [Home page](https://derelllicht.42web.io/PrettyReMark.html)  
Download the [installer](https://github.com/DerellLicht/PrettyReMark/releases/latest) here  
See [Changelog](https://github.com/DerellLicht/PrettyReMark/blob/main/CHANGELOG.md) for the full revision history  
`PrettyReMark` uses the [MIT](https://github.com/DerellLicht/PrettyReMark/blob/main/LICENSE.MIT.txt) license; view the file here.  
Get the [PAD file](PrettyReMark.pad.xml) here

![Sample image](https://github.com/DerellLicht/PrettyReMark/blob/main/images/PRM.combined.image.jpg?raw=true)

---
### 10/16/26 new feature:  
Though `PrettyReMark` is only a viewer, it has the ability to load a file into a separate editor.  
Note that if a `.md` file is edited outside of PRM, any time the file is saved in the editor,
the file display will be automatically updated in PRM.

To access that function:
right-click on a filename in sidebar, opens a menu;  
The interesting item here is `Edit in Notepad` ...  
To change the editor that is used here, do the following:
1. Close `PrettyReMark`
2. Edit `%APPDATA%\PrettyReMark\settings.json`
Look for the line:  
  "EditorPath": "C:\\\WINDOWS\\\system32\\\notepad.exe",  
Change that to the path+filename of your preferred editor; for example:  
  "EditorPath": "C:\\\RJTextEd\\\TextEd.exe",  
NOTE: each of the backslashes must be doubled: `\\`  
3. Restart `PrettyReMark`; It should now use your specified editor

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

