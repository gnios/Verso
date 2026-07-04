using Whisper.net;

namespace Transcriba.Core.Engine;

public interface IWhisperFactoryLoader
{
    WhisperFactory Load(string modelPath);
}

public sealed class DefaultWhisperFactoryLoader : IWhisperFactoryLoader
{
    public WhisperFactory Load(string modelPath) => WhisperFactory.FromPath(modelPath);
}

public interface IWhisperFactoryCache : IDisposable
{
    WhisperFactory GetOrCreate(string modelPath);
    int LoadCount { get; }
}

public sealed class WhisperFactoryCache : IWhisperFactoryCache
{
    private readonly IWhisperFactoryLoader _loader;
    private readonly object _lock = new();
    private WhisperFactory? _factory;
    private string? _modelPath;

    public WhisperFactoryCache(IWhisperFactoryLoader? loader = null)
    {
        _loader = loader ?? new DefaultWhisperFactoryLoader();
    }

    public int LoadCount { get; private set; }

    public WhisperFactory GetOrCreate(string modelPath)
    {
        lock (_lock)
        {
            if (_modelPath == modelPath)
                return _factory!;

            _factory?.Dispose();
            _factory = _loader.Load(modelPath);
            _modelPath = modelPath;
            LoadCount++;
            return _factory;
        }
    }

    public void Dispose()
    {
        lock (_lock)
        {
            _factory?.Dispose();
            _factory = null;
            _modelPath = null;
        }
    }
}
