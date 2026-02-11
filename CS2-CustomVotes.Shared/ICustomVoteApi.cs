using CounterStrikeSharp.API.Core;
using CS2_CustomVotes.Shared.Models;

namespace CS2_CustomVotes.Shared;

public interface ICustomVoteApi
{
    public void AddCustomVote(string name, string description, string defaultOption, float timeToVote, Dictionary<string, VoteOption> options, string style);
    public void AddCustomVote(string name, List<string> aliases, string description, string defaultOption, float timeToVote, Dictionary<string, VoteOption> options, string style);
    public void AddCustomVote(string name, string description, string defaultOption, float timeToVote, Dictionary<string, VoteOption> options, string style, int minVotePercentage);
    public void AddCustomVote(string name, List<string> aliases, string description, string defaultOption, float timeToVote, Dictionary<string, VoteOption> options, string style, int minVotePercentage);
    public void AddCustomVote(string name, string description, string defaultOption, float timeToVote, Dictionary<string, VoteOption> options, string style, int minVotePercentage, int minParticipationPercentage);
    public void AddCustomVote(string name, List<string> aliases, string description, string defaultOption, float timeToVote, Dictionary<string, VoteOption> options, string style, int minVotePercentage, int minParticipationPercentage);
    public void AddCustomVote(string name, string description, string defaultOption, float timeToVote, Dictionary<string, VoteOption> options, string style, int minVotePercentage, bool usePanoramaVote);
    public void AddCustomVote(string name, List<string> aliases, string description, string defaultOption, float timeToVote, Dictionary<string, VoteOption> options, string style, int minVotePercentage, bool usePanoramaVote);
    public void AddCustomVote(string name, string description, string defaultOption, float timeToVote, Dictionary<string, VoteOption> options, string style, int minVotePercentage, int minParticipationPercentage, bool usePanoramaVote);
    public void AddCustomVote(string name, List<string> aliases, string description, string defaultOption, float timeToVote, Dictionary<string, VoteOption> options, string style, int minVotePercentage, int minParticipationPercentage, bool usePanoramaVote);
    public void AddCustomVote(string name, string description, string defaultOption, float timeToVote, Dictionary<string, VoteOption> options, string style, int minVotePercentage, int minParticipationPercentage, bool usePanoramaVote, Func<YesNoVoteInfo, bool>? resultCallback, Action<YesNoVoteAction, int, int, VoteEndReason>? handler);
    public void AddCustomVote(string name, List<string> aliases, string description, string defaultOption, float timeToVote, Dictionary<string, VoteOption> options, string style, int minVotePercentage, int minParticipationPercentage, bool usePanoramaVote, Func<YesNoVoteInfo, bool>? resultCallback, Action<YesNoVoteAction, int, int, VoteEndReason>? handler);
    public void AddCustomVote(string name, string description, string defaultOption, float timeToVote, Dictionary<string, VoteOption> options, string style, int minVotePercentage, int minParticipationPercentage, bool usePanoramaVote, string? panoramaDisplayToken, string? panoramaPassedToken, string? panoramaPassedDetails, Func<YesNoVoteInfo, bool>? resultCallback, Action<YesNoVoteAction, int, int, VoteEndReason>? handler);
    public void AddCustomVote(string name, List<string> aliases, string description, string defaultOption, float timeToVote, Dictionary<string, VoteOption> options, string style, int minVotePercentage, int minParticipationPercentage, bool usePanoramaVote, string? panoramaDisplayToken, string? panoramaPassedToken, string? panoramaPassedDetails, Func<YesNoVoteInfo, bool>? resultCallback, Action<YesNoVoteAction, int, int, VoteEndReason>? handler);
    public void StartCustomVote(CCSPlayerController? player, string name);
    public void EndCustomVote(string name);
    public void RemoveCustomVote(string name);
}
