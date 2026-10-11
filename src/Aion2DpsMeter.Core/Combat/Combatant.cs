namespace Aion2DpsMeter.Core.Combat;

/// <summary>
/// Ein Spieler (oder anderer Verursacher von Schaden) im Kampf.
/// </summary>
/// <param name="Id">Eindeutige Kennung innerhalb einer Kampfsitzung.</param>
/// <param name="Name">Anzeigename.</param>
/// <param name="IsLocalPlayer">true für die eigene Spielfigur.</param>
public sealed record Combatant(long Id, string Name, bool IsLocalPlayer = false);
