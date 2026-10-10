namespace Clickra.Core.Application;

/// <summary>Resolves a conversion command to its single authoritative application use case.</summary>
public sealed class ConversionUseCaseRegistry
{
    private readonly IReadOnlyDictionary<string, IConversionUseCase> _useCases;
    private readonly IReadOnlyCollection<string> _commands;

    public ConversionUseCaseRegistry(IEnumerable<IConversionUseCase> useCases)
    {
        ArgumentNullException.ThrowIfNull(useCases);

        var map = new Dictionary<string, IConversionUseCase>(StringComparer.OrdinalIgnoreCase);
        foreach (IConversionUseCase useCase in useCases)
        {
            ArgumentNullException.ThrowIfNull(useCase);
            if (string.IsNullOrWhiteSpace(useCase.Command))
                throw new ArgumentException("Conversion use cases must declare a non-empty command.", nameof(useCases));
            if (!map.TryAdd(useCase.Command, useCase))
                throw new InvalidOperationException($"Multiple conversion use cases own command '{useCase.Command}'.");
        }

        _useCases = map;
        _commands = Array.AsReadOnly(map.Keys.ToArray());
    }

    public IReadOnlyCollection<string> Commands => _commands;

    public bool TryGet(string command, out IConversionUseCase? useCase)
    {
        if (string.IsNullOrWhiteSpace(command))
        {
            useCase = null;
            return false;
        }

        return _useCases.TryGetValue(command, out useCase);
    }

    public IConversionUseCase GetRequired(string command)
    {
        if (TryGet(command, out IConversionUseCase? useCase))
            return useCase!;
        throw new KeyNotFoundException($"No conversion use case owns command '{command}'.");
    }
}
