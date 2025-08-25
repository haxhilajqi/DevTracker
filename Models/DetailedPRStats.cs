public class DetailedPRStats
{
    public int Id { get; set; }
    public string Title { get; set; }
    public string Project { get; set; }
    public string Repository { get; set; }
    public string Branch { get; set; }
    public DateTime Created { get; set; }
    public DateTime Merged { get; set; }
    public double LeadTimeHours { get; set; }
    public int LinesChanged { get; set; }
    public bool IsRevert { get; set; }
    public bool IsHotfix { get; set; }
    public string Developer { get; set; } 
}
