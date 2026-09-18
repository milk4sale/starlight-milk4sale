namespace Content.Shared._Starlight.Weapons.Melee;

[RegisterComponent]
public sealed partial class ShieldRaisedComponent : Component
{
    [DataField] public float Distance = 0.25f;

    [DataField] public float Speed = 3f;

    [DataField] public float RaisedDistance = 0.5f;

    [DataField] public float RaisedSpeed = 4f;

    [DataField] public float RaisedStaminaMultiplier = 2.5f;

    [DataField] public float RaiseAttackDelay = 2f;

    public TimeSpan? RaisedAt;

    public bool PendingRaisedBash;
}
