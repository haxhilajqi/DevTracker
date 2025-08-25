using Newtonsoft.Json.Linq;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using static System.Net.WebRequestMethods;

var config = JObject.Parse(System.IO.File.ReadAllText("appsettings.json"));
string org = config["Organization"]!.ToString();
string pat = config["PAT"]!.ToString(); // don't forget to regenerate this time by time. it will expire after three months or so (end of october0

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

        //Console.WriteLine($"     Repository: {repo.Name} ({branchRef})");

        //Console.WriteLine($"Checking repo: {repo.Name}");

        var res = client.GetAppSettingSandBox(repo, project.Name).Result;
        if (res != null)
        {
            foreach (var match in res)
            {
                Console.WriteLine($"{repo.Name}: {match.Path}");
            }
        }
           



        //var prs = await client.GetMergedPullRequestsAsync(project.Name, repo.Id, branchRef);
        //if (prs == null || !prs.Any())
        //{
        //    Console.WriteLine("      No PRs found.");
        //    continue;
        //}

        //var prStatsList = new List<PRStats>();
        //var prStatsListDetails = new List<DetailedPRStats>();
        //var commitStatsList = new List<DetailedCommitStats>();

        //foreach (var pr in prs)
        //{
        //    var merged = DateTime.Parse(pr["closedDate"]!.ToString());


        //    if (fromDate.HasValue && merged < fromDate.Value) continue;
        //    if (toDate.HasValue && merged > toDate.Value) continue;

        //    var prId = pr["pullRequestId"]!.Value<int>();

        //    var created = DateTime.Parse(pr["creationDate"]!.ToString());
        //    var creator = pr["createdBy"]?["displayName"]?.ToString() ?? "Unknown";

        //    //don't delete

        //    prStatsListDetails.Add(new DetailedPRStats
        //    {
        //        Id = prId,
        //        Title = pr["title"]!.ToString(),
        //        Project = project.Name,
        //        Repository = repo.Name,
        //        Branch = branchRef.Split('/').Last(),
        //        Created = created,
        //        Merged = merged,
        //        LeadTimeHours = (merged - created).TotalHours,
        //        LinesChanged = await client.GetLinesChangedInPR(project.Name, repo.Name, prId),
        //        IsRevert = pr["title"]!.ToString().Contains("Revert", StringComparison.OrdinalIgnoreCase),
        //        IsHotfix = !pr["sourceRefName"]!.ToString().Contains("develop", StringComparison.OrdinalIgnoreCase)
        //       && (branchRef.EndsWith("main") || branchRef.EndsWith("master")),
        //        Developer = creator
        //    });
        //    /////till here
        //    ///
        //    //prStatsList.Add(new PRStats
        //    //{
        //    //    Title = pr["title"]!.ToString(),
        //    //    SourceRef = pr["sourceRefName"].ToString(),
        //    //    TargetRef = pr["targetRefName"].ToString(),
        //    //    Created = created,
        //    //    Merged = merged,
        //    //    LinesChanged = await client.GetLinesChangedInPR(project.Name, repo.Name, prId),
        //    //});

        //    #region author report
        //    commitStatsList = await client.GetLinesChangedInPRPerDeveloper(project.Name, repo.Name, prId);
        //    foreach (var commitStats in commitStatsList)
        //    {
        //        csvPrs.AppendLine($"{prId},{project.Name},{repo.Name},{commitStats.CommitId},{commitStats.Author},{commitStats.CommitDate:yyyy-MM-dd},{commitStats.LinesAdded},{commitStats.LinesEdited},{commitStats.LinesDeleted},{commitStats.TotalLinesChanged}");
        //    }
        //    #endregion


        //}






        ////export PRs

        //var grouped = prStatsList
        //    .GroupBy(pr => new { pr.Merged.Year, pr.Merged.Month })
        //    .OrderByDescending(g => g.Key.Year)
        //    .ThenByDescending(g => g.Key.Month);


        //foreach (var group in grouped)
        //{
        //    var stats = new MonthlyDeploymentStats
        //    {
        //        Project = project.Name,
        //        Repository = repo.Name,
        //        Branch = branchRef.Split('/').Last(),
        //        Year = group.Key.Year,
        //        Month = group.Key.Month,
        //        PullRequests = group.ToList()
        //    };

        //    AnalyzePRGroup(csv, stats);
        //}
    }

    




}

//export prs
var prFileName = $"pullrequest_report_{DateTime.Now:yyyyMMdd_HHmmss}.csv";
System.IO.File.WriteAllText(prFileName, csvPrs.ToString());
Console.WriteLine($"\nCSV PR export complete: {prFileName}");

//var fileName = $"deployment_report_{DateTime.Now:yyyyMMdd_HHmmss}.csv";
//File.WriteAllText(fileName, csv.ToString());
//Console.WriteLine($"\nCSV export complete: {fileName}");

void AnalyzePRGroup(StringBuilder csv, MonthlyDeploymentStats stats)
{
    Console.WriteLine(
        $"         {stats.Year}-{stats.Month:D2} ({stats.MonthName}): " +
        $"Deploys: {stats.Deploys}, Reverts: {stats.Reverts}, Hotfixes: {stats.Hotfixes}, " +
        $"Failure Rate: {stats.FailureRate:F1}%, Avg Lead Time: {stats.AvgLeadTimeHours:F2}h, Avg PR Size: {stats.AvgPRSize:F2}");

    csv.AppendLine($"{stats.Project},{stats.Repository},{stats.Branch},{stats.Year},{stats.Month}," +
                   $"{stats.Deploys},{stats.Reverts},{stats.Hotfixes},{stats.FailureRate:F2},{stats.AvgLeadTimeHours:F2}h,{stats.AvgPRSize:F1}");
}







