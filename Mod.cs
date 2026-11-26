using System;
using System.Collections.Concurrent;
using System.Net;
using System.Threading;
using MelonLoader;
using UnityEngine;
using Il2CppScheduleOne.Money;
using Il2CppScheduleOne.PlayerScripts;
using Il2CppScheduleOne.ItemFramework;
using Il2CppScheduleOne.Product;
using Il2CppScheduleOne.Property;
using Il2CppScheduleOne.Storage;
using Il2CppScheduleOne.ObjectScripts;

[assembly: MelonInfo(typeof(ChatFx.Core), "Chat FX", "1.8.0", "purplehaxttv")]
[assembly: MelonGame("TVGS", "Schedule I")]

namespace ChatFx
{
    public class Core : MelonMod
    {
        private static MelonPreferences_Category cfg;
        private static MelonPreferences_Entry<int> portEntry;

        public override void OnInitializeMelon()
        {
            cfg = MelonPreferences.CreateCategory("ChatFx");
            portEntry = cfg.CreateEntry("Port", 7900,
                description: "Local HTTP port Firebot hits to trigger in-game effects.");
            FxServer.Start(portEntry.Value, LoggerInstance);
        }

        public override void OnUpdate() => FxServer.Pump();

        public override void OnDeinitializeMelon() => FxServer.Stop();
    }

    // Tiny local HTTP server so Firebot (or anything) can trigger in-game effects.
    // Firebot fires an HTTP request -> we queue the effect -> OnUpdate runs it on
    // the Unity main thread (IL2CPP calls must never run on the listener thread).
    public static class FxServer
    {
        private static HttpListener listener;
        private static Thread thread;
        private static volatile bool running;
        private static MelonLogger.Instance log;

        private static readonly ConcurrentQueue<Action> queue = new ConcurrentQueue<Action>();

        public static void Start(int port, MelonLogger.Instance logger)
        {
            log = logger;
            try
            {
                listener = new HttpListener();
                listener.Prefixes.Add($"http://localhost:{port}/");
                listener.Start();
                running = true;
                thread = new Thread(Loop) { IsBackground = true, Name = "ChatFx-Listener" };
                thread.Start();
                log.Msg($"Chat FX listening on http://localhost:{port}/fx");
            }
            catch (Exception e)
            {
                log.Error($"Chat FX failed to start on port {port}: {e.Message}");
            }
        }

        public static void Stop()
        {
            running = false;
            try { listener?.Stop(); } catch { }
            try { listener?.Close(); } catch { }
        }

        public static void Pump()
        {
            while (queue.TryDequeue(out Action a))
            {
                try { a(); }
                catch (Exception e) { log.Warning($"Chat FX effect error: {e.Message}"); }
            }
        }

        private static void Loop()
        {
            while (running)
            {
                HttpListenerContext ctx;
                try { ctx = listener.GetContext(); }
                catch { break; }

                string reply;
                try
                {
                    string type = (ctx.Request.QueryString["type"] ?? "").ToLowerInvariant();
                    string amountStr = ctx.Request.QueryString["amount"];
                    string levelStr = (ctx.Request.QueryString["level"] ?? "").ToLowerInvariant();
                    string propStr = (ctx.Request.QueryString["property"] ?? "").ToLowerInvariant();
                    string targetStr = (ctx.Request.QueryString["target"] ?? "").ToLowerInvariant();
                    reply = Dispatch(type, amountStr, levelStr, propStr, targetStr);
                }
                catch (Exception e)
                {
                    reply = "error: " + e.Message;
                }

                try
                {
                    byte[] buf = System.Text.Encoding.UTF8.GetBytes(reply);
                    ctx.Response.StatusCode = reply.StartsWith("error") ? 400 : 200;
                    ctx.Response.ContentType = "text/plain";
                    ctx.Response.OutputStream.Write(buf, 0, buf.Length);
                    ctx.Response.OutputStream.Close();
                }
                catch { }
            }
        }

        // Listener thread: validate + queue only. No game calls here.
        private static string Dispatch(string type, string amountStr, string levelStr, string propStr, string targetStr)
        {
            switch (type)
            {
                case "help":
                case "bank":
                case "deposit":
                {
                    float amount = 100f;
                    if (!string.IsNullOrEmpty(amountStr) && float.TryParse(amountStr, out float a)) amount = a;
                    queue.Enqueue(() => FxAddBank(amount));
                    return $"ok: +${amount} bank queued";
                }
                case "cashgift":
                case "tip":
                {
                    float amount = 100f;
                    if (!string.IsNullOrEmpty(amountStr) && float.TryParse(amountStr, out float a)) amount = a;
                    queue.Enqueue(() => FxGiveCash(amount));
                    return $"ok: +${amount} cash queued";
                }
                case "pardon":
                case "clearwanted":
                {
                    string t = targetStr;
                    queue.Enqueue(() => FxClearWanted(t));
                    return $"ok: pardon on '{(string.IsNullOrEmpty(t) ? "self" : t)}' queued";
                }
                case "gift":
                case "resupply":
                {
                    int count = 10;
                    if (!string.IsNullOrEmpty(amountStr) && int.TryParse(amountStr, out int c)) count = c;
                    queue.Enqueue(() => FxGiveDrugs(count));
                    return $"ok: gift {count} product queued";
                }
                case "restock":
                case "supplydrop":
                {
                    if (string.IsNullOrEmpty(propStr)) return "error: restock needs &property=<name>";
                    int count = 20;
                    if (!string.IsNullOrEmpty(amountStr) && int.TryParse(amountStr, out int c)) count = c;
                    string p = propStr;
                    queue.Enqueue(() => FxRestockProperty(p, count));
                    return $"ok: restock '{p}' with {count} product queued";
                }
                case "arrest":
                case "hurt":
                {
                    string t = targetStr;
                    queue.Enqueue(() => FxPursuit(levelStr, t));
                    return $"ok: pursuit '{(string.IsNullOrEmpty(levelStr) ? "arresting" : levelStr)}' on '{(string.IsNullOrEmpty(t) ? "self" : t)}' queued";
                }
                case "pickpocket":
                case "steal":
                {
                    int cap = 100;
                    if (!string.IsNullOrEmpty(amountStr) && int.TryParse(amountStr, out int c)) cap = c;
                    queue.Enqueue(() => FxStealCash(cap));
                    return $"ok: pickpocket up to ${cap} queued";
                }
                case "hack":
                case "hacker":
                {
                    int cap = 500;
                    if (!string.IsNullOrEmpty(amountStr) && int.TryParse(amountStr, out int c)) cap = c;
                    queue.Enqueue(() => FxDrainBank(cap));
                    return $"ok: hack up to ${cap} queued";
                }
                case "rivalgang":
                case "gang":
                {
                    int count = 10;
                    if (!string.IsNullOrEmpty(amountStr) && int.TryParse(amountStr, out int c)) count = c;
                    queue.Enqueue(() => FxRemoveDrugs(count));
                    return $"ok: rival gang steals up to {count} product queued";
                }
                case "raid":
                case "gangraid":
                {
                    if (string.IsNullOrEmpty(propStr)) return "error: raid needs &property=<name>";
                    int count = 20;
                    if (!string.IsNullOrEmpty(amountStr) && int.TryParse(amountStr, out int c)) count = c;
                    string p = propStr;
                    queue.Enqueue(() => FxRaidProperty(p, count));
                    return $"ok: raid '{p}' for up to {count} product queued";
                }
                default:
                    return "error: unknown fx type '" + type + "'";
            }
        }

        // ---- effects (main thread only) ----
        private static void FxAddBank(float amount)
        {
            var mm = UnityEngine.Object.FindObjectOfType<MoneyManager>();
            if (mm == null)
            {
                log.Warning("FxAddBank: no MoneyManager (not in a game?)");
                return;
            }
            mm.CreateOnlineTransaction("Chat Gift", amount, 1f, "Someone helped you out!");
            log.Msg($"Chat FX: added ${amount} to bank balance.");
        }

        // Resolve which players an effect targets.
        // ""/"self"/"me" = local player; "all"/"everyone" = all; "others" = everyone but you;
        // otherwise match any player whose name contains the string.
        private static System.Collections.Generic.List<Player> GetTargets(string target)
        {
            var result = new System.Collections.Generic.List<Player>();
            var list = Player.PlayerList;
            if (list == null) return result;
            for (int i = 0; i < list.Count; i++)
            {
                var p = list[i];
                if (p == null) continue;
                bool include;
                if (string.IsNullOrEmpty(target) || target == "self" || target == "me") include = p.IsLocalPlayer;
                else if (target == "all" || target == "everyone") include = true;
                else if (target == "others") include = !p.IsLocalPlayer;
                else include = (p.PlayerName ?? "").ToLowerInvariant().Contains(target);
                if (include) result.Add(p);
            }
            return result;
        }

        private static void FxPursuit(string levelStr, string target)
        {
            var targets = GetTargets(target);
            if (targets.Count == 0) { log.Warning($"FxPursuit: no players matched target '{target}'"); return; }

            PlayerCrimeData.EPursuitLevel level;
            switch (levelStr)
            {
                case "investigating": level = PlayerCrimeData.EPursuitLevel.Investigating; break;
                case "nonlethal":     level = PlayerCrimeData.EPursuitLevel.NonLethal;     break;
                case "lethal":        level = PlayerCrimeData.EPursuitLevel.Lethal;        break;
                default:              level = PlayerCrimeData.EPursuitLevel.Arresting;      break;
            }

            foreach (var p in targets)
            {
                if (p.CrimeData == null) continue;
                p.CrimeData.SetPursuitLevel(level);
                log.Msg($"Chat FX: set pursuit {level} on {p.PlayerName}.");
            }
        }

        private static readonly System.Random rng = new System.Random();

        private static void FxStealCash(int cap)
        {
            var mm = UnityEngine.Object.FindObjectOfType<MoneyManager>();
            if (mm == null) { log.Warning("FxStealCash: no MoneyManager"); return; }
            float cash = mm.cashBalance;
            if (cash <= 0f) { log.Msg("Chat FX: pickpocket found no cash on hand."); return; }
            float take = Math.Min(rng.Next(1, cap + 1), cash);
            mm.ChangeCashBalance(-take, true, false);
            log.Msg($"Chat FX: pickpocketed ${take} cash.");
        }

        private static void FxDrainBank(int cap)
        {
            var mm = UnityEngine.Object.FindObjectOfType<MoneyManager>();
            if (mm == null) { log.Warning("FxDrainBank: no MoneyManager"); return; }
            float bal = mm.onlineBalance;
            if (bal <= 0f) { log.Msg("Chat FX: hacker found an empty account."); return; }
            float take = Math.Min(rng.Next(1, cap + 1), bal);
            mm.CreateOnlineTransaction("Hacked!", -take, 1f, "Someone drained your account!");
            log.Msg($"Chat FX: hacker drained ${take} from the bank.");
        }

        private static void FxRemoveDrugs(int count)
        {
            var inv = UnityEngine.Object.FindObjectOfType<PlayerInventory>();
            if (inv == null) { log.Warning("FxRemoveDrugs: no PlayerInventory"); return; }
            var slots = inv.GetAllInventorySlots();
            if (slots == null) return;

            // Gather every drug stack, then rob one at random.
            var drugSlots = new System.Collections.Generic.List<ItemSlot>();
            for (int i = 0; i < slots.Count; i++)
            {
                var slot = slots[i];
                if (slot == null) continue;
                var item = slot.ItemInstance;
                if (item == null) continue;
                if (item.TryCast<ProductItemInstance>() == null) continue; // drugs only
                if (slot.Quantity <= 0) continue;
                drugSlots.Add(slot);
            }

            if (drugSlots.Count == 0)
            {
                log.Msg("Chat FX: rival gang found no product to steal.");
                return;
            }

            var chosen = drugSlots[rng.Next(drugSlots.Count)];
            int take = Math.Min(count, chosen.Quantity);
            chosen.ChangeQuantity(-take);
            log.Msg($"Chat FX: rival gang stole {take} units of product.");
        }

        private static void FxRaidProperty(string propName, int count)
        {
            var props = Property.OwnedProperties;
            if (props == null) { log.Warning("FxRaidProperty: no owned properties"); return; }

            Property target = null;
            for (int i = 0; i < props.Count; i++)
            {
                var p = props[i];
                if (p == null) continue;
                string name = (p.PropertyName ?? "").ToLowerInvariant();
                string code = (p.PropertyCode ?? "").ToLowerInvariant();
                if (name.Contains(propName) || code.Contains(propName)) { target = p; break; }
            }
            if (target == null) { log.Warning($"FxRaidProperty: no owned property matching '{propName}'"); return; }

            // Gather all product stacks across the property's storage racks.
            var drugSlots = new System.Collections.Generic.List<ItemSlot>();
            var builds = target.BuildableItems;
            if (builds != null)
            {
                for (int i = 0; i < builds.Count; i++)
                {
                    var b = builds[i];
                    if (b == null) continue;
                    var rack = b.TryCast<PlaceableStorageEntity>();
                    if (rack == null) continue;
                    var se = rack.StorageEntity;
                    if (se == null) continue;
                    var slots = se.ItemSlots;
                    if (slots == null) continue;
                    for (int j = 0; j < slots.Count; j++)
                    {
                        var slot = slots[j];
                        if (slot == null) continue;
                        var item = slot.ItemInstance;
                        if (item == null) continue;
                        if (item.TryCast<ProductItemInstance>() == null) continue;
                        if (slot.Quantity <= 0) continue;
                        drugSlots.Add(slot);
                    }
                }
            }

            if (drugSlots.Count == 0)
            {
                log.Msg($"Chat FX: raid on '{target.PropertyName}' found no product.");
                return;
            }

            // Spread the theft across random stacks until we've taken 'count' total.
            int remaining = count, removed = 0;
            while (remaining > 0 && drugSlots.Count > 0)
            {
                int idx = rng.Next(drugSlots.Count);
                var slot = drugSlots[idx];
                int take = Math.Min(remaining, slot.Quantity);
                slot.ChangeQuantity(-take);
                remaining -= take;
                removed += take;
                drugSlots.RemoveAt(idx);
            }
            log.Msg($"Chat FX: rival gang raided '{target.PropertyName}' and stole {removed} product.");
        }

        // ---- helps (polar opposites) ----

        private static void FxGiveCash(float amount)
        {
            var mm = UnityEngine.Object.FindObjectOfType<MoneyManager>();
            if (mm == null) { log.Warning("FxGiveCash: no MoneyManager"); return; }
            mm.ChangeCashBalance(amount, true, true);
            log.Msg($"Chat FX: gifted ${amount} cash.");
        }

        private static void FxClearWanted(string target)
        {
            var targets = GetTargets(target);
            if (targets.Count == 0) { log.Warning($"FxClearWanted: no players matched target '{target}'"); return; }
            foreach (var p in targets)
            {
                if (p.CrimeData == null) continue;
                p.CrimeData.SetPursuitLevel(PlayerCrimeData.EPursuitLevel.None);
                p.CrimeData.ClearCrimes();
                log.Msg($"Chat FX: cleared wanted on {p.PlayerName}.");
            }
        }

        private static void FxGiveDrugs(int count)
        {
            var inv = UnityEngine.Object.FindObjectOfType<PlayerInventory>();
            if (inv == null) { log.Warning("FxGiveDrugs: no PlayerInventory"); return; }
            var slots = inv.GetAllInventorySlots();
            if (slots == null) return;

            // Collect product types held, clone a random one with the full count.
            var drugs = new System.Collections.Generic.List<ProductItemInstance>();
            for (int i = 0; i < slots.Count; i++)
            {
                var slot = slots[i];
                if (slot == null) continue;
                var item = slot.ItemInstance;
                if (item == null) continue;
                var prod = item.TryCast<ProductItemInstance>();
                if (prod == null) continue;
                if (slot.Quantity <= 0) continue;
                drugs.Add(prod);
            }

            if (drugs.Count == 0) { log.Msg("Chat FX: gift found no product to copy (hold at least one)."); return; }

            var chosen = drugs[rng.Next(drugs.Count)];
            var copy = chosen.GetCopy(count); // quantity = count, spills past the stack cap
            inv.AddItemToInventory(copy);
            string nm = "?"; try { nm = chosen.Definition.name; } catch { }
            log.Msg($"Chat FX: gifted {count}x '{nm}' to the player.");
        }

        private static void FxRestockProperty(string propName, int count)
        {
            var props = Property.OwnedProperties;
            if (props == null) { log.Warning("FxRestockProperty: no owned properties"); return; }

            Property target = null;
            for (int i = 0; i < props.Count; i++)
            {
                var p = props[i];
                if (p == null) continue;
                string name = (p.PropertyName ?? "").ToLowerInvariant();
                string code = (p.PropertyCode ?? "").ToLowerInvariant();
                if (name.Contains(propName) || code.Contains(propName)) { target = p; break; }
            }
            if (target == null) { log.Warning($"FxRestockProperty: no owned property matching '{propName}'"); return; }

            // Collect (storage, product) pairs so we can clone a drug and insert it back.
            var pairs = new System.Collections.Generic.List<System.ValueTuple<StorageEntity, ProductItemInstance>>();
            var builds = target.BuildableItems;
            if (builds != null)
            {
                for (int i = 0; i < builds.Count; i++)
                {
                    var b = builds[i];
                    if (b == null) continue;
                    var rack = b.TryCast<PlaceableStorageEntity>();
                    if (rack == null) continue;
                    var se = rack.StorageEntity;
                    if (se == null) continue;
                    var sslots = se.ItemSlots;
                    if (sslots == null) continue;
                    for (int j = 0; j < sslots.Count; j++)
                    {
                        var slot = sslots[j];
                        if (slot == null) continue;
                        var item = slot.ItemInstance;
                        if (item == null) continue;
                        var prod = item.TryCast<ProductItemInstance>();
                        if (prod == null) continue;
                        if (slot.Quantity <= 0) continue;
                        pairs.Add(new System.ValueTuple<StorageEntity, ProductItemInstance>(se, prod));
                    }
                }
            }

            if (pairs.Count == 0) { log.Msg($"Chat FX: restock on '{target.PropertyName}' found no product to copy."); return; }

            var pick = pairs[rng.Next(pairs.Count)];
            int placed = StoreInto(pick.Item1, pick.Item2, count);
            string nm = "?"; try { nm = pick.Item2.Definition.name; } catch { }
            log.Msg($"Chat FX: restocked '{target.PropertyName}' with {placed}x '{nm}'" + (placed < count ? $" (asked for {count}, the storage is full)." : "."));
        }

        // Add up to `quantity` of `item` to a storage unit: top up matching stacks, then fill empty slots.
        // The game's StorageEntity.InsertItem is not used here: in 0.4.7 it removes an existing partial
        // stack of the same item instead of adding to it.
        private static int StoreInto(StorageEntity storage, ItemInstance item, int quantity)
        {
            int remaining = quantity;
            var slots = storage.ItemSlots;
            if (slots == null) return 0;
            for (int pass = 0; pass < 2 && remaining > 0; pass++)
            {
                for (int j = 0; j < slots.Count && remaining > 0; j++)
                {
                    var slot = slots[j];
                    if (slot == null) continue;
                    var held = slot.ItemInstance;
                    int have = held != null ? slot.Quantity : 0;
                    bool empty = held == null || have <= 0;
                    if (empty != (pass == 1)) continue;
                    if (!empty && !held.CanStackWith(item, false)) continue;
                    int room;
                    try { room = slot.GetCapacityForItem(item, true); } catch { room = 0; }
                    if (room <= 0) continue;
                    int n = Math.Min(room, remaining);
                    if (empty) slot.SetStoredItem(item.GetCopy(n), false);
                    else slot.ChangeQuantity(n, false);
                    int now = slot.ItemInstance != null ? slot.Quantity : 0;
                    remaining -= Math.Max(0, Math.Min(n, now - have));
                }
            }
            return quantity - remaining;
        }
    }
}
