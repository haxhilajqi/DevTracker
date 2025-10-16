namespace DevTracker.Models;

public class AuthorStats
{
    public string Email { get; init; } = "";
    public string Name { get; init; } = "";
    public int Commits { get; set; }
    public long LinesAdded { get; set; }
    
    public long LogicalChanges { get; set; }
    public long LinesDeleted { get; set; }
    public long TotalLoc => LinesAdded + LinesDeleted;
    public int PRsTouched { get; set; }
    public double LeadTimeHoursSum { get; set; }
    
    public long FilesChanged { get; set; }
}