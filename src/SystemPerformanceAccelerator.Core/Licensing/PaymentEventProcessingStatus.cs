namespace SystemPerformanceAccelerator.Core.Licensing;

public enum PaymentEventProcessingStatus
{
    Applied,
    Duplicate,
    IgnoredOutOfOrder,
    Rejected
}