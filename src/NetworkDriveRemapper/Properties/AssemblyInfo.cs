using System.Runtime.CompilerServices;

// Allow the regression runner to verify error feedback and inject a browser opener without launching
// real browser windows or dismissing unrelated message boxes on the user's desktop.
[assembly: InternalsVisibleTo("NetworkDriveRemapper.Tests")]
