namespace AS24Net.DataLayer;

public class DataFragment<TItem>
{
    public List<TItem> Data { get; init; } = [];

    /// <summary>
    /// Total number of records regardless of paging.
    /// </summary>
    public int TotalCount { get; init; }
}