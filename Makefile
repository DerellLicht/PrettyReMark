# PrettyReMark -- Makefile (replaces rebuild.cmd)
# Wraps dotnet publish + Inno Setup for building, and gh for GitHub releases.
# (Moved from GitLab/glab to GitHub/gh, Oct 2026, because the winget-pkgs
# validation pipeline does not whitelist GitLab download URLs.)
# VERSION comes from CHANGELOG.md and feeds dotnet publish, Inno Setup, and
# the release/tag names -- no separate generated version file.

# Most recent "## [x.y]" header in CHANGELOG.md, e.g. "## [1.16]" -> 1.16
VERSION := $(shell grep -oE '\[[0-9]+\.[0-9]+\]' CHANGELOG.md | head -n 1 | tr -d '[]')

SETUP_EXE = Output/PrettyReMarkV$(VERSION).setup.exe
SETUP_ZIP = Output/PrettyReMarkV$(VERSION).setup.zip
TAG        = v$(VERSION)

# Explicit -R keeps gh from guessing the target repo (it otherwise infers it
# from the git remotes). The GitLab-only "percent-encoded repo id" variable
# is gone: gh takes plain "owner/repo" everywhere, and the glab-api asset-
# link juggling it supported is replaced by "gh release upload --clobber".
GH_REPO = DerellLicht/PrettyReMark

.PHONY: single setup dist release update retag re-release check-clean clean install

# Self-contained single-exe build.
single:
	rm -rf bin obj
	dotnet publish -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -p:IncludeAllContentForSelfExtract=true -p:Version=$(VERSION)

# Builds the installer. Depends on "single" so setup/dist/release/update always
# package a fresh build.
setup: single
	rm -rf Output
	iscc /DMyAppVersion=$(VERSION) /Q PrettyReMark.iss

dist: setup
	zip -j $(SETUP_ZIP) $(SETUP_EXE)

# Blocks release/retag on an unclean working tree -- catches building
# from a tree that doesn't match what the tag is about to point at. 
# Runs before the expensive rebuild.
check-clean:
	@if [ -n "$$(git status --porcelain)" ]; then \
		echo "ERROR: uncommitted changes present -- commit before releasing."; \
		git status --short; \
		exit 1; \
	fi

# First-time release: tag, push, create the GitHub release with notes sliced
# from CHANGELOG.md. Fails at "git tag" if $(TAG) already exists locally
# (e.g. it rode along with "git push --all/--tags" during the GitLab->GitHub
# move) -- in that case use "make update", which creates the release if
# it is missing.
release: check-clean dist
	@echo Preparing GitHub release $(TAG)...
	sed -n '/## \[$(VERSION)\]/,/## \[/p' CHANGELOG.md | sed '$$d' > temp_notes.md
	git tag $(TAG)
	git push origin $(TAG)
	gh release create $(TAG) $(SETUP_ZIP) --title "$(TAG)" --notes-file temp_notes.md -R $(GH_REPO)
	rm temp_notes.md
	@echo Release $(TAG) uploaded to GitHub!

# Updates (or creates, if missing) the release for $(TAG). Self-healing:
# "gh release view" succeeds only if the release exists, so we edit its notes
# if it does and create it (with the asset) if it doesn't. Then the asset is
# uploaded with --clobber, which replaces a same-named asset in place -- this
# is what replaced the old glab "find stale link via api + jq, delete it,
# re-upload" dance, since gh supports overwrite natively. When creating a
# release for a tag that is not yet on the remote, gh would create the tag
# itself from the default branch's HEAD; use "make release" or "make retag"
# first if the tag needs to point somewhere specific.
update: check-clean dist
	@echo Updating release $(TAG)...
	sed -n '/## \[$(VERSION)\]/,/## \[/p' CHANGELOG.md | sed '$$d' > temp_notes.md
	@if gh release view $(TAG) -R $(GH_REPO) >/dev/null 2>&1; then \
		echo "Release $(TAG) exists -- updating notes."; \
		gh release edit $(TAG) --notes-file temp_notes.md -R $(GH_REPO); \
	else \
		echo "Release $(TAG) not found -- creating it."; \
		gh release create $(TAG) --title "$(TAG)" --notes-file temp_notes.md -R $(GH_REPO); \
	fi
	rm temp_notes.md
	gh release upload $(TAG) $(SETUP_ZIP) --clobber -R $(GH_REPO)
	@echo Release $(TAG) assets and notes updated on GitHub!

# Recovery for "released with pending changes left out of the tag": force-
# moves $(TAG) to HEAD and re-pushes, then "update" re-releases against it.
# Solo-repo only -- force-pushing a moved tag is unsafe if anyone else has
# already fetched it. Deliberately separate from "release", which uses a
# bare (non -f) "git tag" as a guard rail.
retag: check-clean
	@if git rev-parse $(TAG) >/dev/null 2>&1; then \
		echo "Retagging existing tag $(TAG)."; \
	else \
		echo "Note: $(TAG) doesn't exist yet -- this will be its first release."; \
	fi
	git tag -f $(TAG)
	git push origin $(TAG) --force

re-release: retag update
	@echo Release $(TAG) retagged and re-released.

clean:
	rm -rf bin obj Output

# Silent install from base folder.
install:
	$(SETUP_EXE) /SILENT /SUPPRESSMSGBOXES /NORESTART

sha256:
	certutil -hashfile $(SETUP_ZIP) SHA256

# --- Static analysis / linting ------------------------------------------
# Roslynator (C#), vnu.jar (HTML), stylelint (CSS), eslint (JS, via
# eslint-plugin-html on index.html). Each has its own target; "lint" runs
# all four, output goes to reports\. Nonzero exit = issues found, not a
# make failure -- lint-cs ignores it (roslynator's own report is clear
# either way); lint-html/css/js capture it and print/log a pass/fail line,
# since those three are otherwise silent on a clean run (replaces the old
# lint-all.ps1, which existed only to work around make aborting on this).
#
# One-time setup (per machine):
#   - roslynator: dotnet tool install -g roslynator.dotnet.cli
#   - vnu.jar: see build_tools.md, point VNU_JAR below at it
#   - node + npm install -g eslint eslint-plugin-html stylelint stylelint-config-standard
#   - .stylelintrc.json + eslint.config.js in repo root (provided alongside
#     this Makefile) -- eslint-plugin-html must be registered in
#     eslint.config.js itself; a bare --plugin flag doesn't work under
#     ESLint's flat config.

VNU_JAR = ../tools/vnu.jar

.PHONY: lint lint-cs lint-html lint-css lint-js roslyn

lint: lint-cs lint-html lint-css lint-js
	@echo Lint reports written to reports\

reports:
	mkdir -p reports

lint-cs: | reports
	-roslynator analyze PrettyReMark.csproj --output reports/csharp.xml --verbosity normal

lint-html: | reports
	@java -jar $(VNU_JAR) assets/index.html > reports/html.txt 2>&1; \
	status=$$?; \
	if [ $$status -eq 0 ]; then \
		msg="vnu.jar: no issues found"; \
	else \
		msg="vnu.jar: issues found (exit $$status) -- see reports/html.txt"; \
	fi; \
	echo "$$msg" >> reports/html.txt; \
	echo "$$msg"

lint-css: | reports
	@stylelint --no-color assets/*.css > reports/css.txt 2>&1; \
	status=$$?; \
	if [ $$status -eq 0 ]; then \
		msg="stylelint: no issues found"; \
	else \
		msg="stylelint: issues found (exit $$status) -- see reports/css.txt"; \
	fi; \
	echo "$$msg" >> reports/css.txt; \
	echo "$$msg"

lint-js: | reports
	@eslint --no-color assets/index.html > reports/js.txt 2>&1; \
	status=$$?; \
	if [ $$status -eq 0 ]; then \
		msg="eslint: no issues found"; \
	else \
		msg="eslint: issues found (exit $$status) -- see reports/js.txt"; \
	fi; \
	echo "$$msg" >> reports/js.txt; \
	echo "$$msg"
