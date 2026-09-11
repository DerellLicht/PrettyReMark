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

### roslynator - C# code validation
This is an addon package for `dotnet` toolchain.  
install: `dotnet tool install -g roslynator.dotnet.cli`
one-time setup:

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

running the test:
just run `make roslyn` in the `PrettyReMark` folder.
