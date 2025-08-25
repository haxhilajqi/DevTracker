public class DetailedCommitStats
{
    public int PrId { get; set; }
    public string Project { get; set; }
    public string Repository { get; set; }
    public string CommitId { get; set; }
    public string Author { get; set; }
    public DateTime CommitDate { get; set; }
    public int LinesAdded { get; set; }
    public int LinesEdited { get; set; }
    public int LinesDeleted { get; set; }

    public int TotalLinesChanged => LinesAdded + LinesEdited + LinesDeleted;
}