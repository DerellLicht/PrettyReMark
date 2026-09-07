namespace PrettyMark;

// Single source of truth for the application version. Shown in the About
// dialog (via setAppVersion() in index.html). Bump this alongside a new
// CHANGELOG.md entry -- the two should always move together.
static class AppVersion
{
    public const string Current = "1.03";
}
