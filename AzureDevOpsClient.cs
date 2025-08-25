using System.Net.Http.Headers;
using System.Text.Json;
using Newtonsoft.Json.Linq;
using static System.Net.WebRequestMethods;

public class AzureDevOpsClient
{
    private readonly HttpClient _client;
    private readonly string _org;

    private const string TargetFile = "appsettings.sandbox.json";

    public AzureDevOpsClient(string org, string pat)
    {
        _org = org;
        _client = new HttpClient();
        var base64Pat = Convert.ToBase64String(System.Text.Encoding.ASCII.GetBytes($":{pat}"));
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", base64Pat);
    }

    public async Task<List<ProjectInfo>> GetAllProjectsAsync()
    {
        string url = $"https://dev.azure.com/{_org}/_apis/projects?api-version=7.2-preview.2";
        var res = await _client.GetAsync(url);
        var json = JObject.Parse(await res.Content.ReadAsStringAsync());
        return json["value"]!
            .Select(p => new ProjectInfo
            {
                Name = p["name"]!.ToString(),
                State = p["state"]!.ToString()
            })
            .Where(p => p.State == "wellFormed")
            .ToList();
    }

    public async Task<JArray?> GetMergedPullRequestsAsync(string projectName, string repositoryId, string targetRefFull)
    {
        string url = $"https://dev.azure.com/{_org}/{projectName}/_apis/git/repositories/{repositoryId}/pullrequests?" +
                     "searchCriteria.status=completed" +
                     $"&searchCriteria.targetRefName={targetRefFull}" +
                     "&$top=1000&api-version=7.2-preview.2";

        var res = await _client.GetAsync(url);
        if (!res.IsSuccessStatusCode) return null;

        var json = JObject.Parse(await res.Content.ReadAsStringAsync());
        return json["value"] as JArray;
    }


    public async Task<List<RepositoryInfo>> GetRepositoriesForProjectAsync(string projectName)
    {
        string url = $"https://dev.azure.com/{_org}/{projectName}/_apis/git/repositories?api-version=7.2-preview.2";
        var res = await _client.GetAsync(url);
        if (!res.IsSuccessStatusCode) return new List<RepositoryInfo>();

        var json = JObject.Parse(await res.Content.ReadAsStringAsync());
        return json["value"]!
            .Select(repo => new RepositoryInfo
            {
                Id = repo["id"]!.ToString(),
                Name = repo["name"]!.ToString(),
                DefaultBranch = repo["defaultBranch"]?.ToString() ?? "refs/heads/main"
            }).ToList();
    }

    public async Task<List<string>> GetBranchesAsync(string projectName, string repositoryId)
    {
        string url = $"https://dev.azure.com/{_org}/{projectName}/_apis/git/repositories/{repositoryId}/refs?" +
                     "filter=heads/&api-version=7.2-preview.2";

        var res = await _client.GetAsync(url);
        if (!res.IsSuccessStatusCode) return new List<string>();

        var json = JObject.Parse(await res.Content.ReadAsStringAsync());
        return json["value"]!
            .Select(refObj => refObj["name"]!.ToString()) // e.g., "refs/heads/main"
            .ToList();
    }

    //public async Task<int> GetLinesChangedInPR(string project, string repoId, int prId)
    //{
    //    try
    //    {
    //        // STEP 1: Get PR iterations
    //        string iterationsUrl = $"https://dev.azure.com/{_org}/{project}/_apis/git/repositories/{Uri.EscapeDataString(repoId)}/pullRequests/{prId}/iterations?api-version=7.2-preview.1";
    //        var iterationsRes = await _client.GetAsync(iterationsUrl);

    //        if (!iterationsRes.IsSuccessStatusCode)
    //            throw new Exception("Failed to get PR iterations");

    //        var iterationsJson = JObject.Parse(await iterationsRes.Content.ReadAsStringAsync());
    //        var latestIteration = iterationsJson["value"]
    //            ?.OrderByDescending(i => i["createdDate"]!.Value<DateTime>())
    //            .FirstOrDefault();

    //        if (latestIteration == null)
    //            throw new Exception("No iterations found");

    //        int latestIterationId = latestIteration["id"]!.Value<int>();

    //        // STEP 2: Try files endpoint
    //        string filesUrl = $"https://dev.azure.com/{_org}/{project}/_apis/git/repositories/{Uri.EscapeDataString(repoId)}/pullRequests/{prId}/iterations/{latestIterationId}/files?api-version=7.2-preview.1";
    //        var filesRes = await _client.GetAsync(filesUrl);

    //        if (filesRes.IsSuccessStatusCode)
    //        {
    //            var filesJson = JObject.Parse(await filesRes.Content.ReadAsStringAsync());
    //            int added = filesJson["changeCounts"]?["Add"]?.Value<int>() ?? 0;
    //            int edited = filesJson["changeCounts"]?["Edit"]?.Value<int>() ?? 0;
    //            int deleted = filesJson["changeCounts"]?["Delete"]?.Value<int>() ?? 0;

    //            return added + edited + deleted;
    //        }
    //        else
    //        {
    //            Console.WriteLine($"⚠️ Files API failed for PR {prId}, falling back to commit-based diff...");
    //        }
    //    }
    //    catch (Exception ex)
    //    {
    //        Console.WriteLine($"⚠️ Iteration/files logic failed for PR {prId}: {ex.Message}");
    //    }

    //    // STEP 3: Fallback to commit-by-commit analysis
    //    try
    //    {
    //        string commitsUrl = $"https://dev.azure.com/{_org}/{project}/_apis/git/repositories/{Uri.EscapeDataString(repoId)}/pullRequests/{prId}/commits?api-version=7.2-preview.1";
    //        var commitsRes = await _client.GetAsync(commitsUrl);

    //        if (!commitsRes.IsSuccessStatusCode)
    //            throw new Exception("Failed to get PR commits");

    //        var commitsJson = JObject.Parse(await commitsRes.Content.ReadAsStringAsync());
    //        var commits = commitsJson["value"];
    //        if (commits == null) return 0;

    //        int totalChanged = 0;

    //        foreach (var commit in commits)
    //        {
    //            string commitId = commit["commitId"]!.ToString();
    //            string changesUrl = $"https://dev.azure.com/{_org}/{project}/_apis/git/repositories/{Uri.EscapeDataString(repoId)}/commits/{commitId}/changes?api-version=7.2-preview.1";
    //            var changesRes = await _client.GetAsync(changesUrl);

    //            if (!changesRes.IsSuccessStatusCode)
    //                continue;

    //            var changesJson = JObject.Parse(await changesRes.Content.ReadAsStringAsync());
    //            var changes = changesJson["changes"];
    //            if (changes == null) continue;

    //            foreach (var change in changes)
    //            {
    //                int add = change["changeCounts"]?["Add"]?.Value<int>() ?? 0;
    //                int edit = change["changeCounts"]?["Edit"]?.Value<int>() ?? 0;
    //                int del = change["changeCounts"]?["Delete"]?.Value<int>() ?? 0;

    //                totalChanged += add + edit + del;
    //            }
    //        }

    //        return totalChanged;
    //    }
    //    catch (Exception ex)
    //    {
    //        Console.WriteLine($"❌ Commit-based fallback failed for PR {prId}: {ex.Message}");
    //        return 0;
    //    }
    //}


    public async Task<int> GetLinesChangedInPR(string project, string repoId, int prId)
    {
        int totalChanged = 0;

        // Step 1: Get PR commits
        string commitsUrl = $"https://dev.azure.com/{_org}/{project}/_apis/git/repositories/{Uri.EscapeDataString(repoId)}/pullRequests/{prId}/commits?api-version=7.2-preview.1";
        try
        {
            var commitsRes = await _client.GetAsync(commitsUrl);
            if (!commitsRes.IsSuccessStatusCode) return 0;

            var commitsJson = JObject.Parse(await commitsRes.Content.ReadAsStringAsync());
            var commits = commitsJson["value"];
            if (commits == null || !commits.Any()) return 0;

            foreach (var commit in commits)
            {
                string commitId = commit["commitId"]!.ToString();
                string changesUrl = $"https://dev.azure.com/{_org}/{project}/_apis/git/repositories/{Uri.EscapeDataString(repoId)}/commits/{commitId}/changes?api-version=7.2-preview.1";
                var changesRes = await _client.GetAsync(changesUrl);
                if (!changesRes.IsSuccessStatusCode) continue;

                var changesJson = JObject.Parse(await changesRes.Content.ReadAsStringAsync());
                var changes = changesJson["changeCounts"];
                if (changes == null) continue;

                int add = changesJson["changeCounts"]?["Add"]?.Value<int>() ?? 0;
                int edit = changesJson["changeCounts"]?["Edit"]?.Value<int>() ?? 0;
                int del = changesJson["changeCounts"]?["Delete"]?.Value<int>() ?? 0;
                totalChanged += add + del + edit;
            }
        }
        catch (HttpRequestException ex) when (ex.Message.Contains("404"))
        {
            Console.WriteLine($" Could not get diff for PR {prId} — source branch may be deleted or external.");
            return 0;
        }

        return totalChanged;
    }
    public async Task<List<DetailedCommitStats>> GetLinesChangedInPRPerDeveloper(string project, string repoId, int prId)
    {
        var detailedCommitStats = new DetailedCommitStats();
        var detailedCommitStatsList = new List<DetailedCommitStats>();
        string commitsUrl = $"https://dev.azure.com/{_org}/{project}/_apis/git/repositories/{Uri.EscapeDataString(repoId)}/pullRequests/{prId}/commits?api-version=7.2-preview.1";
        try
        {
            var commitsRes = await _client.GetAsync(commitsUrl);
            if (!commitsRes.IsSuccessStatusCode) return new List<DetailedCommitStats>();

            var commitsJson = JObject.Parse(await commitsRes.Content.ReadAsStringAsync());
            var commits = commitsJson["value"];
            if (commits == null || !commits.Any()) return new List<DetailedCommitStats>();

            foreach (var commit in commits)
            {
                string commitId = commit["commitId"]!.ToString();
                string changesUrl = $"https://dev.azure.com/{_org}/{project}/_apis/git/repositories/{Uri.EscapeDataString(repoId)}/commits/{commitId}/changes?api-version=7.2-preview.1";
                var changesRes = await _client.GetAsync(changesUrl);
                if (!changesRes.IsSuccessStatusCode) continue;

                var changesJson = JObject.Parse(await changesRes.Content.ReadAsStringAsync());
                var changes = changesJson["changeCounts"];
                if (changes == null) continue;

                int add = changesJson["changeCounts"]?["Add"]?.Value<int>() ?? 0;
                int edit = changesJson["changeCounts"]?["Edit"]?.Value<int>() ?? 0;
                int del = changesJson["changeCounts"]?["Delete"]?.Value<int>() ?? 0;


                var author = commit["committer"]?["name"]?.ToString() ?? "Unknown";
                var date = DateTime.Parse(commit["committer"]?["date"]?.ToString() ?? DateTime.MinValue.ToString());

                var counts = changesJson["changeCounts"];

                detailedCommitStats = new DetailedCommitStats
                {
                    PrId = prId,
                    Project = project,
                    Repository = repoId,
                    CommitId = commitId,
                    Author = author,
                    CommitDate = date,
                    LinesAdded = add,
                    LinesEdited = edit,
                    LinesDeleted = del
                };
                detailedCommitStatsList.Add(detailedCommitStats);
            }

        }
        catch (HttpRequestException ex) when (ex.Message.Contains("404"))
        {
            Console.WriteLine($" Could not get diff for PR {prId} — source branch may be deleted or external.");
            return new List<DetailedCommitStats>();
        }

        return detailedCommitStatsList;
    }

    public async Task<List<GitItem>> GetAppSettingSandBox(RepositoryInfo repo, string project)
    {
        try
        {
            // List all items from default branch (recursively). This can be heavy on big repos.
            var itemsUrl = $"https://dev.azure.com/{_org}/{project}/_apis/git/repositories/{repo.Id}/items?scopePath=/&recursionLevel=Full&includeContentMetadata=false&api-version=7.2-preview.1";
            var itemsResp = await _client.GetAsync(itemsUrl);
            itemsResp.EnsureSuccessStatusCode();
            var items = await JsonSerializer.DeserializeAsync<ItemList>(await itemsResp.Content.ReadAsStreamAsync(), JsonOpts);
            var matchList = items?.Value?.Where(i => !i.IsFolder && i.Path.EndsWith(TargetFile, StringComparison.OrdinalIgnoreCase));
            return matchList.ToList();
        }
        catch (Exception ex)
        {
            Console.WriteLine($" ! Error {ex.Message}");
        }
        return null;
    }

    private static readonly JsonSerializerOptions JsonOpts = new JsonSerializerOptions
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };


}
