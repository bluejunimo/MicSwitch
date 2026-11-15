namespace MicSwitch.Shell;

/// <summary>Lets view models end the app the orderly way.</summary>
internal sealed class AppLifetime(Action exit)
{
    public void Exit() => exit();
}
