using System.Text;
using System.Text.RegularExpressions;
using LibGit2Sharp;
using DiffPlex;
using DiffPlex.DiffBuilder;

using DiffPlex.DiffBuilder.Model;

internal static class GitHelpers
{
    internal static readonly string RepoCacheRoot =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".ado-repos-cache");

    internal static string EnsureLocalRepo(string org, string project, Guid repoId, string? pat)
    {
        if (string.IsNullOrWhiteSpace(pat))
            throw new InvalidOperationException("PAT is required for Azure DevOps.");

        Directory.CreateDirectory(RepoCacheRoot);
        var localPath = Path.Combine(RepoCacheRoot, $"{project}_{repoId}");
        var repoUrl   = $"https://dev.azure.com/{org}/{project}/_git/{repoId}";

        // Build Basic auth header: base64("pat:<PAT>")
        var basic = Convert.ToBase64String(Encoding.ASCII.GetBytes($"pat:{pat}"));
        var authArg = $"-c http.extraHeader=\"Authorization: Basic {basic}\"";

        if (!Repository.IsValid(localPath))
        {
            // fresh clone (lean clone; adjust flags if you prefer a full checkout)
            RunGit(null, $"{authArg} clone --filter=blob:none --no-tags --origin origin \"{repoUrl}\" \"{localPath}\"");
        }

        // Always refresh branches & tags; safe and avoids SecureTransport in LibGit2Sharp
        RunGit(localPath, $"{authArg} fetch --prune origin \"+refs/heads/*:refs/remotes/origin/*\"");
        RunGit(localPath, $"{authArg} fetch --prune --tags origin");

        // (Optional) If you *also* want PR refs, uncomment below.
        // NOTE: These work on Azure DevOps if the server exposes PR refs.
        // RunGit(localPath, $"{authArg} fetch origin \"+refs/pull/*/merge:refs/remotes/origin/pr/*/merge\"");
        // RunGit(localPath, $"{authArg} fetch origin \"+refs/pull/*/head:refs/remotes/origin/pr/*/head\"");

        return localPath;
    }

    private static (int code, string stdout, string stderr) RunGit(string? workDir, string args)
    {
        var psi = new System.Diagnostics.ProcessStartInfo("git", args)
        {
            WorkingDirectory = workDir ?? Directory.GetCurrentDirectory(),
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        using var p = System.Diagnostics.Process.Start(psi)!;
        var so = p.StandardOutput.ReadToEnd();
        var se = p.StandardError.ReadToEnd();
        p.WaitForExit();

        if (p.ExitCode != 0)
            throw new InvalidOperationException($"git {args}\nexit={p.ExitCode}\n{se}\n{so}");

        return (p.ExitCode, so, se);
    }
    
    internal static void EnsureCommitPresent(string repoPath, string sha, string pat)
    {
        using var repo = new Repository(repoPath);
        if (repo.Lookup<Commit>(sha) != null) return;

        var basic = Convert.ToBase64String(Encoding.ASCII.GetBytes($"pat:{pat}"));
        var authArg = $"-c http.extraHeader=\"Authorization: Basic {basic}\"";

        // Targeted fetch of exactly that object (fast)
        try
        {
            RunGit(repoPath, $"{authArg} fetch origin {sha}");
        }
        catch
        {
            // Fall back to broad refresh
            RunGit(repoPath, $"{authArg} fetch --prune origin \"+refs/heads/*:refs/remotes/origin/*\"");
            RunGit(repoPath, $"{authArg} fetch --prune --tags origin");
        }

        if (new Repository(repoPath).Lookup<Commit>(sha) == null)
            throw new InvalidOperationException($"Commit {sha} still not found after fetch in {repoPath}");
    }




    // internal static string EnsureLocalRepo(string org, string project, Guid repoId, string? pat)
    // {
    //     Directory.CreateDirectory(RepoCacheRoot);
    //     var localPath = Path.Combine(RepoCacheRoot, $"{project}_{repoId}");

    //     var creds = new UsernamePasswordCredentials
    //     {
    //         Username = "pat",                // any non-empty string
    //         Password = pat ?? string.Empty   // your Azure DevOps PAT
    //     };

    //     if (!Repository.IsValid(localPath))
    //     {
    //         var co = new CloneOptions
    //         {
    //             IsBare = false
    //         };
    //         co.FetchOptions.CredentialsProvider = (_url, _user, _types) => creds;

    //         Repository.Clone($"https://dev.azure.com/{org}/{project}/_git/{repoId}", localPath, co);
    //     }

    //     using var repo = new Repository(localPath);
    //     var fo = new FetchOptions
    //     {
    //         CredentialsProvider = (_url, _user, _types) => creds
    //     };
    //     Commands.Fetch(repo, "origin", new[]
    //     {
    //         // all branches
    //         "+refs/heads/*:refs/remotes/origin/*"
    //     }, fo, null);

    //     return localPath;
    // }
    
    internal static (int filesChanged, int logicalChanged, int added, int deleted)
    GetCommitDiffSummaryCollapsedIgnoringTrivial(
        string repoPath,
        string commitId,
        bool stripXmlDecl = true,
        string[]? includeExts = null) // e.g., new[]{ ".cs",".resx" }
    {
        using var repo = new Repository(repoPath);
        var commit = repo.Lookup<Commit>(commitId)
                     ?? throw new InvalidOperationException($"Commit {commitId} not found in {repoPath}");
        var parent = commit.Parents.FirstOrDefault();
        if (parent == null) return (0, 0, 0, 0);

        Patch patch = repo.Diff.Compare<Patch>(parent.Tree, commit.Tree);
        var entries = patch.Where(e =>
            !e.IsBinaryComparison &&
            (includeExts == null || includeExts.Length == 0 ||
             includeExts.Contains(Path.GetExtension(e.Path)?.ToLowerInvariant() ?? ""))
        ).ToList();

        int filesChanged = 0, addedTotal = 0, deletedTotal = 0, logicalTotal = 0;

        foreach (var e in entries)
        {
            var oldBlob = parent[e.Path]?.Target as Blob;
            var newBlob = commit[e.Path]?.Target as Blob;

            if (oldBlob == null || newBlob == null)
            {
                filesChanged++;
                addedTotal  += e.LinesAdded;
                deletedTotal += e.LinesDeleted;
                logicalTotal += Math.Max(e.LinesAdded, e.LinesDeleted);
                continue;
            }

            string oldText = ReadAllText(oldBlob);
            string newText = ReadAllText(newBlob);

            oldText = NormalizeText(oldText, stripXmlDecl);
            newText = NormalizeText(newText, stripXmlDecl);

            if (string.Equals(oldText, newText, StringComparison.Ordinal))
                continue;

            filesChanged++;

            addedTotal  += e.LinesAdded;
            deletedTotal += e.LinesDeleted;

            var differ = new Differ();
            var inline = new InlineDiffBuilder(differ).BuildDiffModel(oldText, newText);

            int logicalForFile = inline.Lines.Count(l =>
                l.Type == ChangeType.Inserted || l.Type == ChangeType.Deleted);

            if (logicalForFile == 0)
                logicalForFile = Math.Max(e.LinesAdded, e.LinesDeleted);

            logicalTotal += logicalForFile;
        }

        return (filesChanged, logicalTotal, addedTotal, deletedTotal);
    }

    private static string ReadAllText(Blob blob)
    {
        using var s = blob.GetContentStream();
        using var r = new StreamReader(s, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        return r.ReadToEnd();
    }

    private static readonly Regex XmlDeclRegex =
        new(@"^\s*<\?xml\s+version=""1\.0""[^?]*\?>\s*\n?", RegexOptions.IgnoreCase | RegexOptions.Multiline);

    private static string NormalizeText(string s, bool stripXmlDecl)
    {
        s = s.Replace("\r\n", "\n").Replace("\r", "\n");
        s = string.Join("\n", s.Split('\n').Select(line => line.TrimEnd()));
        if (stripXmlDecl) s = XmlDeclRegex.Replace(s, string.Empty);
        return s;
    }
}







