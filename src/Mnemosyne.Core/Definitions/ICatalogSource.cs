namespace Mnemosyne.Core.Definitions;

/// <summary>Where the rows come from. The plugin's implementation reads Lumina; the tests read a fixture.</summary>
public interface ICatalogSource
{
    CatalogRows Load();
}
