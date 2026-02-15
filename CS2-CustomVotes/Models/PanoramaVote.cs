using CounterStrikeSharp.API;
using CS2_CustomVotes.Extensions;
using CounterStrikeSharp.API.Core;
using Microsoft.Extensions.Logging;
using CS2_CustomVotes.Shared.Models;
using CounterStrikeSharp.API.Modules.Utils;
using CounterStrikeSharp.API.Modules.Admin;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.UserMessages;
using Timer = CounterStrikeSharp.API.Modules.Timers.Timer;

namespace CS2_CustomVotes.Models;

public class PanoramaVoteInstance
{
    private const int VoteUncastValue = 5;
    private readonly CustomVotes _plugin;
    private readonly CustomVote _vote;
    private readonly Action<string, VoteEndReason> _onEndVote;
    private readonly Action<CCSPlayerController?, string> _onPlayerVoted;
    private readonly List<CCSPlayerController> _voters;
    private readonly int? _voteCallerSlot;
    private readonly List<int> _voterSlots = new();
    private int _voterCount;
    private string? _yesOptionKey;
    private string? _noOptionKey;
    private CVoteController? _voteController;
    private RecipientFilter? _currentVoteFilter;
    private Timer? _voteTimer;
    private bool _voteEnded;
    public bool IsEnded => _voteEnded;

    public PanoramaVoteInstance(CustomVotes plugin, CustomVote vote, Action<string, VoteEndReason> onEndVote, Action<CCSPlayerController?, string> onPlayerVoted, CCSPlayerController? voteCaller)
    {
        _plugin = plugin;
        _vote = vote;
        _onEndVote = onEndVote;
        _onPlayerVoted = onPlayerVoted;
        _voters = Utilities.GetPlayers().Where(p => p.IsPlayer()).ToList();
        _voteCallerSlot = voteCaller?.Slot;
        _voteEnded = false;

        InitializeVoteController();
    }

    private void InitializeVoteController()
    {
        var controllers = Utilities.FindAllEntitiesByDesignerName<CVoteController>("vote_controller").ToList();
        if (controllers.Count == 0)
        {
            _plugin.Logger.LogError("[CustomVotes] Panorama vote controller entity not found.");
            throw new InvalidOperationException("Could not find vote controller entity");
        }
        _voteController = controllers.Last();
    }

    public void Display()
    {
        InitializeVoters();
            
        if (_voterCount == 0)
            return;
        if (_voteController == null)
            return;

        _currentVoteFilter = new RecipientFilter(_voters.ToArray());
        _yesOptionKey = _vote.Options.Keys.FirstOrDefault(k => string.Equals(k, "Yes", StringComparison.OrdinalIgnoreCase));
        _noOptionKey = _vote.Options.Keys.FirstOrDefault(k => string.Equals(k, "No", StringComparison.OrdinalIgnoreCase));

        ResetVoteController();

        _voteController.PotentialVotes = _voterCount;
        _voteController.ActiveIssueIndex = 2;

        UpdateVoteCounts();
        RegisterCommands();
        SendVoteStartMessage(_currentVoteFilter);

        _plugin.RegisterEventHandler<EventVoteCast>(OnVoteCast);
        _voteTimer = _plugin.AddTimer(_vote.TimeToVote, () => EndVote(VoteEndReason.VoteEnd_TimeUp));

        _vote.PanoramaHandler?.Invoke(YesNoVoteAction.VoteAction_Start, 0, 0, VoteEndReason.VoteEnd_TimeUp);
    }

    private HookResult OnVoteCast(EventVoteCast @event, GameEventInfo info)
    {
        if (@event.Userid is not { } player)
            return HookResult.Continue;

        var voteOption = @event.VoteOption;
        var optionKey = voteOption switch
        {
            0 => _yesOptionKey,
            1 => _noOptionKey,
            _ => null
        };

        if (optionKey == null)
            return HookResult.Continue;

        _onPlayerVoted(player, optionKey);
        _vote.PanoramaHandler?.Invoke(YesNoVoteAction.VoteAction_Vote, player.Slot, voteOption, VoteEndReason.VoteEnd_TimeUp);
        UpdateVoteCounts();
        CheckForEarlyVoteClose();

        return HookResult.Continue;
    }

    private void ResetVoteController()
    {
        if (_voteController == null)
            return;

        for (int i = 0; i < _voteController.VotesCast.Length; i++)
        {
            _voteController.VotesCast[i] = VoteUncastValue;
            if (i < _voteController.VoteOptionCount.Length)
                _voteController.VoteOptionCount[i] = 0;
        }
    }

    private void InitializeVoters()
    {
        _voterSlots.Clear();
        foreach (var player in _voters)
        {
            if (player.Slot != -1)
                _voterSlots.Add(player.Slot);
        }
        _voterCount = _voterSlots.Count;
    }

    private void UpdateVoteCounts()
    {
        if (_voteController == null)
            return;

        new EventVoteChanged(true)
        {
            VoteOption1 = _voteController.VoteOptionCount[0],
            VoteOption2 = _voteController.VoteOptionCount[1],
            VoteOption3 = _voteController.VoteOptionCount[2],
            VoteOption4 = _voteController.VoteOptionCount[3],
            VoteOption5 = _voteController.VoteOptionCount[4],
            Potentialvotes = _voterCount
        }.FireEvent(false);
    }

    private void SendVoteStartMessage(RecipientFilter recipientFilter)
    {
        UserMessage um = UserMessage.FromId(346);
        um.SetInt("team", -1);
        um.SetInt("player_slot", _voteCallerSlot ?? 99);
        um.SetInt("vote_type", -1);
        um.SetString("disp_str", string.IsNullOrWhiteSpace(_vote.PanoramaDisplayToken)
            ? "#SFUI_vote_panorama_vote_default"
            : _vote.PanoramaDisplayToken);
        um.SetString("details_str", _vote.Description);
        um.SetBool("is_yes_no_vote", true);
        um.Send(recipientFilter);
    }

    private void CheckForEarlyVoteClose()
    {
        if (_voteController == null)
            return;

        int votes = _voteController.VoteOptionCount[0] + _voteController.VoteOptionCount[1];
        if (votes >= _voterCount)
                Server.NextFrame(() => EndVote(VoteEndReason.VoteEnd_AllVotes));
    }

    public void OnPlayerDisconnected(CCSPlayerController player)
    {
        if (_voteController == null)
            return;
        if (player.Slot < 0)
            return;
        if (!_voterSlots.Remove(player.Slot))
            return;

        _voterCount = _voterSlots.Count;

        if (player.Slot < _voteController.VotesCast.Length)
        {
            var vote = _voteController.VotesCast[player.Slot];
            if (vote != VoteUncastValue &&
                vote >= 0 &&
                vote < _voteController.VoteOptionCount.Length &&
                _voteController.VoteOptionCount[vote] > 0)
            {
                _voteController.VoteOptionCount[vote]--;
            }

            _voteController.VotesCast[player.Slot] = VoteUncastValue;
        }

        _voteController.PotentialVotes = _voterCount;
        UpdateVoteCounts();
    }

    public void EndVote(VoteEndReason reason)
    {
        if (_voteEnded)
            return;

        _voteEnded = true;
        _voteTimer?.Kill();
        _plugin.DeregisterEventHandler<EventVoteCast>(OnVoteCast);
        DeregisterCommands();

        if (_voteController != null)
            _voteController.ActiveIssueIndex = -1;

        if (reason == VoteEndReason.VoteEnd_Cancelled)
        {
            SendVoteFailedMessage(reason);
            _vote.PanoramaHandler?.Invoke(YesNoVoteAction.VoteAction_End, 0, 0, reason);
            _onEndVote(_vote.Command, reason);
            return;
        }

        var voteInfo = BuildYesNoVoteInfo();
        if (DidYesWin(voteInfo))
            SendVotePassedMessage();
        else
            SendVoteFailedMessage(reason);

        _vote.PanoramaHandler?.Invoke(YesNoVoteAction.VoteAction_End, 0, 0, reason);

        _onEndVote(_vote.Command, reason);
    }

    private void RegisterCommands()
    {
        _plugin.AddCommand("css_cancelvote", "Cancels the active vote.", CommandCancelVote);
        _plugin.AddCommand("css_revote", "Allows you to revote.", CommandRevote);
    }

    private void DeregisterCommands()
    {
        _plugin.RemoveCommand("css_cancelvote", CommandCancelVote);
        _plugin.RemoveCommand("css_revote", CommandRevote);
    }

    [RequiresPermissions("@css/root")]
    private void CommandCancelVote(CCSPlayerController? player, CommandInfo info)
    {
        EndVote(VoteEndReason.VoteEnd_Cancelled);
    }

    private void CommandRevote(CCSPlayerController? player, CommandInfo info)
    {
        Revote(player);
    }

    private void Revote(CCSPlayerController? player)
    {

        if (_voteController == null)
            return;
        if (player == null || _currentVoteFilter == null)
            return;
        if (!_currentVoteFilter.Contains(player))
            return;

        int vote = _voteController.VotesCast[player.Slot];
        if (vote != VoteUncastValue)
        {
            _voteController.VoteOptionCount[vote]--;
            _voteController.VotesCast[player.Slot] = VoteUncastValue;
            UpdateVoteCounts();
        }

        SendVoteStartMessage(new RecipientFilter(player));
    }

    private bool DidYesWin(YesNoVoteInfo voteInfo)
    {
        if (_vote.PanoramaResult != null)
            return _vote.PanoramaResult(voteInfo);
        if (voteInfo.TotalVotes == 0)
            return false;
        if (_vote.MinVotePercentage >= 0 && voteInfo.YesVotes < voteInfo.TotalVotes * _vote.MinVotePercentage / 100.0)
            return false;

        return voteInfo.YesVotes > voteInfo.NoVotes;
    }

    private void SendVoteFailedMessage(VoteEndReason reason)
    {
        if (_currentVoteFilter == null)
            return;

        UserMessage um = UserMessage.FromId(348);
        um.SetInt("team", -1);
        um.SetInt("reason", (int)reason);
        um.Send(_currentVoteFilter);
    }

    private YesNoVoteInfo BuildYesNoVoteInfo()
    {
        var info = new YesNoVoteInfo
        {
            TotalClients = _voterCount,
            YesVotes = _voteController?.VoteOptionCount[0] ?? 0,
            NoVotes = _voteController?.VoteOptionCount[1] ?? 0
        };

        info.TotalVotes = info.YesVotes + info.NoVotes;

        var clientCount = _currentVoteFilter?.Count ?? 0;
        info.ClientInfo = new (int ClientSlot, int Vote)[clientCount];

        for (int i = 0; i < clientCount; i++)
        {
            if (i < _voterCount)
            {
                var slot = _voterSlots[i];
                info.ClientInfo[i] = (slot, _voteController?.VotesCast[slot] ?? VoteUncastValue);
            }
            else
            {
                info.ClientInfo[i] = (-1, -1);
            }
        }

        return info;
    }

    private void SendVotePassedMessage()
    {
        if (_currentVoteFilter == null)
            return;
            
        UserMessage um = UserMessage.FromId(347);
        um.SetInt("team", -1);
        um.SetInt("vote_type", 2);
        um.SetString("disp_str", string.IsNullOrWhiteSpace(_vote.PanoramaPassedToken)
            ? "#SFUI_vote_passed_panorama_vote"
            : _vote.PanoramaPassedToken);
        um.SetString("details_str", string.IsNullOrWhiteSpace(_vote.PanoramaPassedDetails)
            ? _vote.Description
            : _vote.PanoramaPassedDetails);
        um.Send(_currentVoteFilter);
    }
}