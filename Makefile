# PrettyReMark -- Makefile (replaces rebuild.cmd)
# Wraps dotnet publish + Inno Setup for building, and glab for GitLab releases.
# VERSION comes from CHANGELOG.md and feeds dotnet publish, Inno Setup, and
# the release/tag names -- no separate generated version file.

# Most recent "## [x.y]" header in CHANGELOG.md, e.g. "## [1.16]" -> 1.16
VERSION := $(shell grep -oE '\[[0-9]+\.[0-9]+\]' CHANGELOG.md | head -n 1 | tr -d '[]')

SETUP_EXE = Output/PrettyReMarkV$(VERSION).setup.exe
SETUP_ZIP = Output/PrettyReMarkV$(VERSION).setup.zip
TAG        = v$(VERSION)

# Explicit -R avoids glab's "which is the base repository?" prompt (this repo
# is a fork of eagle1's original).
GLAB_REPO = DerellLicht/pretty-mark

# GLAB_REPO with "/" percent-encoded, for glab api's :id path segments
# (e.g. projects/<GLAB_REPO_ENC>/releases/...) -- "owner/repo" isn't accepted
# there directly the way it is with -R elsewhere.
GLAB_REPO_ENC = $(subst /,%2F,$(GLAB_REPO))

.PHONY: single setup dist release update retag re-release publish check-clean clean install

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

# Blocks release/retag/publish on an unclean working tree -- catches building
# from a tree that doesn't match what the tag is about to point at. Runs
# before the expensive rebuild.
check-clean:
	@if [ -n "$$(git status --porcelain)" ]; then \
		echo "ERROR: uncommitted changes present -- commit before releasing."; \
		git status --short; \
		exit 1; \
	fi

# First-time release: tag, push, create the GitLab release with notes sliced
# from CHANGELOG.md.
release: check-clean dist
	@echo Preparing GitLab release $(TAG)...
	sed -n '/## \[$(VERSION)\]/,/## \[/p' CHANGELOG.md | sed '$$d' > temp_notes.md
	git tag $(TAG)
	git push origin $(TAG)
	glab release create $(TAG) $(SETUP_ZIP) --notes-file temp_notes.md -R $(GLAB_REPO)
	rm temp_notes.md
	@echo Release $(TAG) uploaded to GitLab!

# Re-uploads the installer + notes to an EXISTING release (tag unchanged).
# Requires a release to already exist for $(TAG) -- see "publish" below if
# it doesn't. glab has no upload --clobber (re-uploading a same-named asset
# errors), so any stale link with this filename is looked up and deleted
# first, via glab api + jq. "tr -d '\r'" strips the CRLF glab emits on
# Windows, which otherwise breaks the id substitution.
update: dist
	@echo Updating assets for existing release $(TAG)...
	@for id in $$(glab api "projects/$(GLAB_REPO_ENC)/releases/$(TAG)/assets/links" \
	    | jq -r '.[] | select(.name=="$(notdir $(SETUP_ZIP))") | .id' | tr -d '\r'); do \
		echo Removing stale asset link $$id for $(notdir $(SETUP_ZIP))...; \
		glab api -X DELETE "projects/$(GLAB_REPO_ENC)/releases/$(TAG)/assets/links/$$id" >/dev/null; \
	done
	glab release upload $(TAG) $(SETUP_ZIP) -R $(GLAB_REPO)
	sed -n '/## \[$(VERSION)\]/,/## \[/p' CHANGELOG.md | sed '$$d' > temp_notes.md
	glab release create $(TAG) --notes-file temp_notes.md -R $(GLAB_REPO)
	rm temp_notes.md
	@echo Release $(TAG) assets and notes updated on GitLab!

# Recovery for "released with pending changes left out of the tag": force-
# moves $(TAG) to HEAD and re-pushes, then "update" re-releases against it.
# Solo-repo only -- force-pushing a moved tag is unsafe if anyone else has
# already fetched it. Deliberately separate from "release", which uses a
# bare (non -f) "git tag" as a guard rail.
retag: check-clean
	git tag -f $(TAG)
	git push origin $(TAG) --force

re-release: retag update
	@echo Release $(TAG) retagged and re-released.

# Recovery for "the tag is already pushed, but no GitLab release was ever
# created for it" -- e.g. running re-release/update before "release" was
# ever run once. Skips tagging (the tag's already right) and just builds +
# creates the release against it. Refuses if HEAD doesn't match $(TAG); use
# retag/re-release instead in that case.
publish: check-clean dist
	@if [ "$$(git rev-parse HEAD)" != "$$(git rev-parse $(TAG)^{})" ]; then \
		echo "ERROR: HEAD doesn't match tag $(TAG) -- use retag/re-release instead."; \
		exit 1; \
	fi
	sed -n '/## \[$(VERSION)\]/,/## \[/p' CHANGELOG.md | sed '$$d' > temp_notes.md
	glab release create $(TAG) $(SETUP_ZIP) --notes-file temp_notes.md -R $(GLAB_REPO)
	rm temp_notes.md
	@echo Release $(TAG) created on GitLab from existing tag!

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
