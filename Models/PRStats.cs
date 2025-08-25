public class PRStats
{
    public DateTime Created { get; set; }
    public DateTime Merged { get; set; }
    public string Title { get; set; }
    public string SourceRef { get; set; }
    public string TargetRef { get; set; }
    public int LinesChanged { get; set; }

    public bool IsRevert =>
        Title.Contains("Revert", StringComparison.OrdinalIgnoreCase);

    public bool IsHotfix =>
        (TargetRef.EndsWith("/main") || TargetRef.EndsWith("/master")) &&
        !SourceRef.EndsWith("/develop");

    public double LeadTimeHours =>
        (Merged - Created).TotalHours;
}

public class ItemList { public List<GitItem> Value { get; set; } = new(); }
public class GitItem { public string Path { get; set; } = string.Empty; public bool IsFolder { get; set; } }