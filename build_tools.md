## Summary of tools used for building or otherwise managing `PrettyReMark`

### Cygwin toolchain
`https://cygwin.com/`  
tools provided: `make`, `sed`, `tr`, `head`, `tail`, `rm`, other *nix utilities

### Git for Windows - command-line utility
`https://gitforwindows.org/`  
tools provided: `git`

### GitHub CLI
`https://github.com/cli/cli`  
tools provided: `gh`  
***Usage notes:***  
After installation, find `gh.exe` in folder `C:\Program Files\GitHub CLI`,
copy it to somewhere in your path.

### GitLab CLI
`https://docs.gitlab.com/cli/`  
tools provided: `glab`  
***Usage notes:***  
After installation, find `glab.exe` in folder `C:\Users\dan7m\AppData\Local\Programs\glab`,
copy it to somewhere in your path.

### Inno Setup 7 - installer generator
`https://jrsoftware.org/isinfo.php`  
tools provided: `icss`  

### Lint setup — Roslynator + vnu.jar + stylelint + eslint
`make lint` in the `PrettyReMark` folder runs all four tools below back to
back and leaves each one's raw output in `reports\` (`reports/csharp.xml`,
`reports/html.txt`, `reports/css.txt`, `reports/js.txt`). Each also has its
own standalone target (`make lint-cs`, `make lint-html`, `make lint-css`,
`make lint-js`) so you can run just one. `make roslyn` still works as an
alias for `make lint-cs`.

#### roslynator - C# code validation
This is an addon package for `dotnet` toolchain.  
install: `dotnet tool install -g roslynator.dotnet.cli`

One-time setup: add the analyzer package to PrettyMark's .csproj as a 
dev-only reference (it won't ship in your published output):
```css
<ItemGroup>
  <PackageReference Include="Roslynator.Analyzers" Version="4.*">
    <PrivateAssets>all</PrivateAssets>
    <IncludeAssets>runtime; build; native; contentfiles; analyzers</IncludeAssets>
  </PackageReference>
</ItemGroup>
```
(This also means dotnet build itself will now show Roslynator warnings inline, 
for free, not just when you run the CLI.)

#### vnu.jar - HTML validation (Nu Html Checker)
`https://validator.github.io/validator/`  
Needs a Java runtime (JRE) on PATH — Eclipse Adoptium works fine.  
Shared, not per-project: lives in `..\tools\vnu.jar` relative to each
repo (i.e. one level up, alongside the repo checkouts), not copied into
every project. The Makefile's `VNU_JAR` variable points at it.

#### stylelint - CSS validation
install: `npm install -g stylelint stylelint-config-standard`  
one-time setup: drop the following `.stylelintrc.json` in the repo root —
with no config file present, stylelint exits (code 78) immediately instead
of just reporting zero findings.  
```
{
  "extends": "stylelint-config-standard"
}
```

**Confirm it's actually there** before chasing anything fancier: `dir /a
.styl*` from the repo root (or `ls -la .stylelintrc.json` from Cygwin
bash). The exit-78 "No configuration provided" error looks the same
whether the file is missing, misnamed, or genuinely broken — and it's
easy for a dotfile to just not get saved in the first place (no extension
for Explorer to hang onto, easy to lose track of whether a save actually
went through). Check existence first, only look at content second.

#### eslint - JS validation (via eslint-plugin-html, since the JS lives inline in index.html)
install (CLI only): `npm install -g eslint`  
**Do NOT install eslint-plugin-html with `-g`.** `eslint.config.js` is
loaded via Node's ESM `import`, and ESM import resolution only walks up
through `node_modules` folders from the config file's own location — it
does not check npm's global install folder the way old-style CommonJS
`require()` sometimes did. A global install of the plugin will sit there
unused and `eslint.config.js` will fail with `ERR_MODULE_NOT_FOUND`.

Instead, install it once at the common ancestor of your own repos so every
project under it resolves it automatically via that walk-up, with nothing
extra needed per-project:
```
cd /d D:\SourceCode\Git
npm install eslint-plugin-html
```
This drops `node_modules\eslint-plugin-html` plus a `package.json` /
`package-lock.json` at `D:\SourceCode\Git\` itself — that's expected, it's
just bookkeeping for the shared install, not a project of its own. (Use
`D:\SourceCode\Git\`, not `D:\SourceCode\`, since `Git.others\` holds forked
repos you don't own and shouldn't get this global change.)

one-time setup, per project:
- drop the provided `eslint.config.js` in the repo root — it registers
  eslint-plugin-html for `*.html` files. Required under ESLint 9/10's flat
  config: the old `--plugin html` CLI-only invocation (no config file) only
  worked under ESLint 8 and earlier's `.eslintrc.json` model.
- drop the provided `package.json` (containing just `{"type": "module"}`)
  in the repo root too. Without it, `eslint.config.js` still works but Node
  prints a `MODULE_TYPELESS_PACKAGE_JSON` warning on every run because it
  has to sniff the file's module type at runtime. This `package.json` is
  scoped to the repo only — it does not touch or duplicate the shared one
  at `D:\SourceCode\Git\`, the two are independent (module-type lookup and
  npm's dependency walk-up are separate mechanisms).
