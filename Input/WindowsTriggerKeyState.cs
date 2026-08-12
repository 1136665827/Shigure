namespace Shigure;

internal sealed class WindowsTriggerKeyState : ITriggerKeyState
{
    public int? ResolveVirtualKey(string keyName) => WindowsVirtualKeyMap.Resolve(keyName);

    public bool IsPressed(int virtualKey) => NativeMethods.IsKeyDown(virtualKey);
}
