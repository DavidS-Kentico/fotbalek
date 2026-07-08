namespace Fotbalek.Web.Game;

/// <summary>
/// Structured, source-generated telemetry for the live game's latency instrumentation (§12). Every
/// event's rendered message starts with <c>GameLatency</c> and carries a <c>signal=</c> discriminator
/// (<c>client</c> / <c>tick</c> / <c>conn</c>), so App Insights KQL can filter on
/// <c>traces | where message startswith "GameLatency"</c> and read the numeric fields out of
/// <c>customDimensions</c>. The client/tick events fire only while a game is actively playing.
/// </summary>
internal static partial class GameTelemetry
{
    [LoggerMessage(
        EventId = 6001,
        Level = LogLevel.Information,
        Message = "GameLatency signal=client team={TeamId} room={RoomId} seat={Seat} " +
                  "rttMeanMs={RttMean:F1} rttP50Ms={RttP50:F1} rttP95Ms={RttP95:F1} rttMaxMs={RttMax:F1} rttN={RttN} " +
                  "gapMeanMs={GapMean:F1} gapP95Ms={GapP95:F1} gapMaxMs={GapMax:F1} gapN={GapN} " +
                  "frameMeanMs={FrameMean:F1} frameP95Ms={FrameP95:F1} frameMaxMs={FrameMax:F1} " +
                  "extrapFrames={ExtrapFrames} sampledFrames={SampledFrames}")]
    public static partial void ClientLatency(
        ILogger logger, int teamId, Guid roomId, int seat,
        double rttMean, double rttP50, double rttP95, double rttMax, int rttN,
        double gapMean, double gapP95, double gapMax, int gapN,
        double frameMean, double frameP95, double frameMax,
        int extrapFrames, int sampledFrames);

    [LoggerMessage(
        EventId = 6002,
        Level = LogLevel.Information,
        Message = "GameLatency signal=tick team={TeamId} room={RoomId} " +
                  "tickMeanMs={TickMean:F2} tickP95Ms={TickP95:F2} tickMaxMs={TickMax:F2} tickN={TickN} stalls={Stalls} " +
                  "sendMeanMs={SendMean:F2} sendP95Ms={SendP95:F2} sendMaxMs={SendMax:F2} " +
                  "players={Players}")]
    public static partial void TickCadence(
        ILogger logger, int teamId, Guid roomId,
        double tickMean, double tickP95, double tickMax, int tickN, int stalls,
        double sendMean, double sendP95, double sendMax,
        int players);

    [LoggerMessage(
        EventId = 6003,
        Level = LogLevel.Information,
        Message = "GameLatency signal=conn team={TeamId} user={UserId} transport={Transport}")]
    public static partial void Transport(ILogger logger, int teamId, int userId, string transport);
}
