namespace Chomik.Models;

/// <summary>
/// Explicit state machine for the hamster. Replaces the original tangle of
/// boolean flags (is_typing, is_dragging, is_afk, etc.) with a single
/// enum that makes illegal state combinations impossible.
/// </summary>
public enum HamsterState
{
    Idle,
    IdleRandom,     // playing a one-off random idle animation
    Afk,            // user has been away too long
    Typing,         // user is typing
    Music,          // music is playing
    Dragging,       // user is dragging the hamster window
    FileDrag,       // a file is being dragged over the hamster
    Screenshot,     // screenshot animation playing
    Writing,        // write-mode bubble active
}
