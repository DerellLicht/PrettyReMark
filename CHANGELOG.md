# PrettyReMark Changelog

## [1.12] - 2026-09-14
- placeholder for development version

## [1.11] - 2026-09-14
- running `roslynator` on `Program.cs` and resolving warnings
- added option to enable/disable tabs, default: disable
- modified `settings.json` to have each element on a separate line
- modify Makefile to allow recovery from release with pending commits
- modify Makefile to block `release` with pending commits

## [1.10] - 2026-09-10
- deal with `mailto:` links
- Make a global change to hand *all* unknown extensions to Windows' handlers
- try to get program to pop to top of window stack when loading a new `.md` file

## [1.09] - 2026-09-09
- Add tooltips to sidebar entries 
- Make width of sidebar resizeable and persistent
- Modify the build rule to manually regenerate AppVersion.cs from VERSION in CHANGELOG.md

## [1.08] - 2026-09-09
- convert rebuild.cmd to Makefile
- prepare for first release

## [1.07] - 2026-09-09
- Create installer via Inno Setup 7
- Update documents

## [1.06] - 2026-09-08
- renaming program to PrettyReMark, in preperation for distributing it

## [1.05] - 2026-09-08
- final code and layout tweaking

## [1.04] - 2026-09-08
- update documents (including README.md, LICENSE.txt)
- added colors and options to Options dialog

## [1.03] - 2026-09-07
- Implement Options dialog and menu link
- Make program recall current file position in *each* file, across document changes 
  as well as program restarts.

## [1.02] - 2026-09-07
- added version number to About dialog (see `AppVersion.cs`)
- moved main text colors to `colors.json`
- save/restore size/position of dialog

## [1.01] - 2026-09-07
- forked project into my workspace
- added fix for opening images and html files in appropriate programs
- added `rebuild.cmd` script to rebuild the project

## [1.00] - 2026-03-07
- original release
