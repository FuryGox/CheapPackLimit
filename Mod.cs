using HarmonyLib;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace LimitBoostersNS
{
    public class LimitBoosters : Mod
    {
        // Mod Settings
        public ConfigEntry<int> MaxPurchasesConfig = null!;
        public ConfigEntry<bool> PerPackLimitConfig = null!;
        public ConfigEntry<int> PriceIncreaseConfig = null!;
        public ConfigEntry<int> TrackedCheapestCountConfig = null!;
        public ConfigEntry<int> ResetMonthsConfig = null!;
        public ConfigEntry<bool> UsePercentageConfig = null!;
        public Dictionary<string, ConfigEntry<int>> BoardPriceIncreaseConfigs = new Dictionary<string, ConfigEntry<int>>();

        // State tracking
        public static LimitBoosters Instance = null!;
        public static int PurchasesThisMonth = 0;
        public static Dictionary<string, int> PackPurchases = new Dictionary<string, int>();
        public static int LastResetMonth = -1;

        public static void Log(string message)
        {
            Instance?.Logger.Log(message);
        }

        public override void Ready()
        {
            Instance = this;
            Logger.Log("LimitBoosters mod is ready.");

            // Register Settings for the Mod Options Menu
            MaxPurchasesConfig = Config.GetEntry<int>("Max Purchases", 5);
            MaxPurchasesConfig.UI.Tooltip = "Maximum number of booster packs that can be purchased per month. This limit resets at the start of each new month. Set to -1 or 0 to disable this feature.";
            PerPackLimitConfig = Config.GetEntry<bool>("Limit Per Individual Pack", true);
            PerPackLimitConfig.UI.Tooltip = "Whether the purchase limit applies to each individual booster pack. If false, the limit is applied globally across all packs.";
            PriceIncreaseConfig = Config.GetEntry<int>("Price Increase per Buy (Default)", 1);
            PriceIncreaseConfig.UI.Tooltip = "Default amount by which the price of a booster pack increases with each purchase if no board-specific setting applies. Set to -1 or 0 to disable.";
            TrackedCheapestCountConfig = Config.GetEntry<int>("Cheapest Packs Count", 2);
            TrackedCheapestCountConfig.UI.Tooltip = "Number of cheapest booster packs to track for price increases. Set to -1 or 0 to disable this feature.";
            ResetMonthsConfig = Config.GetEntry<int>("Reset Time (Moons)", 1);
            ResetMonthsConfig.UI.Tooltip = "Number of Moons (CurrentMonth) between resets. Set to 1 to reset every moon (default), 2 to reset every 2 moons, etc. Set to -1 to never reset.";

            UsePercentageConfig = Config.GetEntry<bool>("Use Percentage Increase", false);
            UsePercentageConfig.UI.Tooltip = "Whether to use percentage-based price increase instead of a flat value (e.g., 20% on a 5-cost pack increases price by +1 to 6, rounded up).";

            if (WorldManager.instance?.Boards != null)
            {
                List<GameBoard> gameboard = WorldManager.instance.Boards;
                Log("Number of game boards: " + gameboard.Count);
                foreach (var board in gameboard)
                {
                    Log("Game board: " + board.Id);
                    RegisterBoardConfig(board.Id, board.name);
                }
            }

            // Apply Harmony patches
            Harmony.PatchAll();
            Logger.Log("LimitBoosters initialized with Harmony.");
        }

        public void RegisterBoardConfig(string boardId, string boardName = "")
        {
            if (string.IsNullOrEmpty(boardId) || BoardPriceIncreaseConfigs.ContainsKey(boardId)) return;

            var entry = Config.GetEntry<int>($"Price Increase ({boardName})", 1);
            entry.UI.Tooltip = $"Price increase per buy for '{boardId}'{(string.IsNullOrEmpty(boardName) ? "" : $" ({boardName})")}. If 'Use Percentage Increase' is true, this represents the percent (e.g. 20 for 20%). Set to -1 or 0 to disable.";
            BoardPriceIncreaseConfigs[boardId] = entry;
            Log($"Registered price increase config for board: {boardId}{(string.IsNullOrEmpty(boardName) ? "" : $" ({boardName})")}");
        }

        public int GetPriceIncreaseForBoard(string boardId)
        {
            if (!string.IsNullOrEmpty(boardId) && BoardPriceIncreaseConfigs.TryGetValue(boardId, out var boardConfig))
            {
                return boardConfig.Value;
            }
            return PriceIncreaseConfig?.Value ?? 1;
        }

        public static object? CurrentRoundExtraKeyValuesRef = null;

        public void Update()
        {
            if (WorldManager.instance == null) return;

            // Detect new save/game session by tracking changes in RoundExtraKeyValues reference
            var currentKeyValues = WorldManager.instance.RoundExtraKeyValues;
            if (currentKeyValues != null && !ReferenceEquals(currentKeyValues, CurrentRoundExtraKeyValuesRef))
            {
                CurrentRoundExtraKeyValuesRef = currentKeyValues;
                LoadFromExtraKeyValues();
                LastResetMonth = WorldManager.instance.CurrentMonth;
                Log("Save round session changed. Synced counters.");
            }

            int currentMonth = WorldManager.instance.CurrentMonth;

            // Dynamically register any boards discovered during gameplay
            if (WorldManager.instance.Boards != null)
            {
                foreach (var board in WorldManager.instance.Boards)
                {
                    if (!string.IsNullOrEmpty(board.Id) && !BoardPriceIncreaseConfigs.ContainsKey(board.Id))
                    {
                        RegisterBoardConfig(board.Id, board.name);
                    }
                }
            }

            // Initialize on first frame
            if (LastResetMonth == -1)
            {
                LastResetMonth = currentMonth;
                return;
            }

            // Handle new game or earlier save loaded (moon decreased)
            if (currentMonth < LastResetMonth)
            {
                LastResetMonth = currentMonth;
                ResetMonthlyCounters("New game or earlier save loaded");
                return;
            }

            // Check if the configured number of Moons/Months has passed
            int resetInterval = ResetMonthsConfig.Value;
            if (resetInterval > 0 && (currentMonth - LastResetMonth) >= resetInterval)
            {
                LastResetMonth = currentMonth;
                ResetMonthlyCounters($"Reset interval reached ({resetInterval} moon(s) passed)");
            }
        }

        public static void ResetMonthlyCounters(string reason = "Reset")
        {
            PurchasesThisMonth = 0;
            PackPurchases.Clear();
            if (WorldManager.instance != null)
            {
                LastResetMonth = WorldManager.instance.CurrentMonth;
            }
            else
            {
                LastResetMonth = -1;
            }
            Log($"{reason}: Reset cheap booster limits and price increases.");
        }

        #region Save / Load Support
        public static void SetExtraValue(string key, string value)
        {
            if (WorldManager.instance?.RoundExtraKeyValues == null) return;
            var item = WorldManager.instance.RoundExtraKeyValues.FirstOrDefault(kv => kv.Key == key);
            if (item != null)
            {
                item.Value = value;
            }
            else
            {
                WorldManager.instance.RoundExtraKeyValues.Add(new SerializedKeyValuePair { Key = key, Value = value });
            }
        }

        public static string? GetExtraValue(string key)
        {
            if (WorldManager.instance?.RoundExtraKeyValues == null) return null;
            return WorldManager.instance.RoundExtraKeyValues.FirstOrDefault(kv => kv.Key == key)?.Value;
        }

        public static void SaveToExtraKeyValues()
        {
            try
            {
                SetExtraValue("CheapPackLimit_PurchasesThisMonth", PurchasesThisMonth.ToString());
                SetExtraValue("CheapPackLimit_LastResetMonth", LastResetMonth.ToString());
                SetExtraValue("CheapPackLimit_PackPurchases", JsonConvert.SerializeObject(PackPurchases));
                Log("Saved pack limit and price increase data to save file.");
            }
            catch (Exception ex)
            {
                Log($"Error saving pack limit data: {ex.Message}");
            }
        }

        public static void LoadFromExtraKeyValues()
        {
            try
            {
                string? purchasesStr = GetExtraValue("CheapPackLimit_PurchasesThisMonth");
                if (int.TryParse(purchasesStr, out int purchases))
                {
                    PurchasesThisMonth = purchases;
                }
                else
                {
                    PurchasesThisMonth = 0;
                }

                string? lastMonthStr = GetExtraValue("CheapPackLimit_LastResetMonth");
                if (int.TryParse(lastMonthStr, out int lastMonth))
                {
                    LastResetMonth = lastMonth;
                }
                else if (WorldManager.instance != null)
                {
                    LastResetMonth = WorldManager.instance.CurrentMonth;
                }

                string? packPurchasesStr = GetExtraValue("CheapPackLimit_PackPurchases");
                if (!string.IsNullOrEmpty(packPurchasesStr))
                {
                    var dict = JsonConvert.DeserializeObject<Dictionary<string, int>>(packPurchasesStr);
                    PackPurchases = dict ?? new Dictionary<string, int>();
                }
                else
                {
                    PackPurchases.Clear();
                }

                Log($"Loaded pack limit data: {PurchasesThisMonth} total cheap buys, {PackPurchases.Count} pack records.");
            }
            catch (Exception ex)
            {
                Log($"Error loading pack limit data: {ex.Message}");
            }
        }
        #endregion

        /// <summary>
        /// Finds the N cheapest booster pack IDs for the active board using base costs from AllBoosterBoxes.
        /// If TrackedCheapestCountConfig is -1 (or <= 0), the feature is completely disabled.
        /// </summary>
        public static HashSet<string> GetCheapestBoosterIds()
        {
            if (WorldManager.instance?.CurrentBoard?.BoosterIds == null || WorldManager.instance.AllBoosterBoxes == null)
                return new HashSet<string>();

            int count = Instance != null ? Instance.TrackedCheapestCountConfig.Value : 2;
            if (count == -1 || count <= 0)
                return new HashSet<string>(); // -1 disables tracking completely

            return WorldManager.instance.CurrentBoard.BoosterIds
                .Select(id => WorldManager.instance.AllBoosterBoxes.FirstOrDefault(box => box.BoosterId == id))
                .Where(box => box != null)
                .OrderBy(box => box.Cost) // Base cost
                .Take(count)
                .Select(box => box.BoosterId)
                .ToHashSet();
        }

        /// <summary>
        /// Checks whether the purchase limit has been reached for a specific booster pack.
        /// Respects the PerPackLimitConfig setting.
        /// If MaxPurchasesConfig is -1 (or <= 0), the purchase limit is disabled.
        /// </summary>
        public static bool IsPackLimitReached(string boosterId)
        {
            if (Instance == null) return false;

            int limit = Instance.MaxPurchasesConfig.Value;
            if (limit == -1 || limit <= 0) return false;

            var cheapPacks = GetCheapestBoosterIds();
            if (!cheapPacks.Contains(boosterId)) return false;

            if (Instance.PerPackLimitConfig.Value)
            {
                PackPurchases.TryGetValue(boosterId, out int boughtCount);
                return boughtCount >= limit;
            }
            else
            {
                return PurchasesThisMonth >= limit;
            }
        }
    }

    
    #region HARMONY PATCHES

    /// <summary>
    /// Track when a booster pack is purchased.
    /// </summary>
    [HarmonyPatch(typeof(BuyBoosterBox), "CreateBoosterPack")]
    public static class Patch_BuyBoosterBox_CreateBoosterPack
    {
        public static void Postfix(BuyBoosterBox __instance)
        {
            var cheapPacks = LimitBoosters.GetCheapestBoosterIds();
            if (cheapPacks.Contains(__instance.BoosterId))
            {
                LimitBoosters.PurchasesThisMonth++;

                if (!LimitBoosters.PackPurchases.ContainsKey(__instance.BoosterId))
                    LimitBoosters.PackPurchases[__instance.BoosterId] = 0;

                LimitBoosters.PackPurchases[__instance.BoosterId]++;

                LimitBoosters.PackPurchases.TryGetValue(__instance.BoosterId, out int packCount);
                LimitBoosters.Log(
                    $"Bought pack '{__instance.BoosterId}'. Pack buys: {packCount}, Total cheap buys: {LimitBoosters.PurchasesThisMonth}");
            }
        }
    }

    /// <summary>
    /// Block buying once the limit is reached.
    /// </summary>
    [HarmonyPatch(typeof(BuyBoosterBox), nameof(BuyBoosterBox.CanHaveCard))]
    public static class Patch_BuyBoosterBox_CanHaveCard
    {
        public static void Postfix(BuyBoosterBox __instance, ref bool __result)
        {
            if (!__result) return;

            if (LimitBoosters.IsPackLimitReached(__instance.BoosterId))
            {
                __result = false; // Cannot drop gold / cards onto this pack
            }
        }
    }

    /// <summary>
    /// Dynamically increase the cost per purchase.
    /// Also updates the cost text in BuyBoosterBox.Update().
    /// </summary>
    [HarmonyPatch(typeof(BuyBoosterBox), nameof(BuyBoosterBox.GetCost))]
    public static class Patch_BuyBoosterBox_GetCost
    {
        public static void Postfix(BuyBoosterBox __instance, ref int __result)
        {
            if (LimitBoosters.Instance == null) return;

            var cheapPacks = LimitBoosters.GetCheapestBoosterIds();
            if (!cheapPacks.Contains(__instance.BoosterId)) return;

            if (!LimitBoosters.PackPurchases.TryGetValue(__instance.BoosterId, out int boughtCount) || boughtCount <= 0)
                return;

            string boardId = __instance.MyBoard?.Id ?? WorldManager.instance?.CurrentBoard?.Id ?? "";
            int increaseSetting = LimitBoosters.Instance.GetPriceIncreaseForBoard(boardId);

            if (increaseSetting == -1 || increaseSetting <= 0) return;

            if (LimitBoosters.Instance.UsePercentageConfig != null && LimitBoosters.Instance.UsePercentageConfig.Value)
            {
                // Percentage increase rounded up (e.g., baseCost = 5, 20% -> +1 per buy => 6)
                int baseCost = __instance.Cost;
                int increasePerBuy = Mathf.Max(1, Mathf.CeilToInt(baseCost * (increaseSetting / 100f)));
                __result += boughtCount * increasePerBuy;
            }
            else
            {
                // Flat value increase
                __result += boughtCount * increaseSetting;
            }
        }
    }

    /// <summary>
    /// Display "MAX" on the booster box if the purchase limit is reached.
    /// </summary>
    [HarmonyPatch(typeof(BuyBoosterBox), "Update")]
    public static class Patch_BuyBoosterBox_Update
    {
        public static void Postfix(BuyBoosterBox __instance)
        {
            if (LimitBoosters.IsPackLimitReached(__instance.BoosterId))
            {
                int resetMoons = LimitBoosters.Instance != null ? LimitBoosters.Instance.ResetMonthsConfig.Value : 1;
                if (resetMoons > 1 && WorldManager.instance != null && LimitBoosters.LastResetMonth != -1)
                {
                    int passed = WorldManager.instance.CurrentMonth - LimitBoosters.LastResetMonth;
                    int remaining = Mathf.Max(1, resetMoons - passed);
                    __instance.BuyText.text = $"<color=red>MAX ({remaining}m)</color>";
                }
                else
                {
                    __instance.BuyText.text = "<color=red>MAX</color>";
                }
            }
        }
    }

    /// <summary>
    /// Save current pack purchase limits and price increases to the save file.
    /// </summary>
    [HarmonyPatch(typeof(WorldManager), nameof(WorldManager.GetSaveRound))]
    public static class Patch_WorldManager_GetSaveRound
    {
        public static void Prefix()
        {
            LimitBoosters.SaveToExtraKeyValues();
        }
    }

    /// <summary>
    /// Load pack purchase limits and price increases from the save file.
    /// </summary>
    [HarmonyPatch(typeof(WorldManager), nameof(WorldManager.LoadSaveRound))]
    public static class Patch_WorldManager_LoadSaveRound
    {
        public static void Postfix()
        {
            LimitBoosters.LoadFromExtraKeyValues();
        }
    }
    #endregion
}