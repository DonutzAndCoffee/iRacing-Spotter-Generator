namespace iRacing_Spotter_Generator.Models
{
    /// <summary>
    /// User-tracked review status for a spotter message row, used to keep
    /// an overview of what still needs work while authoring a pack.
    /// </summary>
    public enum RowStatus
    {
        ToDo,
        Satisfactory,
        ReworkNeeded,
        Done
    }
}
