using Newtonsoft.Json.Linq;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using DevTracker.Models;
using LibGit2Sharp;
using DiffPlex;
using DiffPlex.DiffBuilder;

using DiffPlex.DiffBuilder.Model;

var config = JObject.Parse(System.IO.File.ReadAllText("appsettings.json"));
string org = config["Organization"]!.ToString();
string pat = config["PAT"]!.ToString(); // don't forget to regenerate this time by time. it will expire after three months or so (end of october

var client = new AzureDevOpsClient(org, pat);

bool allProjects = args.Contains("--all-projects");

var excludedProjects = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
{
    "Packages",
    "AWS",
    "DataServices",
    "Salesforce"
};

var excludedRepos = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
{
    "FinancialServices",
    "ValueAddedServices",
    "PaymentProducts",
    "BusinessServices",
    "SharedServices",
    "Frontend",
    "Tools",
    "Mobile",
    "Plugins",
    "SME",
    "TestRepository",
    "Klangkuenstler",
    "GooglePayTool",
    "DevLocal",
    "CSLoadTestingTool",
    "CheckoutLoadTester",
    "ShopwareCloud",
    "SecuraPentestRepo",
    "CCVService",
    "BillableService"
};

DateTime? fromDate = null, toDate = null;

for (int i = 0; i < args.Length; i++)
{
    if (args[i] == "--from" && i + 1 < args.Length)
        fromDate = DateTime.ParseExact(args[i + 1], "yyyy-MM", CultureInfo.InvariantCulture);
    if (args[i] == "--to" && i + 1 < args.Length)
        toDate = DateTime.ParseExact(args[i + 1], "yyyy-MM", CultureInfo.InvariantCulture).AddMonths(1).AddDays(-1);
}

List<ProjectInfo> projects;

if (allProjects)
{
    Console.WriteLine(" Fetching all projects...");
    projects = await client.GetAllProjectsAsync();
}
else
{
    Console.Write("Enter a single project name: ");
    string projectName = Console.ReadLine()!;
    projects = new List<ProjectInfo> { new ProjectInfo { Name = projectName, State = "wellFormed" } };
}

var csv = new StringBuilder();
csv.AppendLine("Project,Repository,Branch,Year,Month,Deploys,Reverts,Hotfixes,FailureRate (%),AvgLeadTimeInHour, AvgPRSize");
var csvPrs = new StringBuilder();
csvPrs.AppendLine("PR ID,Project,Repository,Commit ID,Author,Date,Added,Edited,Deleted,Total LOC");


var authorStats = new Dictionary<string, AuthorStats>(StringComparer.OrdinalIgnoreCase);

var csvCommits = new StringBuilder();
csvCommits.AppendLine("PR ID,Project,Repository,Commit ID,Author Name,Author Email,Date,Added,Deleted,Total LOC, Logical Changes");

var csvAuthors = new StringBuilder();
csvAuthors.AppendLine("Author Name,Author Email,Commits,PRs Touched,Lines Added,Lines Deleted,Logical Changes,Files Changed,Total LOC");


var authorToPrs = new Dictionary<string, HashSet<int>>(StringComparer.OrdinalIgnoreCase);



foreach (var project in projects)
{
    if (excludedProjects.Contains(project.Name))
    {
        continue;
    }

    Console.WriteLine($"\n  Project: {project.Name}");
    var repos = await client.GetRepositoriesForProjectAsync(project.Name);
    

    foreach (var repo in repos)
    {
        if (excludedRepos.Contains(repo.Name))
        {
            continue;
        }

        var branches = await client.GetBranchesAsync(project.Name, repo.Id);

        string? branchRef = branches.Contains("refs/heads/main") ? "refs/heads/main" :
                           branches.Contains("refs/heads/master") ? "refs/heads/master" :
                           null;

        if (branchRef == null)
        {
            Console.WriteLine($"       No 'main' or 'master' branch found in {project.Name}/{repo.Name}. Skipping.");
            continue;
        }

        Console.WriteLine($"     Repository: {repo.Name} ({branchRef})");
        Console.WriteLine($"Checking repo: {repo.Name}");

        var localRepoPath = GitHelpers.EnsureLocalRepo(org, project.Name, Guid.Parse(repo.Id), pat);
 
        var prs = await client.GetMergedPullRequestsAsync(project.Name, repo.Id, branchRef); //get only for specifc month not all
        if (prs == null || !prs.Any())
        {
           Console.WriteLine("      No PRs found.");
           continue;
        }

        var prStatsList = new List<PRStats>();
        var prStatsListDetails = new List<DetailedPRStats>();
        var commitStatsList = new List<DetailedCommitStats>();

        foreach (var pr in prs)
        {
           var merged = DateTime.Parse(pr["closedDate"]!.ToString());


           if (fromDate.HasValue && merged < fromDate.Value) continue;
           if (toDate.HasValue && merged > toDate.Value) continue;

           var prId = pr["pullRequestId"]!.Value<int>();

           var created = DateTime.Parse(pr["creationDate"]!.ToString());
           var creator = pr["createdBy"]?["displayName"]?.ToString() ?? "Unknown";

           // ---- Inside your foreach (var pr in prs) after you compute prId, created, merged, etc. ----
           var prCommits = await client.GetPullRequestCommitsAsync(project.Name, repo.Id, prId);
           if (prCommits == null || prCommits.Count == 0) continue;
        
           // For lead time per author within this PR:
           var firstCommitTimeByAuthor = new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);

           foreach (var c in prCommits)
           {
               string authorName = c["author"]?["name"]?.ToString() ?? c["authorName"]?.ToString() ?? "Unknown";
               string authorEmail = c["author"]?["email"]?.ToString() ?? c["authorEmail"]?.ToString() ?? "unknown@example.com";
               var commitId = c["commitId"]?.ToString() ?? c["commitId"]?.ToString();
               var commitDate = DateTime.Parse(c["author"]?["date"]?.ToString() ?? c["date"]?.ToString() ?? merged.ToString());

               if (IsBot(authorName, authorEmail)) continue;

               string message = c["comment"]?.ToString() ?? c["message"]?.ToString() ?? "";
               var coAuthors = ParseCoAuthors(message); // implement to extract "Co-authored-by: Name <email>"

               try
               {
                   var (filesChanged, logicalChanged, added, deleted) =
                       GitHelpers.GetCommitDiffSummaryCollapsedIgnoringTrivial(
                           localRepoPath, commitId,
                           stripXmlDecl: true,
                           includeExts: new[]{ ".cs", ".fs", ".ts", ".tsx", ".js", ".java", ".kt", ".go", ".py", ".resx",".yaml" } // adjust as needed
                       );
                   
 
                   UpsertAuthor(authorStats, authorToPrs, authorName, authorEmail, prId, added, deleted, logicalChanged, filesChanged);
                   csvCommits.AppendLine($"{prId},{project.Name},{repo.Name},{commitId},{authorName},{authorEmail},{commitDate:O},{added},{deleted},{added + deleted},{logicalChanged}");

                   if (!firstCommitTimeByAuthor.ContainsKey(authorEmail))
                       firstCommitTimeByAuthor[authorEmail] = commitDate;

                   foreach (var (coName, coEmail) in coAuthors)
                   {
                       if (IsBot(coName, coEmail)) continue;
                       UpsertAuthor(authorStats, authorToPrs, coName, coEmail, prId, 0, 0, 0,0);
                       if (!firstCommitTimeByAuthor.ContainsKey(coEmail))
                           firstCommitTimeByAuthor[coEmail] = commitDate;
                   }
               }
               catch (Exception e)
               {
                   Console.WriteLine(e);
               }
           }
        }
    }
}

foreach (var s in authorStats.Values.OrderByDescending(x => x.TotalLoc))
{
    csvAuthors.AppendLine($"{s.Name},{s.Email},{s.Commits},{s.PRsTouched},{s.LinesAdded},{s.LinesDeleted},{s.LogicalChanges},{s.FilesChanged},{s.TotalLoc}");
}

var outDir = AppContext.BaseDirectory;
var authorSummaryFile = Path.Combine(outDir, $"author_summary_{DateTime.Now:yyyyMMdd_HHmmss}.csv");
System.IO.File.WriteAllText(authorSummaryFile, csvAuthors.ToString());
var commitFile = Path.Combine(outDir, $"commit_attribution_{DateTime.Now:yyyyMMdd_HHmmss}.csv");
System.IO.File.WriteAllText(commitFile, csvCommits.ToString());


//export prs
// var prFileName = $"pullrequest_report_{DateTime.Now:yyyyMMdd_HHmmss}.csv";
// System.IO.File.WriteAllText(prFileName, csvPrs.ToString());
// Console.WriteLine($"\nCSV PR export complete: {prFileName}");

// var fileName = $"deployment_report_{DateTime.Now:yyyyMMdd_HHmmss}.csv";
// System.IO.File.WriteAllText(fileName, csv.ToString());
// Console.WriteLine($"\nCSV export complete: {fileName}");

void AnalyzePRGroup(StringBuilder csv, MonthlyDeploymentStats stats)
{
    Console.WriteLine(
        $"         {stats.Year}-{stats.Month:D2} ({stats.MonthName}): " +
        $"Deploys: {stats.Deploys}, Reverts: {stats.Reverts}, Hotfixes: {stats.Hotfixes}, " +
        $"Failure Rate: {stats.FailureRate:F1}%, Avg Lead Time: {stats.AvgLeadTimeHours:F2}h, Avg PR Size: {stats.AvgPRSize:F2}");

    csv.AppendLine($"{stats.Project},{stats.Repository},{stats.Branch},{stats.Year},{stats.Month}," +
                   $"{stats.Deploys},{stats.Reverts},{stats.Hotfixes},{stats.FailureRate:F2},{stats.AvgLeadTimeHours:F2}h,{stats.AvgPRSize:F1}");
}

bool IsBot(string name, string email)
{
    var s = $"{name} {email}".ToLowerInvariant();
    return s.Contains("[bot]") || s.Contains(" bot") || s.Contains("ci@") || s.Contains("build@") || s.Contains("automation@");
}


void UpsertAuthor(
    Dictionary<string, AuthorStats> map,
    Dictionary<string, HashSet<int>> a2p,
    string name,
    string email,
    int prId,
    long added,
    long deleted,
    long logicalChanges,
    long filesChanged)
{
    if (!map.TryGetValue(email, out var s))
    {
        s = new AuthorStats { Email = email, Name = name };
        map[email] = s;
    }
    s.Commits += 1;
    s.LinesAdded += added;
    s.FilesChanged += filesChanged;
    s.LinesDeleted += deleted;
    s.LogicalChanges += logicalChanges;

    if (!a2p.TryGetValue(email, out var set))
    {
        set = new HashSet<int>();
        a2p[email] = set;
    }
    if (set.Add(prId)) s.PRsTouched += 1;
}

IEnumerable<(string Name, string Email)> ParseCoAuthors(string message)
{
    var result = new List<(string, string)>();
    if (string.IsNullOrEmpty(message)) return result;
    var lines = message.Split('\n');
    var pattern = new System.Text.RegularExpressions.Regex(@"Co-authored-by:\s*(.+?)\s*<([^>]+)>", RegexOptions.IgnoreCase);
    foreach (var line in lines)
    {
        var m = pattern.Match(line);
        if (m.Success)
        {
            result.Add((m.Groups[1].Value.Trim(), m.Groups[2].Value.Trim()));
        }
    }
    return result;
}

internal static class GitHelpers
{
    internal static readonly string RepoCacheRoot =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".ado-repos-cache");

    internal static string EnsureLocalRepo(string org, string project, Guid repoId, string? pat)
    {
        Directory.CreateDirectory(RepoCacheRoot);
        var localPath = Path.Combine(RepoCacheRoot, $"{project}_{repoId}");

        var creds = new UsernamePasswordCredentials
        {
            Username = "pat",                // any non-empty string
            Password = pat ?? string.Empty   // your Azure DevOps PAT
        };

        if (!Repository.IsValid(localPath))
        {
            var co = new CloneOptions
            {
                IsBare = false
            };
            co.FetchOptions.CredentialsProvider = (_url, _user, _types) => creds;

            Repository.Clone($"https://dev.azure.com/{org}/{project}/_git/{repoId}", localPath, co);
        }
        else
        {
            using var repo = new Repository(localPath);
            var fo = new FetchOptions
            {
                CredentialsProvider = (_url, _user, _types) => creds
            };
            Commands.Fetch(repo, "origin", new[] { "+refs/heads/*:refs/remotes/origin/*" }, fo, null);
        }

        return localPath;
    }
    
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







