using System.Numerics;
using Content.Shared.Blocking;
using Content.Shared.Damage.Events;
using Content.Shared.Interaction.Events;
using Content.Shared.Throwing;
using Content.Shared.Weapons.Melee.Components;
using Content.Shared.Weapons.Melee.Events;
using Robust.Shared.Timing;

namespace Content.Shared._Starlight.Weapons.Melee;

public sealed partial class ShieldRaisedSystem : EntitySystem
{
    [Dependency] private SharedTransformSystem _transform = null!;
    [Dependency] private ThrowingSystem _throwing = null!;
    [Dependency] private IGameTiming _timing = null!;
    [Dependency] private BlockingSystem _blockingSystem = null!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<ShieldRaisedComponent, MeleeHitEvent>(OnMeleeHit);
        SubscribeLocalEvent<ShieldRaisedComponent, StaminaMeleeHitEvent>(OnStaminaMeleeHit);

        // Prevent attacking until the shield has been planted long enough.
        SubscribeLocalEvent<BlockingUserComponent, AttackAttemptEvent>(OnAttackAttempt);

        // Runs once the melee attack has actually been confirmed.
        SubscribeLocalEvent<ShieldRaisedComponent, AttemptMeleeEvent>(OnAttemptMelee);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var query =
            EntityQueryEnumerator<ShieldRaisedComponent, BlockingComponent>();

        while (query.MoveNext(out _, out var shieldRaised, out var blocking))
        {
            if (blocking.IsBlocking)
            {
                // Start the planting timer once when the shield becomes raised.
                shieldRaised.RaisedAt ??= _timing.CurTime;
            }
            else
            {
                // Reset everything once the shield is lowered.
                shieldRaised.RaisedAt = null;
                shieldRaised.PendingRaisedBash = false;
            }
        }
    }

    private void OnAttackAttempt(
        Entity<BlockingUserComponent> ent,
        ref AttackAttemptEvent args)
    {
        if (args.Disarm)
            return;

        if (args.Weapon is not { } weapon)
            return;

        if (!TryComp<ShieldRaisedComponent>(weapon.Owner, out var shieldRaised))
            return;

        if (!TryComp<BlockingComponent>(weapon.Owner, out var blocking))
            return;

        // Normal unraised attack.
        if (!blocking.IsBlocking)
            return;

        // Start the planting timer if it somehow has not started yet.
        if (shieldRaised.RaisedAt == null)
        {
            shieldRaised.RaisedAt = _timing.CurTime;
            args.Cancel();
            return;
        }

        var readyAt =
            shieldRaised.RaisedAt.Value +
            TimeSpan.FromSeconds(shieldRaised.RaiseAttackDelay);

        // Shield is still being planted.
        if (_timing.CurTime < readyAt)
            args.Cancel();
    }

    private void OnAttemptMelee(
        Entity<ShieldRaisedComponent> ent,
        ref AttemptMeleeEvent args)
    {
        if (!TryComp<BlockingComponent>(ent.Owner, out var blocking))
            return;

        if (!blocking.IsBlocking)
            return;

        if (ent.Comp.RaisedAt == null)
            return;

        var readyAt =
            ent.Comp.RaisedAt.Value +
            TimeSpan.FromSeconds(ent.Comp.RaiseAttackDelay);

        if (_timing.CurTime < readyAt)
            return;

        // Preserve the raised-bash bonuses for this attack.
        ent.Comp.PendingRaisedBash = true;

        // The strong bash consumes the raised stance.
        _blockingSystem.StopBlocking(
            ent.Owner,
            blocking,
            args.User);
    }

    private bool IsRaisedBash(Entity<ShieldRaisedComponent> ent)
    {
        if (ent.Comp.PendingRaisedBash)
            return true;

        return TryComp<BlockingComponent>(ent.Owner, out var blocking) &&
               blocking.IsBlocking;
    }

    private void OnStaminaMeleeHit(
        Entity<ShieldRaisedComponent> ent,
        ref StaminaMeleeHitEvent args)
    {
        if (!IsRaisedBash(ent))
            return;

        args.Multiplier *= ent.Comp.RaisedStaminaMultiplier;
    }

    private void OnMeleeHit(
        Entity<ShieldRaisedComponent> ent,
        ref MeleeHitEvent args)
    {
        if (!args.IsHit || args.HitEntities.Count == 0)
            return;

        var raised = IsRaisedBash(ent);

        var distance = raised
            ? ent.Comp.RaisedDistance
            : ent.Comp.Distance;

        var speed = raised
            ? ent.Comp.RaisedSpeed
            : ent.Comp.Speed;

        var userPosition = _transform.GetWorldPosition(args.User);

        foreach (var target in args.HitEntities)
        {
            var targetPosition =
                _transform.GetMapCoordinates(target).Position;

            var direction =
                args.Direction ?? targetPosition - userPosition;

            if (direction == Vector2.Zero)
                continue;

            var attemptEvent =
                new AttemptMeleeThrowOnHitEvent(target, args.User);

            RaiseLocalEvent(ent.Owner, ref attemptEvent);

            if (attemptEvent.Cancelled)
                continue;

            var startEvent =
                new MeleeThrowOnHitStartEvent(ent.Owner, args.User);

            RaiseLocalEvent(target, ref startEvent);

            _throwing.TryThrow(
                target,
                direction.Normalized() * distance,
                speed,
                args.User,
                predicted: true);
        }
    }
}
