﻿using System;
using System.Collections.Generic;
using UnityEngine;
using Oxide.Core;
using System.Text;
using System.Linq;

namespace Oxide.Plugins
{
nutes;
        float refundRatio;
    string box;
    bool npconly;
    List<object> npcids;
    float radiationMax;

#endregion

    #region State

    private Dictionary<string, DateTime> recycleCooldowns = new Dictionary<string, DateTime> ();

    class OnlinePlayer
    {
        public BasePlayer Player;
        public BasePlayer Target;
        public StorageContainer View;
        public List<BasePlayer> Matches;

        public OnlinePlayer (BasePlayer player)
        {
        }
    }

    public Dictionary<ItemContainer, ulong> containers = new Dictionary<ItemContainer, ulong> ();

    [OnlinePlayers]
    Hash<BasePlayer, OnlinePlayer> onlinePlayers = new Hash<BasePlayer, OnlinePlayer> ();

    #endregion

    #region Initialization

    protected override void LoadDefaultConfig ()
    {
        Config ["Settings", "box"] = "assets/prefabs/deployable/woodenbox/woodbox_deployed.prefab";
        Config ["Settings", "cooldownMinutes"] = 5;
        Config ["Settings", "refundRatio"] = 0.5f;
        Config ["Settings", "radiationMax"] = 1;
        Config ["Settings", "NPCOnly"] = false;
        Config ["Settings", "NPCIDs"] = new List<object> ();
        Config ["VERSION"] = Version.ToString ();
    }

    void Unloaded ()
    {
        foreach (var player in BasePlayer.activePlayerList) {
            OnlinePlayer onlinePlayer;
            if (onlinePlayers.TryGetValue (player, out onlinePlayer) && onlinePlayer.View != null) {
                CloseBoxView (player, onlinePlayer.View);
            }
        }
    }

    void Init ()
    {
        Unsubscribe (nameof (CanNetworkTo));
    }

    void Loaded ()
    {
        permission.RegisterPermission ("recycle.use", this);
        LoadMessages ();
        CheckConfig ();

        cooldownMinutes = GetConfig ("Settings", "cooldownMinutes", 5f);
        box = GetConfig ("Settings", "box", "assets/prefabs/deployable/woodenbox/box_wooden.item.prefab");
        refundRatio = GetConfig ("Settings", "refundRatio", 0.5f);
        radiationMax = GetConfig ("Settings", "radiationMax", 1f);

        npconly = GetConfig ("Settings", "NPCOnly", false);
        npcids = GetConfig ("Settings", "NPCIDs", new List<object> ());
    }

    void CheckConfig ()
    {
        if (Config ["VERSION"] == null) {
            // FOR COMPATIBILITY WITH INITIAL VERSIONS WITHOUT VERSIONED CONFIG
            ReloadConfig ();
        } else if (GetConfig<string> ("VERSION", "") != Version.ToString ()) {
            // ADDS NEW, IF ANY, CONFIGURATION OPTIONS
            ReloadConfig ();
        }
    }

    protected void ReloadConfig ()
    {
        Config ["VERSION"] = Version.ToString ();

        // NEW CONFIGURATION OPTIONS HERE
        // END NEW CONFIGURATION OPTIONS

        PrintToConsole ("Upgrading configuration file");
        SaveConfig ();
    }

    void LoadMessages ()
    {
        lang.RegisterMessages (new Dictionary<string, string>
            {
                    { "Recycle: Complete", "Recycling <color=lime>{0}</color> to {1}% base materials:" },
                    { "Recycle: Item", "    <color=lime>{0}</color> X <color=yellow>{1}</color>" },
                    { "Recycle: Invalid", "Cannot recycle that!" },
                    { "Denied: Permission", "You lack permission to do that" },
                    { "Denied: Privilege", "You lack permission to do that" },
                    { "Denied: Swimming", "You cannot do that while swimming" },
                    { "Denied: Falling", "You cannot do that while falling" },
                    { "Denied: Wounded", "You cannot do that while wounded" },
                    { "Denied: Irradiated", "You cannot do that while irradiated" },
                    { "Denied: Generic", "You cannot do that right now" },
                    { "Cooldown: Seconds", "You are doing that too often, try again in a {0} seconds(s)." },
                    { "Cooldown: Minutes", "You are doing that too often, try again in a {0} minute(s)." },
                }, this);
    }

    private bool IsBox (BaseNetworkable entity)
    {
        foreach (KeyValuePair<BasePlayer, OnlinePlayer> kvp in onlinePlayers) {
            if (kvp.Value.View != null && kvp.Value.View.net.ID == entity.net.ID) {
                return true;
            }
        }

        return false;
    }

    #endregion

    #region Oxide Hooks

    object CanNetworkTo (BaseNetworkable entity, BasePlayer target)
    {
        if (entity == null || target == null || entity == target)
            return null;
        if (target.IsAdmin)
            return null;

        OnlinePlayer onlinePlayer;
        bool IsMyBox = false;
        if (onlinePlayers.TryGetValue (target, out onlinePlayer)) {
            if (onlinePlayer.View != null && onlinePlayer.View.net.ID == entity.net.ID) {
                IsMyBox = true;
            }
        }

        if (IsBox (entity) && !IsMyBox)
            return false;

        return null;
    }

    void OnPlayerInit (BasePlayer player)
    {
        onlinePlayers [player].View = null;
        onlinePlayers [player].Target = null;
        onlinePlayers [player].Matches = null;
    }

    void OnPlayerDisconnected (BasePlayer player)
    {
        if (onlinePlayers [player].View != null) {
            CloseBoxView (player, onlinePlayers [player].View);
        }
    }

    void OnPlayerLootEnd (PlayerLoot inventory)
    {
        BasePlayer player;
        if ((player = inventory.GetComponent<BasePlayer> ()) == null)
            return;

        OnlinePlayer onlinePlayer;
        if (onlinePlayers.TryGetValue (player, out onlinePlayer) && onlinePlayer.View != null) {
            if (onlinePlayer.View == inventory.entitySource) {
                CloseBoxView (player, (StorageContainer)inventory.entitySource);
            }
        }
    }

    void OnItemAddedToContainer (ItemContainer container, Item item)
    {
        if (container.playerOwner is BasePlayer) {
            if (onlinePlayers.ContainsKey (container.playerOwner)) {
                BasePlayer owner = container.playerOwner;
                if (containers.ContainsKey (container)) {
                    if (SalvageItem (owner, item)) {
                        item.Remove (0f);
                        item.RemoveFromContainer ();
                    } else {
                        ShowNotification (owner, GetMsg ("Recycle: Invalid", owner));
                        item.MoveToContainer (owner.inventory.containerMain);
                    }
                }
            }
        }
    }

    void OnUseNPC (BasePlayer npc, BasePlayer player)
    {
        if (!npcids.Contains (npc.UserIDString))
            return;
        ShowBox (player, player);
    }

    #endregion

    #region Commands

    [ConsoleCommand ("rec")]
    void ccRec (ConsoleSystem.Arg arg)
    {
        cmdRec (arg.Connection.player as BasePlayer, arg.cmd.Name, arg.Args);
    }

    [ChatCommand ("rec")]
    void cmdRec (BasePlayer player, string command, string [] args)
    {
        if (npconly)
            return;

        ShowBox (player, player);
    }

    #endregion

    #region Core Methods

    void ShowBox (BasePlayer player, BaseEntity target)
    {
        string playerID = player.userID.ToString ();

        if (!CanPlayerRecycle (player))
            return;

        if (cooldownMinutes > 0 && !player.IsAdmin) {
            DateTime startTime;

            if (recycleCooldowns.TryGetValue (playerID, out startTime)) {
                DateTime endTime = DateTime.Now;

                TimeSpan span = endTime.Subtract (startTime);
                if (span.TotalMinutes > 0 && span.TotalMinutes < Convert.ToDouble (cooldownMinutes)) {
                    double timeleft = System.Math.Round (Convert.ToDouble (cooldownMinutes) - span.TotalMinutes, 2);
                    if (span.TotalSeconds < 0) {
                        recycleCooldowns.Remove (playerID);
                    }

                    if (timeleft < 1) {
                        double timelefts = System.Math.Round ((Convert.ToDouble (cooldownMinutes) * 60) - span.TotalSeconds);
                        SendReply (player, string.Format (GetMsg ("Cooldown: Seconds", player), timelefts.ToString ()));
                        return;
                    } else {
                        SendReply (player, string.Format (GetMsg ("Cooldown: Minutes", player), System.Math.Round (timeleft).ToString ()));
                        return;
                    }
                } else {
                    recycleCooldowns.Remove (playerID);
                }
            }
        }

        if (!recycleCooldowns.ContainsKey (player.userID.ToString ())) {
            recycleCooldowns.Add (player.userID.ToString (), DateTime.Now);
        }
        var ply = onlinePlayers [player];
        if (ply.View == null) {
            OpenBoxView (player, target);
            return;
        }

        CloseBoxView (player, ply.View);
        timer.In (1f, () => OpenBoxView (player, target));
    }

    void HideBox (BasePlayer player)
    {
        player.EndLooting ();
        var ply = onlinePlayers [player];
        if (ply.View == null) {
            return;
        }

        CloseBoxView (player, ply.View);
    }

    void OpenBoxView (BasePlayer player, BaseEntity targArg)
    {
        Subscribe (nameof (CanNetworkTo));

        var pos = new Vector3 (player.transform.position.x, player.transform.position.y - 0.6f, player.transform.position.z);
        int slots = 1;
        var view = GameManager.server.CreateEntity (box, pos) as StorageContainer;
        view.GetComponent<DestroyOnGroundMissing> ().enabled = false;
        view.GetComponent<GroundWatch> ().enabled = false;
        view.transform.position = pos;


        if (!view)
            return;

        player.EndLooting ();
        if (targArg is BasePlayer) {
            BasePlayer target = targArg as BasePlayer;
            ItemContainer container = new ItemContainer ();
            container.playerOwner = player;
            container.ServerInitialize ((Item)null, slots);
            if ((int)container.uid == 0)
                container.GiveUID ();


            if (!containers.ContainsKey (container)) {
                containers.Add (container, player.userID);
            }

            view.enableSaving = false;
            view.Spawn ();
            view.inventory = container;
            view.SendNetworkUpdate (BasePlayer.NetworkQueue.Update);
            onlinePlayers [player].View = view;
            onlinePlayers [player].Target = target;
            timer.Once (0.1f, delegate () {
                view.PlayerOpenLoot (player);
            });

        }
    }

    void CloseBoxView (BasePlayer player, StorageContainer view)
    {
        OnlinePlayer onlinePlayer;
        if (!onlinePlayers.TryGetValue (player, out onlinePlayer))
            return;
        if (onlinePlayer.View == null)
            return;

        if (containers.ContainsKey (view.inventory)) {
            containers.Remove (view.inventory);
        }

        player.inventory.loot.containers = new List<ItemContainer> ();
        view.inventory = new ItemContainer ();

        if (player.inventory.loot.IsLooting ()) {
            player.SendConsoleCommand ("inventory.endloot", null);
        }


        onlinePlayer.View = null;
        onlinePlayer.Target = null;

        view.KillMessage ();

        if (onlinePlayers.Values.Count (p => p.View != null) <= 0) {
            Unsubscribe (nameof (CanNetworkTo));
        }
    }

    //        bool SalvageItem(BasePlayer player, Item item)
    //        {
    //            var sb = new StringBuilder();
    //
    //            var ratio = item.hasCondition ? (item.condition / item.maxCondition) : 1;
    //
    //            sb.Append(string.Format(GetMsg("Recycle: Complete", player), item.info.displayName.english, (refundRatio * 100)));
    //
    //            if (item.info.Blueprint == null)
    //            {
    //                return false;
    //            }
    //
    //            foreach (var ingredient in item.info.Blueprint.ingredients)
    //            {
    //                if (!ingredient.itemDef.shortname == "scrap")
    //                {
    //                    var refundAmount = (double)ingredient.amount / item.info.Blueprint.amountToCreate;
    //                    refundAmount *= item.amount;
    //                    refundAmount *= ratio;
    //                    refundAmount *= refundRatio;
    //                    refundAmount = System.Math.Ceiling(refundAmount);
    //                    if (refundAmount < 1)
    //                        refundAmount = 1;
    //
    //                    var newItem = ItemManager.Create(ingredient.itemDef, (int)refundAmount);
    //
    //                    ItemBlueprint ingredientBp = ingredient.itemDef.Blueprint;
    //                    if (item.hasCondition)
    //                        newItem.condition = (float)System.Math.Ceiling(newItem.maxCondition * ratio);
    //
    //                    player.GiveItem(newItem);
    //                            sb.AppendLine();
    //                            sb.Append(string.Format(GetMsg("Recycle: Item", player), newItem.info.displayName.english, newItem.amount));
    //                }
    //            }
    //
    //            ShowNotification(player, sb.ToString());
    //
    //            return true;
    //        }
    //
    bool SalvageItem (BasePlayer player, Item item)
    {
        if (item == null) {
            return false;
        }

        if (item.info.shortname == "scrap") {
            return false;
        }
        var refundAmount = refundRatio;
        var sb = new StringBuilder ();

        if (item.hasCondition) {
            refundAmount = Mathf.Clamp01 (refundAmount * Mathf.Clamp (item.conditionNormalized * item.maxConditionNormalized, 0.1f, 1f));
        }

        var amountToConsume = 1;
        if (item.amount > 1) {
            amountToConsume = Mathf.CeilToInt (Mathf.Min (item.amount, item.info.stackable * 0.1f));
        }

        if (item.info.Blueprint.scrapFromRecycle > 0) {
            var newItem = ItemManager.CreateByName ("scrap", item.info.Blueprint.scrapFromRecycle * amountToConsume);
            if (newItem != null) {
                player.GiveItem (newItem);
                sb.AppendLine ();
                sb.Append (string.Format (GetMsg ("Recycle: Item", player), newItem.info.displayName.english, newItem.amount));
            }
        }

        item.UseItem (amountToConsume);
        foreach (ItemAmount current in item.info.Blueprint.ingredients.OrderBy (x => Core.Random.Range (0, 1000))) {
            if (!(current.itemDef.shortname == "scrap") && item.info.Blueprint != null) {
                var refundMultiplier = current.amount / item.info.Blueprint.amountToCreate;
                var refundMaximum = 0;
                if (refundMultiplier <= 1) {
                    for (var index = 0; index < amountToConsume; ++index) {
                        if (Core.Random.Range (0, 1) <= refundAmount) {
                            ++refundMaximum;
                        }
                    }
                } else {
                    refundMaximum = Mathf.CeilToInt (Mathf.Clamp (refundMultiplier * refundAmount, 0f, current.amount) * amountToConsume);
                }

                if (refundMaximum > 0) {
                    int refundIterations = Mathf.Clamp (Mathf.CeilToInt (refundMaximum / current.itemDef.stackable), 1, refundMaximum);
                    for (var index = 0; index < refundIterations; ++index) {
                        var itemAmount = refundMaximum <= current.itemDef.stackable ? refundMaximum : current.itemDef.stackable;
                        var newItem = ItemManager.Create (current.itemDef, itemAmount);
                        if (newItem != null) {
                            player.GiveItem (newItem);
                            sb.AppendLine ();
                            sb.Append (string.Format (GetMsg ("Recycle: Item", player), newItem.info.displayName.english, newItem.amount));

                            refundMaximum -= itemAmount;
                            if (refundMaximum <= 0)
                                break;
                        }
                    }
                }
            }
        }

        var msg = sb.ToString ();
        if (msg != string.Empty) {
            ShowNotification (player, msg);
        }

        return true;
    }

    bool CanPlayerRecycle (BasePlayer player)
    {
        if (!permission.UserHasPermission (player.UserIDString, "recycle.use")) {
            SendReply (player, GetMsg ("Denied: Permission", player));
            return false;
        }

        if (!player.CanBuild ()) {
            SendReply (player, GetMsg ("Denied: Privilege", player));
            return false;
        }
        if (radiationMax > 0 && player.radiationLevel > radiationMax) {
            SendReply (player, GetMsg ("Denied: Irradiated", player));
            return false;
        }
        if (player.IsSwimming ()) {
            SendReply (player, GetMsg ("Denied: Swimming", player));
            return false;
        }
        if (!player.IsOnGround ()) {
            SendReply (player, GetMsg ("Denied: Falling", player));
            return false;
        }
        if (player.IsFlying) {
            SendReply (player, GetMsg ("Denied: Falling", player));
            return false;
        }
        if (player.IsWounded ()) {
            SendReply (player, GetMsg ("Denied: Wounded", player));
            return false;
        }

        var canRecycle = Interface.Call ("CanRecycleCommand", player);
        if (canRecycle != null) {
            if (canRecycle is string) {
                SendReply (player, Convert.ToString (canRecycle));
            } else {
                SendReply (player, GetMsg ("Denied: Generic", player));
            }
            return false;
        }

        return true;
    }

    #endregion

    #region GUI

    public string jsonNotify = @"[{""name"":""NotifyMsg"",""parent"":""Overlay"",""components"":[{""type"":""UnityEngine.UI.Image"",""color"":""0 0 0 0.89""},{""type"":""RectTransform"",""anchormax"":""0.99 0.94"",""anchormin"":""0.69 0.77""}]},{""name"":""MassText"",""parent"":""NotifyMsg"",""components"":[{""type"":""UnityEngine.UI.Text"",""text"":""{msg}"",""fontSize"":16,""align"":""UpperLeft""},{""type"":""RectTransform"",""anchormax"":""0.98 0.99"",""anchormin"":""0.01 0.02""}]},{""name"":""CloseButton{1}"",""parent"":""NotifyMsg"",""components"":[{""type"":""UnityEngine.UI.Button"",""color"":""0.95 0 0 0.68"",""close"":""NotifyMsg"",""imagetype"":""Tiled""},{""type"":""RectTransform"",""anchormax"":""0.99 1"",""anchormin"":""0.91 0.86""}]},{""name"":""CloseButtonLabel"",""parent"":""CloseButton{1}"",""components"":[{""type"":""UnityEngine.UI.Text"",""text"":""X"",""fontSize"":5,""align"":""MiddleCenter""},{""type"":""RectTransform"",""anchormax"":""1 1"",""anchormin"":""0 0""}]}]";

    public void ShowNotification (BasePlayer player, string msg)
    {
        this.HideNotification (player);
        string send = jsonNotify.Replace ("{msg}", msg);

        CommunityEntity.ServerInstance.ClientRPCEx (new Network.SendInfo { connection = player.net.connection }, null, "AddUI", send);
        timer.Once (3f, delegate () {
            this.HideNotification (player);
        });
    }

    public void HideNotification (BasePlayer player)
    {
        CommunityEntity.ServerInstance.ClientRPCEx (new Network.SendInfo { connection = player.net.connection }, null, "DestroyUI", "NotifyMsg");
    }

    #endregion

    #region HelpText

    private void SendHelpText (BasePlayer player)
    {
        var sb = new StringBuilder ()
           .Append ("Recycle by <color=#ce422b>http://rustservers.io</color>\n")
           .Append ("  ").Append ("<color=\"#ffd479\">/rec</color> - Open recycle box").Append ("\n");
        player.ChatMessage (sb.ToString ());
    }

    #endregion

    #region Helper methods

    string GetMsg (string key, BasePlayer player = null)
    {
        return lang.GetMessage (key, this, player == null ? null : player.UserIDString);
    }

    private T GetConfig<T> (string name, T defaultValue)
    {
        if (Config [name] == null) {
            return defaultValue;
        }

        return (T)Convert.ChangeType (Config [name], typeof (T));
    }

    private T GetConfig<T> (string name, string name2, T defaultValue)
    {
        if (Config [name, name2] == null) {
            return defaultValue;
        }

        return (T)Convert.ChangeType (Config [name, name2], typeof (T));
    }

    #endregion
}
}