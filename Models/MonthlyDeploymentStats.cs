using System.Globalization;

public class MonthlyDeploymentStats
{
    public string Project { get; set; }
    public string Repository { get; set; }
    public string Branch { get; set; }
    public int Year { get; set; }
    public int Month { get; set; }

    public List<PRStats> PullRequests { get; set; } = new();

    public int Total => PullRequests.Count;
    public int Reverts => PullRequests.Count(pr => pr.IsRevert);
    public int Hotfixes => PullRequests.Count(pr => pr.IsHotfix);
    public int Deploys => Total - Reverts;
    public double FailureRate => Total == 0 ? 0 : (double)Reverts / Total * 100;
    public double AvgLeadTimeHours => PullRequests.Any() ? PullRequests.Average(pr => pr.LeadTimeHours) : 0;
    public double AvgPRSize => PullRequests.Any() ? PullRequests.Average(pr => pr.LinesChanged) : 0;

    public string MonthName => CultureInfo.CurrentCulture.DateTimeFormat.GetMonthName(Month);
}
