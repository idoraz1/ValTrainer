using ValTrainer.Game.Weapon;

namespace ValTrainer.Game;

/// <summary>
/// The session's "ability in hand" state for agent drills (<see cref="IGame.SetAbilityInHand"/>): while an ability is
/// equipped the gun doesn't fire or aim (RunFrame checks <see cref="abilityInHand"/>) and its viewmodel is hidden; putting
/// the ability away re-draws the gun with a draw time and replays the viewmodel's raise.
/// </summary>
public partial class GameSession
{
    bool abilityInHand;

    public bool AbilityInHand => abilityInHand;

    public void SetAbilityInHand(bool inHand, float drawSeconds = -1f)
    {
        if (abilityInHand == inHand) return;
        abilityInHand = inHand;
        if (inHand)
        {
            adsHeld = false;
            return;
        }
        if (Gun != null && weaponDef != null) Gun.EquipLeft = drawSeconds >= 0f ? drawSeconds : weaponDef.Equip;
        // A fresh viewmodel plays its raise animation (it starts lowered).
        if (viewmodel != null && weaponDef != null && cam != null && IsInstanceValid(cam))
        {
            viewmodel.QueueFree();
            viewmodel = Viewmodel.Create(weaponDef.Kind, Main.I.Settings.LeftHandedWeapon);
            cam.AddChild(viewmodel);
        }
    }
}
