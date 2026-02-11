using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Menu;
using CS2_CustomVotes.Extensions;
using CS2_CustomVotes.Models;
using CSSharpUtils.Utils;
using CS2_CustomVotes.Shared.Models;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;

namespace CS2_CustomVotes.Factories;

public interface IActiveVoteFactory
{
    public ActiveVote Create(CustomVote vote, Action<string, VoteEndReason> onEndVote, Action<CCSPlayerController?, string> onPlayerVoted, CCSPlayerController? voteCaller);
}

public class ActiveVoteFactory : IActiveVoteFactory
{
    private readonly CustomVotes _plugin;
    private readonly IStringLocalizer _localizer;

    public ActiveVoteFactory(CustomVotes plugin, IStringLocalizer localizer)
    {
        _plugin = plugin;
        _localizer = localizer;
    }

    public ActiveVote Create(CustomVote vote, Action<string, VoteEndReason> onEndVote, Action<CCSPlayerController?, string> onPlayerVoted, CCSPlayerController? voteCaller)
    {
        var eligibleVoters = Utilities.GetPlayers().Where(p => p.IsPlayer()).Select(p => p.Pawn.Index).ToList();
        
        // determine if we should use Panorama vote
        bool usePanorama = vote.UsePanoramaVote;
        if (usePanorama && vote.Options.Count != 2)
        {
            _plugin.Logger.LogWarning("[CustomVotes] Panorama votes only support 2 options. Vote {Name} has {Count} - falling back to menu UI.", vote.Command, vote.Options.Count);
            usePanorama = false;
        }
        var normalizedVote = usePanorama ? NormalizePanoramaVote(vote) : vote;
        var activeVote = new ActiveVote(_plugin, normalizedVote, eligibleVoters);
        activeVote.UsePanorama = usePanorama;

        if (usePanorama)
        {
            // Create Panorama vote
            activeVote.PanoramaVote = new PanoramaVoteInstance(
                _plugin,
                normalizedVote,
                onEndVote,
                onPlayerVoted,
                voteCaller
            );
        }
        else
        {
            // start vote timeout and save handle
            activeVote.VoteTimeout = _plugin.AddTimer(activeVote.Vote.TimeToVote, () => onEndVote(vote.Command, VoteEndReason.VoteEnd_TimeUp));

            // Use MenuManagerAPI for vote menu
            if (_plugin.MenuManagerApi != null)
            {
                activeVote.VoteMenu = _plugin.MenuManagerApi.GetMenu(normalizedVote.Description);
                var optionLookup = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (var voteOption in normalizedVote.Options)
                {
                    var optionLabel = _localizer[voteOption.Value.Text].Value;
                    optionLookup[ChatUtils.CleanMessage(optionLabel)] = voteOption.Key;
                }
                
                foreach (var voteOption in normalizedVote.Options)
                {
                    activeVote.VoteMenu.AddMenuOption(
                        _localizer[voteOption.Value.Text],
                        (caller, option) =>
                        {
                            var cleanedText = ChatUtils.CleanMessage(option.Text);
                            if (optionLookup.TryGetValue(cleanedText, out var optionKey))
                                onPlayerVoted(caller, optionKey);
                        }
                    );
                }
            }
            else
            {
                // Fallback to basic chat/center menus if MenuManagerAPI not available
                var style = _plugin.Config.ForceStyle == "none" ? normalizedVote.Style : _plugin.Config.ForceStyle;
                
                if (style == "center")
                    activeVote.VoteMenu = new CenterHtmlMenu(activeVote.Vote.Description, _plugin);
                else
                    activeVote.VoteMenu = new ChatMenu(activeVote.Vote.Description);

                foreach (var voteOption in activeVote.Vote.Options)
                    activeVote.VoteMenu.AddMenuOption(
                        style == "center" ? _localizer[voteOption.Key] : ChatUtils.FormatMessage(_localizer[voteOption.Value.Text]),
                        (caller, option) => onPlayerVoted(caller, option.Text)
                    );
            }
        }
        
        return activeVote;
    }

    private CustomVote NormalizePanoramaVote(CustomVote vote)
    {
        bool hasYes = vote.Options.Keys.Any(k => string.Equals(k, "Yes", StringComparison.OrdinalIgnoreCase));
        bool hasNo = vote.Options.Keys.Any(k => string.Equals(k, "No", StringComparison.OrdinalIgnoreCase));
        if (hasYes && hasNo)
            return vote;

        var options = vote.Options.ToList();
        if (options.Count != 2)
            return vote;

        _plugin.Logger.LogDebug("[CustomVotes] Panorama vote {Name} does not use Yes/No keys. Normalizing options by order: first=Yes, second=No.", vote.Command);

        var normalizedOptions = new Dictionary<string, VoteOption>(StringComparer.OrdinalIgnoreCase)
        {
            { "Yes", options[0].Value },
            { "No", options[1].Value }
        };

        return new CustomVote
        {
            Command = vote.Command,
            CommandAliases = vote.CommandAliases,
            Description = vote.Description,
            // ...existing code...
            TimeToVote = vote.TimeToVote,
            Options = normalizedOptions,
            DefaultOption = "No",
            Style = vote.Style,
            MinVotePercentage = vote.MinVotePercentage,
            MinParticipationPercentage = vote.MinParticipationPercentage,
            Permission = vote.Permission,
            UsePanoramaVote = vote.UsePanoramaVote,
            PanoramaDisplayToken = vote.PanoramaDisplayToken,
            PanoramaPassedToken = vote.PanoramaPassedToken,
            PanoramaPassedDetails = vote.PanoramaPassedDetails,
            PanoramaResult = vote.PanoramaResult,
            PanoramaHandler = vote.PanoramaHandler
        };
    }
}