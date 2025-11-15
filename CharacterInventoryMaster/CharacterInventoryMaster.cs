using HarmonyLib;
using System;
using System.Linq;
using CharacterInventoryMaster.Config;
using Vintagestory.API.Common;
using ConfigLib;
using Vintagestory.API.Config;
using Newtonsoft.Json.Linq;
using Vintagestory.API.Datastructures;

namespace CharacterInventoryMaster;

public class CharacterInventoryMaster : ModSystem {
    public static ILogger Logger { get; private set; }
    public static ICoreAPI Api { get; private set; }
    public static Harmony harmony { get; private set; }
    public static ModConfig config => ModConfig.Instance;

    public override void Start(ICoreAPI api) {
        base.Start(api);
        harmony = new Harmony(Mod.Info.ModID);

        Logger = Mod.Logger;
        Api = api;

        try {
            ModConfig.Instance = api.LoadModConfig<ModConfig>(ModConfig.ConfigName) ?? new ModConfig();
            api.StoreModConfig(ModConfig.Instance, ModConfig.ConfigName);
        } catch (Exception) { ModConfig.Instance = new ModConfig(); }

        if (api.ModLoader.IsModEnabled("configlib")) {
            SubscribeToConfigChange(api);
        }
    }

    public override void Dispose() {
        Logger = null;
        Api = null;
        harmony?.UnpatchAll(Mod.Info.ModID);
        harmony = null;
        base.Dispose();
    }

    private void SubscribeToConfigChange(ICoreAPI api) {
        ConfigLibModSystem system = api.ModLoader.GetModSystem<ConfigLibModSystem>();

        system.SettingChanged += (domain, Iconfig, setting) => {
            if (domain != "characterinventorymaster")
                return;

            setting.AssignSettingValue(ModConfig.Instance);
            harmony.UnpatchAll(Mod.Info.ModID);
            harmony.PatchAll(); // Literally everything changes this, so might as well
        };
    }

    public static string LGet(string key) {
        key = "characterinventorymaster:" + key;
        return Lang.Get(key);
    }
}
