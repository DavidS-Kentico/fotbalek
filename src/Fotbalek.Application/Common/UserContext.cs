using Fotbalek.Application.Common.Abstractions;

namespace Fotbalek.Application.Common;

/// <summary>
/// The scoped, settable <see cref="IUserContext"/> instance. The host's IScopedSender (or the
/// per-request seeding middleware) resolves this concrete type inside a fresh scope and seeds it
/// from the caller's principal before dispatching; handlers only ever see the read-only interface.
/// </summary>
public sealed class UserContext : IUserContext
{
    public int? UserId { get; set; }
    public bool IsAdmin { get; set; }
}
