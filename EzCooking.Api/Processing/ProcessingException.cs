namespace EzCooking.Api.Processing;

public sealed class ProcessingException(string message) : Exception(message)
{
}
