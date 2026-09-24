using Brio.Services;
using Brio.UI;
using Dalamud.Game.Command;
using Dalamud.Plugin.Services;
using System;

namespace Brio.Game.Chat;

public class CommandHandlerService : IDisposable
{
    private const string BriosisCommandName = "/briosis";

    private readonly ICommandManager _commandManager;
    private readonly IChatGui _chatGui;
    private readonly UIManager _uiManager;
    private readonly Mediator _mediator;

    public CommandHandlerService(ICommandManager commandManager, IChatGui chatGui, UIManager uiManager, Mediator mediator)
    {
        _commandManager = commandManager;
        _chatGui = chatGui;
        _uiManager = uiManager;
        _mediator = mediator;

        _commandManager.AddHandler(BriosisCommandName, new CommandInfo(OnCommand)
        {
            HelpMessage = "Toggles the Briosis window.",
            ShowInHelp = true,
        });
    }

    private void OnCommand(string command, string arguments)
    {
        if(arguments.Length == 0)
            arguments = "window";

        var argumentList = arguments.Split(' ', 2);

        switch(argumentList[0].ToLowerInvariant())
        {
            case "window":
                _uiManager.ToggleMainWindow();
                break;

            case "timeline":
                _uiManager.ToggleTimelineWindow();
                break;

            case "settings":
                _uiManager.ToggleSettingsWindow();
                break;

            case "about":
                _uiManager.ToggleWelcomeWindow();
                break;

            case "mcdf":
                _uiManager.ToggleMCDFWindow();
                break;

            case "mediator":
                _mediator.PrintSubscriberInfo();
                break;

            case "help":
            default:
                PrintHelp();
                break;
        }

    }

    private void PrintHelp()
    {
        _chatGui.Print("Valid Briosis commands are:");
        _chatGui.Print("<none> - Toggle main Briosis window");
        _chatGui.Print("window - Toggle main Briosis window");
        _chatGui.Print("settings - Toggle Briosis settings window");
        _chatGui.Print("about - Toggle Briosis info window");
        _chatGui.Print("mcdf - Toggle Briosis MCDF window");
        _chatGui.Print("help - Print this help prompt");
    }

    public void Dispose()
    {
        _commandManager.RemoveHandler(BriosisCommandName);
    }
}
