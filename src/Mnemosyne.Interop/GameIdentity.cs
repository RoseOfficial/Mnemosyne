using Dalamud.Plugin.Services;
using Mnemosyne.Core.Readers;

namespace Mnemosyne.Interop;

/// <summary>Identity over Dalamud's player-state service, which is valid from login to logout independent of the
/// player object. The data centre is resolved through the world sheet's data-centre link, never parsed from the
/// world name. Every member is safe to read when IsLoaded is false; it then returns 0 or "".</summary>
public sealed class GameIdentity(IPlayerState playerState) : ICharacterIdentity
{
    public bool IsLoaded => playerState.IsLoaded;
    public ulong ContentId => playerState.IsLoaded ? playerState.ContentId : 0;
    public string Name => playerState.IsLoaded ? playerState.CharacterName : "";
    public string HomeWorld => playerState.IsLoaded ? playerState.HomeWorld.ValueNullable?.Name.ExtractText() ?? "" : "";
    public string DataCenter => playerState.IsLoaded ? playerState.HomeWorld.ValueNullable?.DataCenter.ValueNullable?.Name.ExtractText() ?? "" : "";
}
