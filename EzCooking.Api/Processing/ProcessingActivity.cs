using EzCooking.Api.Recipes;

namespace EzCooking.Api.Processing;

/// <summary>A processing milestone recorded by the worker.</summary>
public sealed record ProcessingEvent(DateTimeOffset At, string Message);

public sealed class ProcessingActivity
{
    private readonly Lock _sync = new();
    private Guid? _currentId;
    private readonly List<ProcessingEvent> _events = [];

    public void Start(Guid id)
    {
        lock (_sync)
        {
            _currentId = id;
            _events.Clear();
            _events.Add(new(DateTimeOffset.UtcNow, "Started processing."));
        }
    }

    public void Add(Guid id, string message)
    {
        lock (_sync)
        {
            if (_currentId != id)
            {
                throw new InvalidOperationException("Processing activity belongs to another job.");
            }

            _events.Add(new(DateTimeOffset.UtcNow, message));
            if (_events.Count > 12)
            {
                _events.RemoveAt(0);
            }
        }
    }

    public RecipeResponse Attach(RecipeResponse recipe)
    {
        lock (_sync)
        {
            return recipe with
            {
                Activity = recipe.Status == "processing" && _currentId == recipe.Id ? _events.ToArray() : []
            };
        }
    }

    public void Clear(Guid id)
    {
        lock (_sync)
        {
            if (_currentId != id)
            {
                return;
            }

            _currentId = null;
            _events.Clear();
        }
    }
}
