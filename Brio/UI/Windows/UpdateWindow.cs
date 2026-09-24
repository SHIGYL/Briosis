using Brio.Config;
using Brio.Files;
using Brio.Resources;
using Brio.UI.Controls.Stateless;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;
using Dalamud.Utility;
using SharpYaml;
using System.Diagnostics;
using System.IO;
using System.Numerics;
using System.Text;
using System.Text.Json;

namespace Brio.UI.Windows;

public class UpdateWindow : Window
{
    //
    // Some code found here is inspired by CharacterSelect+

    private static float CloseButtonWidth => 310f * ImGuiHelpers.GlobalScale;

    private bool _scrollToTop = false;
    private readonly ChangelogFile _changelogFile;

    public UpdateWindow() : base($"   {Brio.Name} CHANGELOG [{ConfigurationService.Instance.Version}]###brio_welcomewindow", ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoDocking | ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoDecoration)
    {
        Namespace = "brio_welcomewindow_namespace";

        Size = new Vector2(710, 745);

        ShowCloseButton = false;
        AllowClickthrough = false;
        AllowPinning = false;
        AllowBackgroundBlur = false;

        _changelogFile = LoadChangelog();
    }

    private static ChangelogFile LoadChangelog()
    {
        using var changelogStream = ResourceProvider.Instance.GetRawResourceStream("Changelog.changelog.yaml");
        using var streamReader = new StreamReader(changelogStream, Encoding.UTF8, true, 128);

        var yamlOptions = new YamlSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = YamlIgnoreCondition.WhenReading,
        };

        var yaml = streamReader.ReadToEnd();
        return YamlSerializer.Deserialize<ChangelogFile>(yaml, yamlOptions)
            ?? throw new InvalidDataException("Unable to deserialize the Briosis changelog.");
    }

    public override void OnOpen()
    {
        _scrollToTop = true;
    }
    public override void PreDraw()
    {
        ImGui.SetNextWindowPos(new Vector2((ImGui.GetIO().DisplaySize.X - Size!.Value.X) / 2, (ImGui.GetIO().DisplaySize.Y - Size!.Value.Y) / 2), ImGuiCond.Appearing);

        base.PreDraw();
    }

    //

    public override void Draw()
    {
        ImBrio.BlurWindow();

        var windowPos = ImGui.GetWindowPos();
        var windowPadding = ImGui.GetStyle().WindowPadding;

        var headerWidth = ImGui.GetWindowSize().X - (windowPadding.X * 2);
        var headerHeight = 76f * ImGuiHelpers.GlobalScale;
        var headerStart = windowPos + windowPadding;
        var headerEnd = headerStart + new Vector2(headerWidth, headerHeight);

        // Background
        DrawBackground(headerStart, headerEnd);

        // Cursor line up
        ImGui.SetCursorScreenPos(headerStart + new Vector2(10f, 10f) * ImGuiHelpers.GlobalScale);

        // Tagline Text
        ImGui.TextColored(new Vector4(0.4f, 0.9f, 0.4f, 1.0f), _changelogFile.Tagline);
        ImGui.SameLine();
        ImGui.TextColored(new Vector4(0.75f, 0.75f, 0.85f, 1.0f), $"  -  {_changelogFile.Subline}");
        ImBrio.VerticalPadding(5);

        // Buttons
        var segmentSize = ImGui.GetWindowSize().X / 4.15f;
        var buttonSize = new Vector2(segmentSize, ImGui.GetTextLineHeight() * 1.7f);

        ImGui.SetCursorPosY(ImGui.GetCursorPosY() + 5);

        using(ImRaii.PushColor(ImGuiCol.Button, new Vector4(0, 224, 148, 200) / 255))
            if(ImGui.Button("Briosis GitHub", buttonSize))
                Process.Start(new ProcessStartInfo { FileName = "https://github.com/SHIGYL/Briosis", UseShellExecute = true });
        ImGui.SameLine();

        using(ImRaii.PushColor(ImGuiCol.Button, new Vector4(65, 90, 240, 200) / 255))
            if(ImGui.Button("Upstream Brio", buttonSize))
                Process.Start(new ProcessStartInfo { FileName = "https://github.com/Etheirys/Brio", UseShellExecute = true });
        ImGui.SameLine();

        using(ImRaii.PushColor(ImGuiCol.Button, new Vector4(96, 108, 246, 200) / 255))
            if(ImGui.Button("Ktisis", buttonSize))
                Process.Start(new ProcessStartInfo { FileName = "https://github.com/ktisis-tools/Ktisis", UseShellExecute = true });
        ImGui.SameLine();

        using(ImRaii.PushColor(ImGuiCol.Button, new Vector4(29, 161, 242, 200) / 255))
            if(ImGui.Button("Credits", buttonSize))
                Process.Start(new ProcessStartInfo { FileName = "https://github.com/SHIGYL/Briosis/blob/main/Acknowledgements.md", UseShellExecute = true });

        ImBrio.VerticalPadding(10);

        using(ImRaii.PushColor(ImGuiCol.ChildBg, 0))
        using(var c = ImRaii.Child("###brio_changelog", new Vector2(ImGui.GetWindowHeight() - 55 * ImGuiHelpers.GlobalScale, ImBrio.GetRemainingHeight() - 44), false,
            Flags = ImGuiWindowFlags.NoSavedSettings | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoCollapse))
            if(c.Success)
            {
                if(_scrollToTop)
                {
                    _scrollToTop = false;
                    ImGui.SetScrollHereY(0);
                }

                foreach(var entry in _changelogFile.Changelog)
                    DrawChangelogTemplate(entry);

                ImBrio.VerticalPadding(15);
            }

        ImGui.SetCursorPosX((ImGui.GetWindowSize().Y - CloseButtonWidth) / 2);
        if(ImBrio.HoldButton("updateWindowClose", "Close", FontAwesomeIcon.SquareXmark, 0.7f, new Vector2(CloseButtonWidth, 0), centerTest: true, tooltip: "[HOLD TO CLOSE]\nTo open this window again click the `Information` button in Briosis."))
        {
            IsOpen = false;
        }
    }

    private void DrawChangelogTemplate(ChangelogEntry entry)
    {
        var currentColor = entry.IsCurrent == true ? new Vector4(0.5f, 0.9f, 0.5f, 1.0f) : new Vector4(0.75f, 0.75f, 0.85f, 1.0f);
        bool isCurrent = entry.IsCurrent ?? false;

        // Dev Message
        if(entry.Message.IsNullOrEmpty() is false)
        {
            if(CollapsingHeader($" {entry.Name} — {entry.Date} ", $" {entry.Tagline} ", currentColor, isCurrent))
            {
                ImBrio.VerticalPadding(10);

                ImGui.Text(entry.Message);

                ImBrio.VerticalPadding(10);
            }
            return;
        }

        if(CollapsingHeader($" {entry.Name} — {entry.Date} ", $"  —  {entry.Tagline} ", currentColor, isCurrent))
        {
            ImBrio.VerticalPadding(10);

            foreach(var item in entry.Versions)
            {
                DrawFeature(FontAwesomeIcon.None, item.Number, new Vector4(0.5f, 0.9f, 0.5f, 1.0f));

                foreach(var subItem in item.Items)
                {
                    ImGui.BulletText(subItem);
                }
            }

            ImBrio.VerticalPadding(10);
        }
    }

    //
    // some code found here is modified and from CharacterSelect+
    // https://github.com/IcarusXIV/Character-Select- (link includes the -)
    //

    private static void DrawBackground(Vector2 headerStart, Vector2 headerEnd)
    {
        var drawList = ImGui.GetWindowDrawList();
        uint gradientTop = ImGui.GetColorU32(new Vector4(0.2f, 0.4f, 0.8f, 0.15f));
        uint gradientBottom = ImGui.GetColorU32(new Vector4(0.1f, 0.1f, 0.2f, 0.05f));
        drawList.AddRectFilledMultiColor(headerStart, headerEnd, gradientTop, gradientTop, gradientBottom, gradientBottom);
    }

    private static bool CollapsingHeader(string title, string subTitle, Vector4 titleColor, bool defaultOpen)
    {
        var flags = defaultOpen ? ImGuiTreeNodeFlags.DefaultOpen : ImGuiTreeNodeFlags.None;

        bool isOpen = false;
        using(ImRaii.PushColor(ImGuiCol.Text, titleColor))
        {
            isOpen = ImGui.CollapsingHeader(title, flags);
        }

        ImGui.SameLine();

        ImGui.TextColored(new Vector4(0.75f, 0.75f, 0.85f, 1.0f), subTitle);

        return isOpen;
    }

    private static void DrawFeature(FontAwesomeIcon icon, string title, Vector4 accentColor)
    {
        ImGui.Spacing();

        ImBrio.Icon(icon);
        ImGui.SameLine();
        ImGui.TextColored(accentColor, title);

        ImGui.Spacing();
    }
}
