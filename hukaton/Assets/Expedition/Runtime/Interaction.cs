namespace Hakaton.Expeditions
{
    /// <summary>
    /// Something in an outing the player uses with E: a chest, a ringing payphone, a hissing television, and whatever
    /// event comes next. <see cref="ExpeditionWorld"/> finds the one under the crosshair within reach (the first solid
    /// thing a thin cast meets must belong to it), shows its <see cref="Prompt"/> and calls <see cref="Interact"/> on E.
    /// Finds (<see cref="WorldLoot"/>) keep their own wider aim assist and magnet and are checked first.
    /// The component must be on the object hit or one of its parents.
    /// </summary>
    public interface IInteractable
    {
        /// <summary>False while it cannot be used (already used, busy): no focus, no prompt.</summary>
        bool CanInteract { get; }
        /// <summary>The HUD hint while it is aimed at, e.g. «E — открыть».</summary>
        string Prompt { get; }
        /// <summary>Called every frame it is under the crosshair (for a highlight); it resets itself when not called.</summary>
        void SetFocused(bool focused);
        /// <summary>The player pressed E on it. True when something happened.</summary>
        bool Interact();
    }
}
