using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Modules.Menu;
using CS2_CustomVotes.Extensions;
using MenuManagerAPI.Shared;
using CS2_CustomVotes.Shared.Models;
using Timer = CounterStrikeSharp.API.Modules.Timers.Timer;

namespace CS2_CustomVotes.Models;

public class ActiveVote
{
    private readonly CustomVotes _plugin;
    public ActiveVote(CustomVotes plugin, CustomVote vote, IEnumerable<uint> eligibleVoters)
    {
        _plugin = plugin;
        Vote = vote;
        OptionVotes = vote.Options.ToDictionary(x => x.Key, _ => new List<uint>());
        EligibleVoters = new HashSet<uint>(eligibleVoters);
    }
    
    public CustomVote Vote { get; set; }
    public Dictionary<string, List<uint>> OptionVotes { get; set; }
    public HashSet<uint> EligibleVoters { get; }

    public Timer? VoteTimeout { get; set; }
    public IMenu? VoteMenu { get; set; }
    public PanoramaVoteInstance? PanoramaVote { get; set; }
    public bool UsePanorama { get; set; }

    public void OpenMenuForAll()
    {
        if (UsePanorama && PanoramaVote != null)
        {
            PanoramaVote.Display();
        }
        else if (VoteMenu != null)
        {
            var players = Utilities.GetPlayers().Where(p => p.IsPlayer()).ToList();
            foreach (var player in players)
            {
                VoteMenu.Open(player);
            }
        }
    }

    public void CloseMenuForAll()
    {
        if (UsePanorama && PanoramaVote != null)
        {
            PanoramaVote.EndVote(VoteEndReason.VoteEnd_Cancelled);
        }
        else if (VoteMenu != null)
        {
            if (_plugin.MenuManagerApi != null)
            {
                _plugin.MenuManagerApi.CloseMenuForAll();
            }
            else
            {
                var players = Utilities.GetPlayers().Where(p => p.IsPlayer()).ToList();
                foreach (var player in players)
                {
                    if (VoteMenu is IDisposable disposable)
                        disposable.Dispose();
                }
            }
        }
    }

    public KeyValuePair<string, List<uint>> GetWinningOption()
    {
        if (OptionVotes.All(o => o.Value.Count == 0))
            return new KeyValuePair<string, List<uint>>(Vote.DefaultOption, []);
        
        var winningOption = OptionVotes.MaxBy(x => x.Value.Count);
        var totalVotes = OptionVotes.Sum(x => x.Value.Count);

        if (Vote.MinVotePercentage < 0 || winningOption.Value.Count >= totalVotes * Vote.MinVotePercentage / 100) 
            return winningOption;
        
        return new KeyValuePair<string, List<uint>>(Vote.DefaultOption, []);
    }
}