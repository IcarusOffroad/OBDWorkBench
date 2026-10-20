# Shared GPS interface, version 1

Windows local named pipe: `Icarus.GPS.Data.v1`. Connect to server `.` with `PipeDirection.InOut`. No request is needed: one UTF-8 newline-terminated JSON Snapshot arrives each second. Maximum 15 active subscriptions plus the listening pipe per endpoint. Each subscriber is independent; a client blocked on writing is disconnected after three seconds. Consumers must treat a disconnect, a five-second heartbeat timeout, or an incompatible ProtocolVersion as unavailable and clear current GPS measurements.

Reference Core/Core.csproj and include GpsClient.cs. Use Subscribe in a background async loop, marshal updates to your UI, and retry with a short delay after connection errors. Subscribe throws on disconnect/timeouts; handle those and clear the displayed values. A canceled iterator releases its pipe.

```csharp
while (!cancellation.IsCancellationRequested)
{
    try
    {
        await foreach (var gps in GpsClient.Subscribe(cancellation))
        {
            if (gps.ProtocolVersion != 1) throw new IOException("Unsupported GPS protocol");
            // Marshal to UI / datalog queue as appropriate.
            // Check FixValid, PublishedUtc freshness, and nullable field presence.
            // Preserve PublishedUtc (PC observation) and GpsUtc (receiver time).
            // gps.SpeedKph is km/h; convert only in the presentation layer.
        }
    }
    catch (Exception e) when (e is IOException or TimeoutException or OperationCanceledException)
    {
        // Clear current position/speed; record a gap rather than holding the last value.
        if (cancellation.IsCancellationRequested) break;
        await Task.Delay(2000, cancellation);
    }
}
```

Snapshot schema is authoritative in Core/Protocol.cs. Data is observational, not suitable as a sole navigation/safety input. Latitude/longitude are decimal degrees; course is degrees true; altitude is GGA mean sea level meters. GPS UTC is available from RMC, with GGA using its date or the nearest machine UTC date if no RMC date has arrived. LastFixUtc is the PC's receipt time, not GPS UTC. The age/timer uses PC UTC and can be affected by a system clock change.

SatellitesInView is keyed by talker, e.g. GP and GL. GN can overlap those constellations: **do not sum all keys**. SatellitesUsed comes from GGA. Only a satellite position received in the last five seconds qualifies as FixValid. Speed/course each require a recent valid RMC; altitude requires recent valid GGA. Blank/unknown remains null, not zero.

Both desktop and service enforce pipe ACLs. Interactive local users can read the GPS feed, including coordinates and the most recent raw NMEA lines. Network logons are denied. This is intended for a trusted personal PC; on a shared PC all signed-in users can access GPS. The separate `Icarus.GPS.Config.v1` endpoint accepts one GpsConfig JSON line and returns `{ "Ok": true, "Error": "" }` or an error. Only elevated administrators and the service/system accounts can access configuration. No internet listener exists.

No automatic legacy-program detection or COM handoff is included. Integration uses subscriptions; existing direct-COM applications need GPS disabled explicitly first.
