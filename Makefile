# PrettyReMark -- Makefile (replaces rebuild.cmd)
#
# Wraps dotnet publish + Inno Setup for building, and glab for GitLab releases.
# VERSION is scraped from the most recent CHANGELOG.md entry and threaded through
# to Inno Setup via /D, so PrettyReMark.iss no longer needs its version hardcoded
# (see the #ifndef MyAppVersion block that needs adding there).
#
# NOTE: AppVersion.cs is NOT yet synced to VERSION -- still a manual step for now.

# Pull the most recent version out of CHANGELOG.md, e.g. "## [1.07]" -> 1.07
# Assumes the same "## [x.y]" header format used in the wbigcalc project --
# adjust the regex below if PrettyReMark's CHANGELOG.md differs.
VERSION := $(shell grep -oE '\[[0-9]+\.[0-9]+\]' CHANGELOG.md | head -n 1 | tr -d '[]')

SETUP_EXE = Output/PrettyReMarkV$(VERSION).setup.exe
SETUP_ZIP = Output/PrettyReMarkV$(VERSION).setup.zip
TAG        = v$(VERSION)

# Explicit -R avoids glab's "which is the base repository?" prompt, since this
# repo is a fork of eagle1's original and glab otherwise asks each time.
GLAB_REPO = DerellLicht/pretty-mark

.PHONY: single setup dist release update clean

# rebuild.cmd's "single" target -- stand-alone self-extracting exe.
# (The old "build" target -- loose-file publish -- is gone: PrettyReMark.iss
# only ever pulled the single exe out of publish\, never the loose-file layout,
# so there was nothing depending on it.)
single:
	rm -rf bin obj
	dotnet publish -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -p:IncludeAllContentForSelfExtract=true

# Compile the installer. VERSION is passed to Inno Setup on the command line
# instead of being hardcoded in PrettyReMark.iss. Depends on "single" so a bare
# "make setup" (or "make dist"/"release"/"update") always packages a fresh build.
setup: single
	rm -rf Output
	iscc /DMyAppVersion=$(VERSION) /Q PrettyReMark.iss

dist: setup
	zip -j $(SETUP_ZIP) $(SETUP_EXE)

# Tag, push, and publish a new GitLab release with the installer attached and
# release notes sliced out of the current CHANGELOG.md entry.
release: dist
	@echo Preparing GitLab release $(TAG)...
	sed -n '/## \[$(VERSION)\]/,/## \[/p' CHANGELOG.md | sed '$$d' > temp_notes.md
	git tag $(TAG)
	git push origin $(TAG)
	glab release create $(TAG) $(SETUP_ZIP) --notes-file temp_notes.md -R $(GLAB_REPO)
	rm temp_notes.md
	@echo Release $(TAG) uploaded to GitLab!

# Re-upload the installer to an existing release (e.g. after a rebuild), without
# re-tagging. NOTE: unlike "gh release upload", "glab release upload" has no
# --clobber flag -- untested whether it silently overwrites a same-named asset
# or errors out. Verify this before relying on it for a real re-upload.
update: dist
	@echo Updating assets for existing release $(TAG)...
	glab release upload $(TAG) $(SETUP_ZIP) -R $(GLAB_REPO)
	@echo Release $(TAG) assets updated on GitLab!

clean:
	rm -rf bin obj Output

