namespace CS2_CustomVotes.Shared.Models;

public class YesNoVoteInfo
{
    public int TotalClients { get; set; }
    public int YesVotes { get; set; }
    public int NoVotes { get; set; }
    public int TotalVotes { get; set; }
    public (int ClientSlot, int Vote)[] ClientInfo { get; set; } = Array.Empty<(int, int)>();
}
