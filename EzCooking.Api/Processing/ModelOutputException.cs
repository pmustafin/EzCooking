namespace EzCooking.Api.Processing;

public sealed class ModelOutputException(string message) : Exception(message)
{
}
