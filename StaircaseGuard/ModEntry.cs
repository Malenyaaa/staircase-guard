using System;
using System.Linq;
using System.Text.RegularExpressions;
using HarmonyLib;
using Microsoft.Xna.Framework;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewModdingAPI.Utilities;
using StardewValley;
using StardewValley.Locations;
using StardewValley.Menus;
using SObject = StardewValley.Object;

namespace StaircaseGuard;

internal sealed class ModEntry : Mod
{
    private const string StaircaseId = "(BC)71";
    private const int HintDuration = 120;
    private const int HintFade = 30;

    private static ModEntry Instance = null!;
    private ModConfig Config = null!;
    private string? HintText;
    private int HintEndTick;

    /// <summary>The mine level the local player already placed a staircase on, until they leave it.</summary>
    private readonly PerScreen<MineShaft?> PlacedOn = new();

    public override void Entry(IModHelper helper)
    {
        Instance = this;
        Config = helper.ReadConfig<ModConfig>();

        var harmony = new Harmony(ModManifest.UniqueID);
        harmony.Patch(
            original: AccessTools.Method(typeof(SObject), nameof(SObject.placementAction)),
            prefix: new HarmonyMethod(typeof(ModEntry), nameof(Before_PlacementAction)),
            postfix: new HarmonyMethod(typeof(ModEntry), nameof(After_PlacementAction))
        );

        helper.Events.GameLoop.GameLaunched += OnGameLaunched;
        helper.Events.Player.Warped += OnWarped;
        helper.Events.Display.RenderedHud += OnRenderedHud;
    }

    private void OnGameLaunched(object? sender, GameLaunchedEventArgs e)
    {
        var gmcm = Helper.ModRegistry.GetApi<IGenericModConfigMenuApi>("spacechase0.GenericModConfigMenu");
        if (gmcm is null)
            return;

        gmcm.Register(ModManifest, reset: () => Config = new ModConfig(), save: () => Helper.WriteConfig(Config));
        gmcm.AddBoolOption(ModManifest, () => Config.Enabled, v => Config.Enabled = v,
            () => Helper.Translation.Get("config.enabled.name"), () => Helper.Translation.Get("config.enabled.tooltip"));
        gmcm.AddKeybindList(ModManifest, () => Config.OverrideKey, v => Config.OverrideKey = v,
            () => Helper.Translation.Get("config.override-key.name"), () => Helper.Translation.Get("config.override-key.tooltip"));
        gmcm.AddBoolOption(ModManifest, () => Config.ShowHint, v => Config.ShowHint = v,
            () => Helper.Translation.Get("config.show-hint.name"), () => Helper.Translation.Get("config.show-hint.tooltip"));
    }

    private void OnWarped(object? sender, WarpedEventArgs e)
    {
        if (e.IsLocalPlayer)
            PlacedOn.Value = null;
    }

    /// <summary>Block a staircase if the player already placed one on this level and the override key isn't held.</summary>
    private static bool Before_PlacementAction(SObject __instance, GameLocation location, Farmer who, ref bool __result)
    {
        try
        {
            var config = Instance.Config;
            if (!config.Enabled || __instance.QualifiedItemId != StaircaseId || location is not MineShaft mine)
                return true;
            if ((who ?? Game1.player) is not { IsLocalPlayer: true })
                return true;
            if (!ReferenceEquals(Instance.PlacedOn.Value, mine) || config.OverrideKey.IsDown())
                return true;

            if (config.ShowHint)
            {
                Instance.HintText = Instance.Helper.Translation.Get("hint.blocked", new { key = DescribeKey(config.OverrideKey) });
                Instance.HintEndTick = Game1.ticks + HintDuration;
            }

            __result = false;
            return false;
        }
        catch (Exception ex)
        {
            Instance.Monitor.Log($"Failed in {nameof(Before_PlacementAction)}:\n{ex}", LogLevel.Error);
            return true;
        }
    }

    /// <summary>Remember the level once a staircase was actually placed there.</summary>
    private static void After_PlacementAction(SObject __instance, GameLocation location, Farmer who, bool __result)
    {
        if (__result && __instance.QualifiedItemId == StaircaseId && location is MineShaft mine && (who ?? Game1.player) is { IsLocalPlayer: true })
            Instance.PlacedOn.Value = mine;
    }

    /// <summary>Draw the hint bottom-left, a bit above where vanilla HUD messages sit so it clears the toolbar.</summary>
    private void OnRenderedHud(object? sender, RenderedHudEventArgs e)
    {
        int left = HintEndTick - Game1.ticks;
        if (HintText is null || left <= 0)
            return;

        float alpha = Math.Min(1f, left / (float)HintFade);
        const int pad = 20;
        Vector2 size = Game1.smallFont.MeasureString(HintText);
        int width = (int)size.X + pad * 2;
        int height = (int)size.Y + pad * 2;
        int x = 16;
        int y = Game1.uiViewport.Height - 136 - height;

        IClickableMenu.drawTextureBox(e.SpriteBatch, Game1.menuTexture, new Rectangle(0, 256, 60, 60), x, y, width, height, Color.White * alpha, drawShadow: false);
        Utility.drawTextWithShadow(e.SpriteBatch, HintText, Game1.smallFont, new Vector2(x + pad, y + pad), Game1.textColor * alpha);
    }

    /// <summary>Turn e.g. "LeftShift, RightShift" into "Shift".</summary>
    private static string DescribeKey(KeybindList keys)
    {
        var names = keys.Keybinds
            .Where(k => k.IsBound)
            .Select(k => string.Join(" + ", k.Buttons.Select(b => Regex.Replace(b.ToString(), "^(Left|Right)(?=[A-Z])", ""))))
            .Distinct()
            .ToArray();
        return names.Length > 0 ? string.Join(" / ", names) : keys.ToString();
    }
}
