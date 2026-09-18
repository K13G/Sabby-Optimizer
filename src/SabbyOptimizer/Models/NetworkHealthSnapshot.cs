namespace PCTweaker.Models;

public sealed record NetworkHealthSnapshot(
    string Adapter,
    string InterfaceType,
    int Mtu,
    long LinkSpeedMbps,
    long ReceivedErrors,
    long SentErrors,
    long ReceivedDiscards,
    long SentDiscards,
    long BytesReceived,
    long BytesSent,
    int ActiveTcpConnections)
{
    public long TotalErrors => ReceivedErrors + SentErrors;
    public long TotalDiscards => ReceivedDiscards + SentDiscards;
}
