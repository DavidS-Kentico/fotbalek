using Fotbalek.Contracts.Players;
using Fotbalek.Contracts.Teams;

namespace Fotbalek.Contracts.Memberships;

/// <summary>One row per membership for the account page: team, captain flag, and the
/// user's claimed player in that team (null when none is claimed).</summary>
public record MembershipOverviewDto(TeamDto Team, DateTimeOffset JoinedAt, bool IsCaptain, PlayerDto? MyPlayer);
