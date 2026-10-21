namespace ParcelFlow.Reporting.Domain;

public sealed class EventEnvelope
{
    private EventEnvelope() { }
    public EventEnvelope(Guid eventId, Guid sourceEventId)
    {
        if (eventId == Guid.Empty || sourceEventId == Guid.Empty)
            throw new ArgumentException("Envelope and source identifiers are required.");
        EventId = eventId;
        SourceEventId = sourceEventId;
    }
    public Guid EventId { get; private set; }
    public Guid SourceEventId { get; private set; }
}
