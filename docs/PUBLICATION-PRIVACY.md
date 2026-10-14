# Public history and download cleanup

The 0.8.0 publication refreshes public branch/tag history to remove literal developer workspace, game, Workshop and user-profile paths from reachable source blobs and commit text. Historical resource-browser screenshots use the current masked capture. Recovery bundles and replacement rules stay in ignored local workspace files and are not published.

Affected historical Windows packages are withdrawn instead of patching compiled assemblies. Their paths were embedded in binaries and debug symbols; removing just the README or PDB files would leave some disclosures behind. Verified clean native-map example downloads remain available, and checksum files describe the retained assets. Use the current Windows release for a ready-to-run editor. Historical source tags retain their version names with rewritten commit identifiers.

After this rewrite, clone the repository again for the simplest update. If retaining local work, export your own changes first and reapply them on the rewritten branch. Do not merge or push old history back into the repository. Public branch/tag commit identifiers have changed.

The current package uses configurable installation folders, relative public instructions, masked path/session fields in documentation screenshots, compiler path mapping and no project PDBs. Packaging checks tracked source and ZIP entries against configured local paths. These checks do not send local settings to GitHub.

Rewriting public refs and withdrawing assets cannot erase already downloaded copies, third-party clones/forks or every cached GitHub view. GitHub describes those limits and support procedures in [Removing sensitive data from a repository](https://docs.github.com/en/authentication/keeping-your-account-and-data-secure/removing-sensitive-data-from-a-repository). No automatic guarantee of deletion from those locations is made.
