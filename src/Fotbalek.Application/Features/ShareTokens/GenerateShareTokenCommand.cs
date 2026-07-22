using System.Security.Cryptography;
using Fotbalek.Application.Common;
using Fotbalek.Application.Common.Abstractions;
using Fotbalek.Application.Common.Authorization;
using Fotbalek.Domain.Entities;
using Fotbalek.SharedKernel;

namespace Fotbalek.Application.Features.ShareTokens;

/// <summary>
/// Generates a 24h share link token for a team. The caller must be a member of that team.
/// </summary>
public sealed record GenerateShareTokenCommand(int TeamId) : ICommand<string>;

internal sealed class GenerateShareTokenCommandHandler(IAppDbContext db, TeamAccess teamAccess)
    : ICommandHandler<GenerateShareTokenCommand, string>
{
    public async Task<Result<string>> Handle(GenerateShareTokenCommand command, CancellationToken cancellationToken)
    {
        if (!await teamAccess.IsMemberAsync(command.TeamId, cancellationToken))
            return Result.Failure<string>(CommonErrors.NotMember);

        // Generate a secure random token
        var tokenBytes = RandomNumberGenerator.GetBytes(32);
        var token = Convert.ToBase64String(tokenBytes)
            .Replace("+", "-")
            .Replace("/", "_")
            .TrimEnd('=');

        db.ShareTokens.Add(new ShareToken
        {
            TeamId = command.TeamId,
            Token = token,
            ExpiresAt = DateTimeOffset.UtcNow.AddHours(Constants.TimeThresholds.ShareTokenExpirationHours),
            CreatedAt = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync(cancellationToken);

        return token;
    }
}
