using Silk.NET.Input;

namespace MandelbrotGpu;

internal enum InputSource
{
    MainWindow,
    Hud
}

internal readonly record struct AppCommand(Key Key, bool Shift, InputSource Source);
