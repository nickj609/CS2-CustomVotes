using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Menu;
using CS2_CustomVotes.Extensions;
using CS2_CustomVotes.Models;
using CSSharpUtils.Utils;
using CS2_CustomVotes.Shared.Models;
using Microsoft.Extensions.Localization;

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

    public ActiveVote Create(CustomVote vote, Action<string, VoteEndReason> onEndVote, Action<CCSPlayerController?, string> onPlayerVoted, CCSPlayerController? voteCaller = null)
    {
        var eligibleVoters = Utilities.GetPlayers().Where(p => p.IsPlayer()).Select(p => p.Pawn.Index).ToList();
        var activeVote = new ActiveVote(_plugin, vote, eligibleVoters);
        
        if (vote.UsePanoramaVote)
        {
            // Always use Yes/No options for Panorama votes, ignore config
            vote = new CustomVote
            {
                Command = vote.Command,
                CommandAliases = vote.CommandAliases,
                Description = vote.Description,
                TimeToVote = vote.TimeToVote,
                Options = new Dictionary<string, VoteOption>(StringComparer.OrdinalIgnoreCase)
                {
                    { "Yes", new VoteOption("Yes", new List<string>()) },
                    { "No", new VoteOption("No", new List<string>()) }
                },
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

            // Create Panorama vote
            activeVote.UsePanorama = vote.UsePanoramaVote;
            activeVote.PanoramaVote = new PanoramaVoteInstance(
                _plugin,
                vote,
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
                MenuManagerAPI.Shared.Models.MenuType? menuType = null;

                if (Enum.TryParse<MenuManagerAPI.Shared.Models.MenuType>(_plugin.Config.ForceStyle, true, out var parsedType))
                    menuType = parsedType;

                if (menuType.HasValue)
                    activeVote.VoteMenu = _plugin.MenuManagerApi.GetMenu(vote.Description, null, null, menuType);
                else
                    activeVote.VoteMenu = _plugin.MenuManagerApi.GetMenu(vote.Description);

                foreach (var voteOption in activeVote.Vote.Options)
                    activeVote.VoteMenu.AddMenuOption(
                        _localizer[voteOption.Value.Text],
                        (caller, option) => onPlayerVoted(caller,  voteOption.Key)
                    );
            }
            else // Fallback to basic chat/center menus if MenuManagerAPI not available
            {
                var style = _plugin.Config.ForceStyle == "none" ? vote.Style : _plugin.Config.ForceStyle;
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
}