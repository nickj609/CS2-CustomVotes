using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Menu;
using CS2_CustomVotes.Extensions;
using CS2_CustomVotes.Factories;
using CS2_CustomVotes.Models;
using CS2_CustomVotes.Shared.Models;
using CSSharpUtils.Utils;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;

namespace CS2_CustomVotes.Services;

public interface IVoteManager
{
    public void AddVote(CustomVote vote);
    public void AddVote(string name, List<string> aliases, string description, string defaultOption, float timeToVote, Dictionary<string, VoteOption> options, string style = "center", int minVotePercentage = 50, int minParticipationPercentage = -1, bool usePanoramaVote = false, string? panoramaDisplayToken = null, string? panoramaPassedToken = null, string? panoramaPassedDetails = null, Func<YesNoVoteInfo, bool>? resultCallback = null, Action<YesNoVoteAction, int, int, VoteEndReason>? handler = null);
    public void RemoveVote(string name);

    public bool StartVote(CCSPlayerController? player, string name, out string baseName);
    public void EndVote(string name, VoteEndReason reason = VoteEndReason.VoteEnd_TimeUp);
    
    public void OnPlayerVoted(CCSPlayerController? player, string option);
    public HookResult OnPlayerConnectFull(EventPlayerConnectFull @event, GameEventInfo _);
    public HookResult OnPlayerDisconnect(EventPlayerDisconnect @event, GameEventInfo _);
}

public class VoteManager : IVoteManager
{
    private readonly ILogger<VoteManager> _logger;
    private readonly CustomVotes _plugin;
    private readonly IStringLocalizer _localizer;
    private readonly IActiveVoteFactory _activeVoteFactory;

    public VoteManager(ILogger<VoteManager> logger, CustomVotes plugin, IStringLocalizer localizer, IActiveVoteFactory activeVoteFactory)
    {
        _logger = logger;
        _plugin = plugin;
        _localizer = localizer;
        _activeVoteFactory = activeVoteFactory;
    }
    private Dictionary<string, CustomVote> Votes { get; set; } = new();
    private ActiveVote? ActiveVote { get; set; }
    private float _nextVoteTime;
    private bool _isEndingVote;

    public void AddVote(CustomVote vote)
    {
        if (vote.Options.Count < 2)
        {
            _logger.LogWarning("[CustomVotes] Vote {Name} must have at least 2 options", vote.Command);
            return;
        }
        
        if (!vote.Options.TryGetValue(vote.DefaultOption, out _))
        {
            _logger.LogWarning("[CustomVotes] Default option {Option} of {Name} is invalid", vote.DefaultOption, vote.Command);
            return;
        }
        
        if (!Votes.TryAdd(vote.Command, vote))
        {
            _logger.LogWarning("[CustomVotes] Vote {Name} already exists", vote.Command);
            return;
        }

        _logger.LogDebug("[CustomVotes] Vote {Name} added", vote.Command);
        
        _plugin.AddCommand(vote.Command, vote.Description, HandleVoteStartRequest);
        foreach (var alias in vote.CommandAliases)
            _plugin.AddCommand(alias, vote.Description, HandleVoteStartRequest);
        
        vote.ExecuteCommand();
    }
    public void AddVote(string name, List<string> aliases, string description, string defaultOption, float timeToVote, Dictionary<string, VoteOption> options, string style = "center", int minVotePercentage = 50, int minParticipationPercentage = -1, bool usePanoramaVote = false, string? panoramaDisplayToken = null, string? panoramaPassedToken = null, string? panoramaPassedDetails = null, Func<YesNoVoteInfo, bool>? resultCallback = null, Action<YesNoVoteAction, int, int, VoteEndReason>? handler = null)
    {
        var customVote = new CustomVote
        {
            Command = name,
            CommandAliases = aliases,
            Description = description,
            DefaultOption = defaultOption,
            TimeToVote = timeToVote,
            Options = options,
            Style = style,
            MinVotePercentage = minVotePercentage,
            MinParticipationPercentage = minParticipationPercentage,
            UsePanoramaVote = usePanoramaVote,
            PanoramaDisplayToken = panoramaDisplayToken,
            PanoramaPassedToken = panoramaPassedToken,
            PanoramaPassedDetails = panoramaPassedDetails,
            PanoramaResult = resultCallback,
            PanoramaHandler = handler
        };
        AddVote(customVote);
    }
    public void RemoveVote(string name)
    {
        if (!Votes.ContainsKey(name))
            _logger.LogWarning("[CustomVotes] Vote {Name} does not exist", name);
        
        _plugin.RemoveCommand(name, HandleVoteStartRequest);
        foreach (var alias in Votes[name].CommandAliases)
            _plugin.RemoveCommand(alias, HandleVoteStartRequest);
        
        if (!Votes.Remove(name))
            _logger.LogWarning("[CustomVotes] Could not remove {Name}", name);
        
        _logger.LogDebug("[CustomVotes] Vote {Name} removed", name);
    }
    
    public bool StartVote(CCSPlayerController? player, string name, out string baseName)
    {
        /// check if vote exists
        if (!Votes.TryGetValue(name, out var vote))
            // might be an alias
            vote ??= Votes.FirstOrDefault(v => v.Value.CommandAliases.Contains(name)).Value;
        
        // set base name for logging and chat message
        baseName = vote?.Command ?? string.Empty;
        
        if (vote == null)
        {
            _logger.LogWarning("[CustomVotes] Vote {Name} does not exist", name);
            return false;
        }
        
        if (_nextVoteTime > Server.CurrentTime)
        {
            player!.PrintToChat($"{ChatUtils.FormatMessage(_plugin.Config.ChatPrefix)} {ChatUtils.FormatMessage(_localizer["vote.cooldown", _plugin.Config.VoteCooldown])}");
            return false;
        }
        
        if (!vote.CheckPermissions(player))
        {
            player!.PrintToChat($"{ChatUtils.FormatMessage(_plugin.Config.ChatPrefix)} {ChatUtils.FormatMessage(_localizer["vote.no_permission"])}");
            return false;
        }

        // print vote started message before opening menu
        Server.PrintToChatAll($"{ChatUtils.FormatMessage(_plugin.Config.ChatPrefix)} {ChatUtils.FormatMessage(_localizer["vote.started", player!.PlayerName, vote.Command])}");

        // create new active vote
        ActiveVote = _activeVoteFactory.Create(vote, EndVote, OnPlayerVoted, player);
        ActiveVote.OpenMenuForAll();

        return true;
    }
    public void EndVote(string name, VoteEndReason reason)
    {
        if (ActiveVote == null)
        {
            _logger.LogWarning("[CustomVotes] No vote is active");
            return;
        }
        
        if (ActiveVote.Vote.Command != name && 
            !ActiveVote.Vote.CommandAliases.Contains(name))
        {
            _logger.LogWarning("[CustomVotes] Vote {Name} is not active", name);
            return;
        }

        if (_isEndingVote)
        {
            _logger.LogDebug("[CustomVotes] Vote {Name} is already ending", name);
            return;
        }

        _isEndingVote = true;
        try
        {
            if (reason != VoteEndReason.VoteEnd_Cancelled)
                ProcessVoteResults();
            else
                _logger.LogDebug("[CustomVotes] Vote {Name} cancelled", name);
            
            // kill vote timeout timer and reset active vote
            if (!(ActiveVote.UsePanorama && ActiveVote.PanoramaVote?.IsEnded == true))
                ActiveVote.CloseMenuForAll();
            ActiveVote.VoteTimeout?.Kill();
            ActiveVote = null;
            
            // set next vote time to prevent spam
            _nextVoteTime = Server.CurrentTime + _plugin.Config.VoteCooldown;
            
            _logger.LogDebug("[CustomVotes] Vote {Name} ended ({Reason})", name, reason);
        }
        finally
        {
            _isEndingVote = false;
        }
    }
    
    public void OnPlayerVoted(CCSPlayerController? player, string option)
    {
        if (ActiveVote == null)
        {
            _logger.LogWarning("[CustomVotes] No vote is active");
            return;
        }

        // clean any color codes etc
        option = ChatUtils.CleanMessage(option);
        
        if (!ActiveVote.Vote.Options.ContainsKey(option))
        {
            _logger.LogWarning("[CustomVotes] Option {Option} does not exist", option);
            return;
        }

        if (!player.IsPlayer())
        {
            _logger.LogDebug("[CustomVotes] Voter is not a valid player");
            return;
        }
        
        // mark player index as voted 
        if (!ActiveVote.OptionVotes[option].Contains(player!.Pawn.Index))
            ActiveVote.OptionVotes[option].Add(player.Pawn.Index);
        else
        {
            _logger.LogDebug("[CustomVotes] Player {Name} already voted", player.PlayerName);
            player.PrintToChat($"{ChatUtils.FormatMessage(_plugin.Config.ChatPrefix)} {ChatUtils.FormatMessage(_localizer["vote.already_voted"])}");
        }
        
        var players = Utilities.GetPlayers().Where(p => p.IsPlayer()).Select(p => p.Pawn.Index);
        var votePlayers = ActiveVote.OptionVotes.Values.SelectMany(p => p).Distinct();
        
        if (ActiveVote.UsePanorama)
            return;

        // if all players voted, end vote early
        if (players.All(votePlayers.Contains))
            EndVote(ActiveVote.Vote.Command, VoteEndReason.VoteEnd_AllVotes);
    }
    public HookResult OnPlayerConnectFull(EventPlayerConnectFull @event, GameEventInfo _)
    {
        var player = @event.Userid;
        
        if (!player.IsPlayer())
            return HookResult.Continue;

        if (ActiveVote == null)
            return HookResult.Continue;

        if (ActiveVote.UsePanorama)
            return HookResult.Continue;

        if (ActiveVote.VoteMenu != null)
        {
            ActiveVote.EligibleVoters.Add(player!.Pawn.Index);

            if (_plugin.MenuManagerApi != null)
            {
                _plugin.MenuManagerApi.OpenMenu(ActiveVote.VoteMenu, player!);
            }
            else if (ActiveVote.VoteMenu is CenterHtmlMenu)
            {
                MenuManager.OpenCenterHtmlMenu(_plugin, player!, (ActiveVote.VoteMenu! as CenterHtmlMenu)!);
            }
            else
            {
                MenuManager.OpenChatMenu(player!, (ActiveVote.VoteMenu! as ChatMenu)!);
            }
        }
        return HookResult.Continue;
    }
    public HookResult OnPlayerDisconnect(EventPlayerDisconnect @event, GameEventInfo _)
    {
        var player = @event.Userid;
        
        if (!player.IsPlayer())
            return HookResult.Continue;

        if (ActiveVote == null)
            return HookResult.Continue;

        if (ActiveVote.UsePanorama)
            ActiveVote.PanoramaVote?.OnPlayerDisconnected(player!);

        ActiveVote.EligibleVoters.Remove(player!.Pawn.Index);

        // remove player vote from all options
        foreach (var option in ActiveVote.OptionVotes.Where(option => option.Value.Contains(player!.Pawn.Index)))
            option.Value.Remove(player!.Pawn.Index);
        
        return HookResult.Continue;
    }

    private void ProcessVoteResults()
    {
        if (ActiveVote == null)
        {
            _logger.LogWarning("[CustomVotes] No vote is active");
            return;
        }

        var totalVotes = ActiveVote.OptionVotes.Sum(x => x.Value.Count);
        var eligibleCount = ActiveVote.EligibleVoters.Count;

        if (ActiveVote.Vote.MinParticipationPercentage >= 0 && eligibleCount > 0)
        {
            var requiredVotes = eligibleCount * (ActiveVote.Vote.MinParticipationPercentage / 100.0);
            if (totalVotes < requiredVotes)
            {
                Server.PrintToChatAll(
                    $"{ChatUtils.FormatMessage(_plugin.Config.ChatPrefix)} {ChatUtils.FormatMessage(_localizer["vote.failed_participation", ActiveVote.Vote.Command, totalVotes, eligibleCount, ActiveVote.Vote.MinParticipationPercentage])}");

                _logger.LogInformation("[CustomVotes] Vote for {Name} failed due to low participation ({Votes}/{Eligible}, min {Min}%)",
                    ActiveVote.Vote.Command, totalVotes, eligibleCount, ActiveVote.Vote.MinParticipationPercentage);
                return;
            }
        }
        
        var winningOption = ActiveVote.GetWinningOption();
        
        // announce winner and execute commands
        Server.PrintToChatAll($"{ChatUtils.FormatMessage(_plugin.Config.ChatPrefix)} {ChatUtils.FormatMessage(_localizer["vote.finished_with", ActiveVote.Vote.Command, winningOption.Key, winningOption.Value.Count])}");
        ActiveVote.Vote.ExecuteCommand(winningOption.Key);
        
        _logger.LogInformation("[CustomVotes] Vote for {Name} ended with {Option}", ActiveVote.Vote.Command, ChatUtils.CleanMessage(winningOption.Key));
    }
    
    private void HandleVoteStartRequest(CCSPlayerController? player, CommandInfo info)
    {
        if (!_plugin.Config.CustomVotesEnabled)
        {
            player!.PrintToChat($"{ChatUtils.FormatMessage(_plugin.Config.ChatPrefix)} {ChatUtils.FormatMessage(_localizer["vote.disabled"])}");
            _logger.LogWarning("[CustomVotes] Custom votes are disabled");
            return;
        }

        if (!player.IsPlayer())
        {
            _logger.LogDebug("[CustomVotes] Voter is not a valid player");
            return;
        }
        
        if (ActiveVote != null)
        {
            player!.PrintToChat($"{ChatUtils.FormatMessage(_plugin.Config.ChatPrefix)} {ChatUtils.FormatMessage(_localizer["vote.active"])}");
            return;
        }
        
        if(StartVote(player, info.GetArg(0), out var baseName))
            _logger.LogDebug("[CustomVotes] Vote {Name} started", baseName);
    }
}