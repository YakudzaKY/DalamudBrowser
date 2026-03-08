using Dalamud.Game.Command;
using Dalamud.IoC;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using DalamudBrowser.Rendering;
using DalamudBrowser.Services;
using DalamudBrowser.Windows;
using System;

namespace DalamudBrowser;

public sealed class Plugin : IDalamudPlugin
{
    [PluginService] internal static IDalamudPluginInterface PluginInterface { get; private set; } = null!;
    [PluginService] internal static ICommandManager CommandManager { get; private set; } = null!;
    [PluginService] internal static IPluginLog Log { get; private set; } = null!;

    private const string CommandName = "/dbrowser";

    public Configuration Configuration { get; }
    public BrowserWorkspace Workspace { get; }
    public WindowSystem WindowSystem { get; } = new("DalamudBrowser");

    private readonly ConfigWindow configWindow;
    private readonly MainWindow mainWindow;

    public Plugin()
    {
        Configuration = PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
        Configuration.EnsureInitialized();
        Configuration.Save();

        Workspace = new BrowserWorkspace(
            Configuration,
            Log,
            new SafeBrowserRenderBackend(Log, new RemoteCefRenderBackend(PluginInterface, Log)));

        configWindow = new ConfigWindow(Workspace);
        mainWindow = new MainWindow(Workspace)
        {
            IsOpen = Configuration.OpenManagerOnStartup,
        };

        WindowSystem.AddWindow(configWindow);
        WindowSystem.AddWindow(mainWindow);

        CommandManager.AddHandler(CommandName, new CommandInfo(OnCommand)
        {
            HelpMessage = "Open the Dalamud Browser workspace manager",
        });

        PluginInterface.UiBuilder.Draw += DrawUi;
        PluginInterface.UiBuilder.OpenConfigUi += ToggleConfigUi;
        PluginInterface.UiBuilder.OpenMainUi += ToggleMainUi;

        Log.Information("DalamudBrowser loaded.");
    }

    public void Dispose()
    {
        PluginInterface.UiBuilder.Draw -= DrawUi;
        PluginInterface.UiBuilder.OpenConfigUi -= ToggleConfigUi;
        PluginInterface.UiBuilder.OpenMainUi -= ToggleMainUi;

        CommandManager.RemoveHandler(CommandName);
        WindowSystem.RemoveAllWindows();

        configWindow.Dispose();
        mainWindow.Dispose();
        Workspace.Dispose();
    }

    private void DrawUi()
    {
        try
        {
            Workspace.DrawViews();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Workspace view rendering failed.");
        }
        finally
        {
            WindowSystem.Draw();
        }
    }

    private void OnCommand(string command, string args)
    {
        ToggleMainUi();
    }

    public void ToggleConfigUi() => configWindow.Toggle();
    public void ToggleMainUi() => mainWindow.Toggle();
}
