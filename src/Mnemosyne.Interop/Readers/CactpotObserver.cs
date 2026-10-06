using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using Dalamud.Game.Chat;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using Mnemosyne.Core.Definitions;
using Mnemosyne.Core.Readers;

namespace Mnemosyne.Interop.Readers;

/// <summary>The two cactpot rows are counted by watching them happen, because no memory
/// holds a ticket count and the reads that would need a request. The plugin registers these handlers on the addon
/// lifecycle and the chat log; each only reads its arguments and the agent, never calls PreventOriginal, and assigns nothing.
/// A ticket bought while the plugin was off is not seen, and the row keeps its "as seen" marker to say so.</summary>
public sealed class CactpotObserver(Catalog catalog, CatalogExtras extras, Tracker tracker, Action<string> log)
{
    /// <summary>LogMessage row 4735, "You use &lt;num(lnum2)&gt; MGP to purchase a Jumbo Cactpot ticket with
    /// the numbers &lt;digit(lnum1,4)&gt;." A sheet row, so it does not depend on the client language, and it fires on the
    /// purchase itself, not on a window the player can cancel.</summary>
    public const uint JumboPurchaseMessage = 4735;

    /// <summary>The MGP price is lnum2, parameter index 1: taken to be so because the DailyDuty plugin reads lnum1 at index 0 for
    /// the same message; not yet confirmed independently.</summary>
    private const int PriceParameter = 1;

    private readonly ItemDefinition? _mini = catalog.TryGet("reset:cactpot:mini");
    private readonly ItemDefinition? _jumbo = catalog.TryGet("reset:cactpot:jumbo");
    private bool _numbersReported;

    /// <summary>"LotteryDaily" PostSetup: one fresh ticket. A fresh ticket shows one of its nine numbers and a reopened one
    /// more than one, so the second is not counted again (that Numbers is filled at PostSetup is taken to be so; not yet
    /// confirmed). The first opening of a session logs how many numbers it saw.
    /// `tracker.Current == null` stands in for the player-state guard every other client read has: the addon this fires from
    /// exists only in a session, so this was safe either way, but it now reads like the readers.</summary>
    public void OnMiniCactpot(AddonEvent type, AddonArgs args)
    {
        try
        {
            if (_mini == null || tracker.Current == null) return;
            var revealed = RevealedNumbers();
            if (!_numbersReported)
            {
                _numbersReported = true;
                log($"mini cactpot window set up with {(revealed is { } n ? n.ToString() : "no readable")} number(s) revealed");
            }
            if (revealed is > 1) return;
            tracker.Observe(_mini.Id, new Observation(ObservationKind.OneMore, 1, extras.ResetCaps.MiniCactpotTickets, _mini.Reset.Schedule));
        }
        catch (Exception ex)
        {
            // A handler runs inside the game's own call; nothing here may escape it.
            log($"mini cactpot observation failed: {ex}");
        }
    }

    /// <summary>Reads a Jumbo Cactpot purchase out of a chat-log message while the message is still valid; it lives only inside
    /// the callback. The price names the ticket; a price outside the ladder is one more. The parameters are
    /// logged for reference.</summary>
    public bool TryReadJumboPurchase(ILogMessage message, out Observation observation)
    {
        observation = null!;
        if (_jumbo == null || message.LogMessageId != JumboPurchaseMessage) return false;
        var cap = extras.ResetCaps.JumboCactpotTickets;
        var priced = message.TryGetIntParameter(PriceParameter, out var mgp);
        observation = priced && Observations.JumboTicketOrdinal(mgp) is { } ordinal
            ? new Observation(ObservationKind.AtLeast, ordinal, cap, _jumbo.Reset.Schedule)
            : new Observation(ObservationKind.OneMore, 1, cap, _jumbo.Reset.Schedule);
        log($"jumbo cactpot purchase seen: {message.ParameterCount} parameter(s), price parameter {(priced ? mgp.ToString() : "unreadable")}");
        return true;
    }

    /// <summary>Applies a purchase read by TryReadJumboPurchase. Kept apart from the read so the plugin can move it to the
    /// framework thread when the chat hook did not deliver it there.</summary>
    public void ObserveJumbo(Observation observation)
    {
        if (_jumbo != null) tracker.Observe(_jumbo.Id, observation);
    }

    /// <summary>AgentLotteryDaily.Numbers, nine bytes; a zero is a hidden space. Null when the agent is absent.
    /// UpdateNumber and UpdatePayout are writes and are never called.</summary>
    private static unsafe int? RevealedNumbers()
    {
        var agent = AgentLotteryDaily.Instance();
        if (agent == null) return null;
        var revealed = 0;
        foreach (var number in agent->Numbers)
        {
            if (number != 0) revealed++;
        }
        return revealed;
    }
}
