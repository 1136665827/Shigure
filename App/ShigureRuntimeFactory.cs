namespace Shigure;

internal interface IShigureRuntimeFactory
{
    ShigureRuntime Create(AppOptions options);
}

internal sealed class ShigureRuntimeFactory : IShigureRuntimeFactory
{
    private readonly string _baseDirectory;
    private readonly ModuleStore _moduleStore;
    private readonly ITriggerKeyState _triggerKeyState;
    private readonly WowProcessLocator _processLocator;
    private readonly TimeProvider _timeProvider;

    public ShigureRuntimeFactory(
        string baseDirectory,
        ModuleStore moduleStore,
        ITriggerKeyState triggerKeyState,
        WowProcessLocator processLocator,
        TimeProvider? timeProvider = null)
    {
        _baseDirectory = baseDirectory;
        _moduleStore = moduleStore;
        _triggerKeyState = triggerKeyState;
        _processLocator = processLocator;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public ShigureRuntime Create(AppOptions options)
    {
        _moduleStore.Reload();
        var config = ConfigService.LoadFromBaseDirectory(_baseDirectory);
        var keymap = new KeymapService(_baseDirectory, config);

        return new ShigureRuntime(
            options,
            new PixelScanner(_processLocator),
            new StateBuilder(config),
            new KeySender(_processLocator),
            _triggerKeyState,
            new LogicRegistry(keymap, _moduleStore, options.ModuleId),
            _timeProvider);
    }
}
