﻿using AI;
using BlockTypes;
using Jobs;
using Monsters;
using NPC;
using Pandaros.Settlers.AI;
using Pandaros.Settlers.Entities;
using Pandaros.Settlers.Items.Armor;
using Pandaros.Settlers.Items.Healing;
using Pandaros.Settlers.Research;
using Pipliz;
using Pipliz.JSON;
using Recipes;
using Shared;
using System;
using System.Collections.Generic;
using System.IO;
using TerrainGeneration;
using Math = System.Math;
using Random = Pipliz.Random;
using Time = Pipliz.Time;

namespace Pandaros.Settlers.Managers
{
    [ModLoader.ModManager]
    public static class SettlerManager
    {
        public const int MAX_BUYABLE = 10;
        public const int MIN_PERSPAWN = 1;
        public const int ABSOLUTE_MAX_PERSPAWN = 5;
        private const string LAST_KNOWN_JOB_TIME_KEY = "lastKnownTime";
        private const string LEAVETIME_JOB = "LeaveTime_JOB";
        private const string LEAVETIME_BED = "LeaveTime_BED";
        private const string ISSETTLER = "isSettler";
        private const string KNOWN_ITTERATIONS = "SKILLED_ITTERATIONS";

        private const int _NUMBEROFCRAFTSPERPERCENT = 1000;
        private const int _UPDATE_TIME = 10;
        public static double IN_GAME_HOUR_IN_SECONDS = 3600 / TimeCycle.Settings.GameTimeScale;
        public static double BED_LEAVE_HOURS = IN_GAME_HOUR_IN_SECONDS * 5;
        public static double LOABOROR_LEAVE_HOURS = TimeSpan.FromDays(7).TotalHours * IN_GAME_HOUR_IN_SECONDS;
        public static double COLD_LEAVE_HOURS = IN_GAME_HOUR_IN_SECONDS * 5;
        public static double HOT_LEAVE_HOURS = IN_GAME_HOUR_IN_SECONDS * 6;
        private static float _baseFoodPerHour;
        private static double _updateTime;
        private static double _magicUpdateTime = Time.SecondsSinceStartDouble + Random.Next(2, 5);
        private static int _idNext = 1;
        private static double _nextLaborerTime = Time.SecondsSinceStartDouble + Random.Next(2, 6);
        private static double _nextbedTime = Time.SecondsSinceStartDouble + Random.Next(1, 2);

        public static List<HealingOverTimeNPC> HealingSpells { get; } = new List<HealingOverTimeNPC>();

        [ModLoader.ModCallback(ModLoader.EModCallbackType.AfterSelectedWorld, GameLoader.NAMESPACE + ".Managers.SettlerManager.AfterSelectedWorld.Healing")]
        public static void Healing()
        {
            HealingOverTimeNPC.NewInstance += HealingOverTimeNPC_NewInstance;
        }

        [ModLoader.ModCallback(ModLoader.EModCallbackType.OnPlayerClicked,  GameLoader.NAMESPACE + ".SettlerManager.OnPlayerClicked")]
        public static void OnPlayerClicked(Players.Player player, Box<PlayerClickedData> boxedData)
        {
            if (boxedData.item1.clickType == PlayerClickedData.ClickType.Right &&
                boxedData.item1.rayCastHit.rayHitType == RayHitType.Block &&
                World.TryGetTypeAt(boxedData.item1.rayCastHit.voxelHit, out ushort blockHit) &&
                blockHit == BuiltinBlocks.BerryBush)
            {
                var inv = Inventory.GetInventory(player);
                inv.TryAdd(BuiltinBlocks.Berry, 2);
            }
        }

        private static void HealingOverTimeNPC_NewInstance(object sender, EventArgs e)
        {
            var healing = sender as HealingOverTimeNPC;

            lock (HealingSpells)
            {
                HealingSpells.Add(healing);
            }

            healing.Complete += Healing_Complete;
        }

        private static void Healing_Complete(object sender, EventArgs e)
        {
            var healing = sender as HealingOverTimeNPC;

            lock (HealingSpells)
            {
                HealingSpells.Remove(healing);
            }

            healing.Complete -= Healing_Complete;
        }

        [ModLoader.ModCallback(ModLoader.EModCallbackType.OnUpdate, GameLoader.NAMESPACE + ".SettlerManager.OnUpdate")]
        public static void OnUpdate()
        {
            if (ServerManager.ColonyTracker != null)
            foreach (var colony in ServerManager.ColonyTracker.ColoniesByID.Values)
            {
                if (_magicUpdateTime < Time.SecondsSinceStartDouble)
                {
                    foreach (var follower in colony.Followers)
                    {
                        var inv = SettlerInventory.GetSettlerInventory(follower);

                        if (inv.MagicItemUpdateTime < Time.SecondsSinceStartDouble)
                        {
                            foreach (var item in inv.Armor)
                                if (item.Value.Id != 0 && ArmorFactory.ArmorLookup.TryGetValue(item.Value.Id, out var armor))
                                {
                                    armor.Update();

                                    if (armor.HPTickRegen != 0)
                                        follower.Heal(armor.HPTickRegen);
                                }

                            var hasBandages = colony.Stockpile.Contains(TreatedBandage.Item.ItemIndex) ||
                                      colony.Stockpile.Contains(Bandage.Item.ItemIndex);

                            if (hasBandages &&
                                follower.health < follower.Colony.NPCHealthMax &&
                                !HealingOverTimeNPC.NPCIsBeingHealed(follower))
                            {
                                var healing = false;

                                if (follower.Colony.NPCHealthMax - follower.health > TreatedBandage.INITIALHEAL)
                                {
                                    colony.Stockpile.TryRemove(TreatedBandage.Item.ItemIndex);
                                    healing = true;
                                    ServerManager.SendAudio(follower.Position.Vector, GameLoader.NAMESPACE + ".Bandage");

                                    var heal = new HealingOverTimeNPC(follower, TreatedBandage.INITIALHEAL,
                                                                      TreatedBandage.TOTALHOT, 5,
                                                                      TreatedBandage.Item.ItemIndex);
                                }

                                if (!healing)
                                {
                                    colony.Stockpile.TryRemove(Bandage.Item.ItemIndex);
                                    healing = true;
                                    ServerManager.SendAudio(follower.Position.Vector, GameLoader.NAMESPACE + ".Bandage");

                                    var heal = new HealingOverTimeNPC(follower, Bandage.INITIALHEAL, Bandage.TOTALHOT, 5,
                                                                      Bandage.Item.ItemIndex);
                                }
                            }


                            inv.MagicItemUpdateTime += 5000;
                        }
                    }
                }
                    

                if (_updateTime < Time.SecondsSinceStartDouble && colony.OwnerIsOnline())
                {
                    NPCBase lastNPC = null;

                    foreach (var follower in colony.Followers)
                    {
                        if (TimeCycle.IsDay)
                            if (lastNPC == null ||
                            UnityEngine.Vector3.Distance(lastNPC.Position.Vector, follower.Position.Vector) > 15 &&
                            Random.NextBool())
                            {
                                lastNPC = follower;
                                ServerManager.SendAudio(follower.Position.Vector, GameLoader.NAMESPACE + ".TalkingAudio");
                            }
                    }
                }

                var cs = ColonyState.GetColonyState(colony);

                if (cs.SettlersEnabled)
                    if (EvaluateSettlers(cs) ||
                        EvaluateLaborers(cs) ||
                        EvaluateBeds(cs))
                        colony.SendUpdate();

                UpdateFoodUse(cs);
            }
            
            if (_magicUpdateTime < Time.SecondsSinceStartDouble)
                _magicUpdateTime = Time.SecondsSinceStartDouble + 1;

            if (_updateTime < Time.SecondsSinceStartDouble && TimeCycle.IsDay)
                _updateTime = Time.SecondsSinceStartDouble + _UPDATE_TIME;
        }

        [ModLoader.ModCallback(ModLoader.EModCallbackType.AfterWorldLoad, GameLoader.NAMESPACE + ".SettlerManager.AfterWorldLoad")]
        public static void AfterWorldLoad()
        {
            _baseFoodPerHour = ServerManager.ServerSettings.NPCs.FoodUsePerHour;
            IN_GAME_HOUR_IN_SECONDS = 3600 / TimeCycle.Settings.GameTimeScale;
            BED_LEAVE_HOURS = IN_GAME_HOUR_IN_SECONDS * 5;
            LOABOROR_LEAVE_HOURS = TimeSpan.FromDays(7).TotalHours * IN_GAME_HOUR_IN_SECONDS;
            COLD_LEAVE_HOURS = IN_GAME_HOUR_IN_SECONDS * 5;
            HOT_LEAVE_HOURS = IN_GAME_HOUR_IN_SECONDS * 6;

            foreach (var p in ServerManager.ColonyTracker.ColoniesByID.Values)
                UpdateFoodUse(ColonyState.GetColonyState(p));
        }

        [ModLoader.ModCallback(ModLoader.EModCallbackType.OnPlayerConnectedEarly, GameLoader.NAMESPACE + ".SettlerManager.OnPlayerConnectedEarly")]
        public static void OnPlayerConnectedEarly(Players.Player p)
        {
            if (p.IsConnected && !Configuration.OfflineColonies)
            {
                foreach (Colony c in p.Colonies)
                {
                    var file = $"{GameLoader.GAMEDATA_FOLDER}/savegames/{ServerManager.WorldName}/NPCArchive/{c.ColonyID}.json";

                    if (File.Exists(file) && JSON.Deserialize(file, out var followersNode, false))
                    {
                        File.Delete(file);
                        PandaLogger.Log(ChatColor.cyan, $"Player {p.ID.steamID} is reconnected. Restoring Colony.");

                        foreach (var node in followersNode.LoopArray())
                            try
                            {
                                node.SetAs("id", GetAIID());

                                var npc = new NPCBase(c, node);
                                ModLoader.TriggerCallbacks(ModLoader.EModCallbackType.OnNPCLoaded, npc, node);

                                foreach (var job in new List<IJob>(c.JobFinder.JobsData.OpenJobs))
                                    if (node.TryGetAs("JobPoS", out JSONNode pos) && job.GetJobLocation() == (Vector3Int)pos)
                                    {
                                        if (job.IsValid && job.NeedsNPC)
                                        {
                                            npc.TakeJob(job);
                                            job.SetNPC(npc);
                                            c.JobFinder.Remove(job);
                                        }

                                        break;
                                    }
                            }
                            catch (Exception ex)
                            {
                                PandaLogger.LogError(ex);
                            }

                        JSON.Serialize(file, new JSONNode(NodeType.Array));
                        c.JobFinder.Update();
                    }
                }
            }
        }

        private static int GetAIID()
        {
            while (true)
            {
                if (_idNext == 1000000000) _idNext = 1;

                if (!NPCTracker.Contains(_idNext)) break;
                _idNext++;
            }

            return _idNext++;
        }


        [ModLoader.ModCallback(ModLoader.EModCallbackType.OnPlayerConnectedLate, GameLoader.NAMESPACE + ".SettlerManager.OnPlayerConnectedLate")]
        public static void OnPlayerConnectedLate(Players.Player p)
        {
            if (p == null || p.Colonies == null || p.Colonies.Length == 0)
                return;

            if (Configuration.GetorDefault("SettlersEnabled", true) &&
                Configuration.GetorDefault("MaxSettlersToggle", 4) > 0 &&
                p.ActiveColony != null)
            {
                var cs = ColonyState.GetColonyState(p.ActiveColony);

                if (cs.SettlersEnabled && Configuration.GetorDefault("ColonistsRecruitment", true))
                    PandaChat.Send(p,
                                   string
                                      .Format("Recruiting over {0} colonists will cost the base food cost plus a compounding {1} food. This compounding value resets once per in game day. If you build it... they will come.",
                                              MAX_BUYABLE,
                                              Configuration.GetorDefault("CompoundingFoodRecruitmentCost", 5)),
                                   ChatColor.orange);

                if (cs.SettlersToggledTimes < Configuration.GetorDefault("MaxSettlersToggle", 4))
                {
                    var settlers = cs.SettlersEnabled ? "on" : "off";

                    if (Configuration.GetorDefault("MaxSettlersToggle", 4) > 0)
                        PandaChat.Send(p,
                                       $"To disable/enable gaining random settlers type '/settlers off' Note: this can only be used {Configuration.GetorDefault("MaxSettlersToggle", 4)} times.",
                                       ChatColor.orange);
                    else
                        PandaChat.Send(p, $"To disable/enable gaining random settlers type '/settlers off'",
                                       ChatColor.orange);

                    PandaChat.Send(p, $"Random Settlers are currently {settlers}!", ChatColor.orange);
                }
            }

            foreach (Colony c in p.Colonies)
            {
                UpdateFoodUse(ColonyState.GetColonyState(c));
                c.SendUpdate();
                c.SendColonistCount();
            }
        }

        [ModLoader.ModCallback(ModLoader.EModCallbackType.OnPlayerDisconnected, GameLoader.NAMESPACE + ".SettlerManager.OnPlayerDisconnected")]
        public static void OnPlayerDisconnected(Players.Player p)
        {
            if (p == null || p.Colonies == null || p.Colonies.Length == 0)
                return;

            foreach (Colony c in p.Colonies)
                SaveOffline(c);
        }

        public static void SaveOffline(Colony colony)
        {
            if (colony.OwnerIsOnline())
                return;

            try
            {
                var folder = $"{GameLoader.GAMEDATA_FOLDER}/savegames/{ServerManager.WorldName}/NPCArchive/";

                if (!Directory.Exists(folder))
                    Directory.CreateDirectory(folder);

                var file = $"{folder}{colony.ColonyID}.json";

                if (!Configuration.OfflineColonies)
                {
                    if (!JSON.Deserialize(file, out var followers, false))
                        followers = new JSONNode(NodeType.Array);

                    followers.ClearChildren();

                    PandaLogger.Log(ChatColor.cyan, $"All players from {colony.ColonyID} have disconnected. Clearing colony until reconnect.");

                    var copyOfFollowers = new List<NPCBase>();

                    foreach (var follower in colony.Followers)
                    {
                        JSONNode jobloc = null;

                        if (follower.IsValid)
                        {
                            var job = follower.Job;

                            if (job != null && job.GetJobLocation() != Vector3Int.invalidPos)
                            {
                                jobloc  = (JSONNode) job.GetJobLocation();
                                job.SetNPC(null);
                                follower.ClearJob();
                            }
                        }

                        if (follower.TryGetJSON(out var node))
                        {
                            if (jobloc != null)
                                node.SetAs("JobPoS", jobloc);

                            ModLoader.TriggerCallbacks(ModLoader.EModCallbackType.OnNPCSaved, follower, node);
                            followers.AddToArray(node);
                            copyOfFollowers.Add(follower);
                        }
                    }

                    JSON.Serialize(file, followers);

                    foreach (var deadMan in copyOfFollowers)
                        deadMan.OnDeath();

                    colony.ForEachOwner(o => MonsterTracker.KillAllZombies(o));
                }
            }
            catch (Exception ex)
            {
                PandaLogger.LogError(ex);
            }
        }

        [ModLoader.ModCallback(ModLoader.EModCallbackType.OnNPCRecruited, GameLoader.NAMESPACE + ".SettlerManager.OnNPCRecruited")]
        public static void OnNPCRecruited(NPCBase npc)
        {
            if (npc.CustomData == null)
                npc.CustomData = new JSONNode();

            if (npc.CustomData.TryGetAs(ISSETTLER, out bool settler) && settler)
                return;

            var ps = ColonyState.GetColonyState(npc.Colony);

            if (ps.SettlersEnabled)
            {
                if (Configuration.GetorDefault("ColonistsRecruitment", true))
                {
                    if (ps.SettlersEnabled && npc.Colony.FollowerCount > MAX_BUYABLE)
                    {
                        var cost = Configuration.GetorDefault("CompoundingFoodRecruitmentCost", 5) * ps.ColonistsBought;
                        var num  = 0f;

                        if (cost < 1)
                            cost = 1;

                        if (npc.Colony.Stockpile.TotalFood < cost ||
                            !npc.Colony.Stockpile.TryRemoveFood(ref num, cost))
                        {
                            PandaChat.Send(npc.Colony, $"Could not recruit a new colonist; not enough food in stockpile. {cost + ServerManager.ServerSettings.NPCs.RecruitmentCost} food required.", ChatColor.red);
                            npc.Colony.Stockpile.Add(BuiltinBlocks.Bread, (int)Math.Floor(ServerManager.ServerSettings.NPCs.RecruitmentCost / 3));
                            npc.health = 0;
                            npc.Update();
                            return;
                        }

                        ps.ColonistsBought++;
                        ps.NextColonistBuyTime = TimeCycle.TotalTime.Value.Hours +  24;
                    }

                    SettlerInventory.GetSettlerInventory(npc);
                    UpdateFoodUse(ps);
                }
                else
                {
                    PandaChat.Send(npc.Colony, "The server administrator has disabled recruitment of colonists while settlers are enabled.");
                }
            }
        }

        [ModLoader.ModCallback(ModLoader.EModCallbackType.OnNPCDied, GameLoader.NAMESPACE + ".SettlerManager.OnNPCDied")]
        public static void OnNPCDied(NPCBase npc)
        {
            SettlerInventory.GetSettlerInventory(npc);
            UpdateFoodUse(ColonyState.GetColonyState(npc.Colony));
        }

        [ModLoader.ModCallback(ModLoader.EModCallbackType.OnNPCLoaded, GameLoader.NAMESPACE + ".SettlerManager.OnNPCLoaded")]
        public static void OnNPCLoaded(NPCBase npc, JSONNode node)
        {
            if (npc.CustomData == null)
                npc.CustomData = new JSONNode();

            if (node.TryGetAs<JSONNode>(GameLoader.SETTLER_INV, out var invNode))
                npc.CustomData.SetAs(GameLoader.SETTLER_INV, new SettlerInventory(invNode, npc));

            if (node.TryGetAs<double>(LEAVETIME_JOB, out var leaveTime))
                npc.CustomData.SetAs(LEAVETIME_JOB, leaveTime);

            if (node.TryGetAs<float>(GameLoader.ALL_SKILLS, out var skills))
                npc.CustomData.SetAs(GameLoader.ALL_SKILLS, skills);

            if (node.TryGetAs<int>(KNOWN_ITTERATIONS, out var jobItterations))
                npc.CustomData.SetAs(KNOWN_ITTERATIONS, jobItterations);
        }

        [ModLoader.ModCallback(ModLoader.EModCallbackType.OnNPCSaved, GameLoader.NAMESPACE + ".SettlerManager.OnNPCSaved")]
        public static void OnNPCSaved(NPCBase npc, JSONNode node)
        {
            node.SetAs(GameLoader.SETTLER_INV, SettlerInventory.GetSettlerInventory(npc).ToJsonNode());

            if (npc.NPCType.IsLaborer && npc.CustomData.TryGetAs(LEAVETIME_JOB, out double leave))
                node.SetAs(LEAVETIME_JOB, leave);

            if (npc.CustomData.TryGetAs(GameLoader.ALL_SKILLS, out float allSkill))
                node.SetAs(GameLoader.ALL_SKILLS, allSkill);

            if (npc.CustomData.TryGetAs(KNOWN_ITTERATIONS, out int itt))
                node.SetAs(KNOWN_ITTERATIONS, itt);
        }

        [ModLoader.ModCallback(ModLoader.EModCallbackType.OnNPCCraftedRecipe, GameLoader.NAMESPACE + ".SettlerManager.OnNPCCraftedRecipe")]
        public static void OnNPCCraftedRecipe(IJob job, Recipe recipe, List<InventoryItem> results)
        {
            if (!job.NPC.CustomData.TryGetAs(KNOWN_ITTERATIONS, out int itt))
                job.NPC.CustomData.SetAs(KNOWN_ITTERATIONS, 1);
            else
                job.NPC.CustomData.SetAs(KNOWN_ITTERATIONS, itt + 1);

            if (!job.NPC.CustomData.TryGetAs(GameLoader.ALL_SKILLS, out float allSkill))
                job.NPC.CustomData.SetAs(GameLoader.ALL_SKILLS, 0f);

            var nextLevel = Pipliz.Math.RoundToInt(allSkill * 100) * _NUMBEROFCRAFTSPERPERCENT;

            if (itt >= nextLevel)
            {
                var nextFloat = allSkill + 0.001f;

                if (nextFloat > 0.025f)
                    nextFloat = 0.025f;

                job.NPC.CustomData.SetAs(KNOWN_ITTERATIONS, 0);
                job.NPC.CustomData.SetAs(GameLoader.ALL_SKILLS, nextFloat);
            }
        }

        public static void UpdateFoodUse(ColonyState state)
        {
            if (ServerManager.TerrainGenerator != null &&
                AIManager.NPCPathFinder != null)
            {
                var food   = _baseFoodPerHour;

                if (state.Difficulty != GameDifficulty.Normal && state.ColonyRef.FollowerCount > 10)
                {
                    var multiplier = .7 / state.ColonyRef.FollowerCount -
                                     state.ColonyRef.TemporaryData.GetAsOrDefault(PandaResearch.GetResearchKey(PandaResearch.ReducedWaste), 0f);

                    food += (float) (_baseFoodPerHour * multiplier);
                    food *= state.Difficulty.FoodMultiplier;
                }

                if (state.ColonyRef.InSiegeMode)
                    food = food * ServerManager.ServerSettings.NPCs.FoodUseMultiplierSiegeMode;

                if (food < _baseFoodPerHour)
                    food = _baseFoodPerHour;

                state.ColonyRef.FoodUsePerHour = food;
                state.ColonyRef.SendUpdate();
            }
        }

        public static bool EvaluateSettlers(ColonyState state)
        {
            var update = false;

            if (state.ColonyRef.OwnerIsOnline())
            {
                if (state.NextGenTime == 0)
                    state.NextGenTime = Time.SecondsSinceStartDouble +
                                        Random.Next(8, 16 - Pipliz.Math.RoundToInt(state.ColonyRef.TemporaryData.GetAsOrDefault(PandaResearch.GetResearchKey(PandaResearch.TimeBetween), 0f))) * IN_GAME_HOUR_IN_SECONDS;

                if (Time.SecondsSinceStartDouble > state.NextGenTime && state.ColonyRef.FollowerCount >= MAX_BUYABLE)
                {
                    var chance =
                        state.ColonyRef.TemporaryData.GetAsOrDefault(PandaResearch.GetResearchKey(PandaResearch.SettlerChance), 0f) +
                        state.Difficulty.AdditionalChance;

                    chance += SettlerEvaluation.SpawnChance(state);

                    var rand = Random.NextFloat();

                    if (chance > rand)
                    {
                        var addCount = Math.Floor(state.MaxPerSpawn * chance);

                        // if we lost alot of colonists add extra to help build back up.
                        if (state.ColonyRef.FollowerCount < state.HighestColonistCount)
                        {
                            var diff = state.HighestColonistCount - state.ColonyRef.FollowerCount;
                            addCount += Math.Floor(diff * .25);
                        }

                        try
                        {
                            var skillChance = state.ColonyRef.TemporaryData.GetAsOrDefault(PandaResearch.GetResearchKey(PandaResearch.SkilledLaborer), 0f);
                            var numbSkilled = 0;
                            rand = Random.NextFloat();

                            try
                            {
                                if (skillChance > rand)
                                    numbSkilled = Pipliz.Random.Next(1,
                                                        2 + Pipliz.Math.RoundToInt(state.ColonyRef.TemporaryData.GetAsOrDefault(PandaResearch.GetResearchKey(PandaResearch.NumberSkilledLaborer), 0f)));
                            }
                            catch (Exception ex)
                            {
                                PandaLogger.Log("NumberSkilledLaborer");
                                PandaLogger.LogError(ex);
                            }


                            if (addCount > 0)
                            {
                                if (addCount > 30)
                                    addCount = 30;

                                var reason = string.Format(SettlerReasoning.GetSettleReason(), addCount);

                                if (numbSkilled > 0)
                                    if (numbSkilled == 1)
                                        reason += string.Format(" {0} of them is skilled!", numbSkilled);
                                    else
                                        reason += string.Format(" {0} of them are skilled!", numbSkilled);

                                PandaChat.Send(state.ColonyRef, reason, ChatColor.magenta);

                                for (var i = 0; i < addCount; i++)
                                {
                                    var newGuy = new NPCBase(state.ColonyRef,
                                                             state.ColonyRef.GetRandomBanner().Position.Vector);

                                    SettlerInventory.GetSettlerInventory(newGuy);
                                    newGuy.CustomData.SetAs(ISSETTLER, true);

                                    if (i <= numbSkilled)
                                        newGuy.CustomData.SetAs(GameLoader.ALL_SKILLS, Random.Next(1, 10) * 0.002f);

                                    update = true;
                                    ModLoader.TriggerCallbacks(ModLoader.EModCallbackType.OnNPCRecruited, newGuy);
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            PandaLogger.Log("SkilledLaborer");
                            PandaLogger.LogError(ex);
                        }

                        if (state.ColonyRef.FollowerCount > state.HighestColonistCount)
                            state.HighestColonistCount = state.ColonyRef.FollowerCount;
                    }


                    state.NextGenTime = Time.SecondsSinceStartDouble +
                                        Random.Next(8,
                                                    16 - Pipliz.Math.RoundToInt(state.ColonyRef.TemporaryData.GetAsOrDefault(PandaResearch.GetResearchKey(PandaResearch.TimeBetween),
                                                                                               0f))) * IN_GAME_HOUR_IN_SECONDS;

                    state.ColonyRef.SendUpdate();
                }
            }

            return update;
        }

        [ModLoader.ModCallback(ModLoader.EModCallbackType.OnNPCJobChanged, GameLoader.NAMESPACE + ".SettlerManager.OnNPCJobChanged")]
        public static void OnNPCJobChanged(TupleStruct<NPCBase, IJob, IJob> data)
        {
            if (data != null && data.item1 != null && !data.item1.NPCType.IsLaborer)
                data.item1.CustomData.SetAs(LEAVETIME_JOB, 0);
        }

        private static bool EvaluateLaborers(ColonyState state)
        {
            var update = false;

            if (TimeCycle.IsDay && Time.SecondsSinceStartDouble > _nextLaborerTime)
            {
                if (state.ColonyRef.OwnerIsOnline())
                {
                    var unTrack = new List<NPCBase>();
                    var left    = 0;

                    for (var i = 0; i < state.ColonyRef.LaborerCount; i++)
                    {
                        var npc     = state.ColonyRef.FindLaborer(i);

                        if (!npc.CustomData.TryGetAs(LEAVETIME_JOB, out double leaveTime))
                        {
                            npc.CustomData.SetAs(LEAVETIME_JOB, Time.SecondsSinceStartDouble + LOABOROR_LEAVE_HOURS);
                        }
                        else if (leaveTime < Time.SecondsSinceStartDouble)
                        {
                            left++;
                            NPCLeaving(npc);
                        }
                    }

                    if (left > 0)
                        PandaChat.Send(state.ColonyRef,
                                       string.Concat(SettlerReasoning.GetNoJobReason(),
                                                     string.Format(" {0} colonists have left your colony.", left)),
                                       ChatColor.red);

                    update = unTrack.Count != 0;
                    state.ColonyRef.SendUpdate();
                }

                _nextLaborerTime = Time.SecondsSinceStartDouble + Random.Next(4, 6) * IN_GAME_HOUR_IN_SECONDS * 24;
            }

            return update;
        }

        private static void NPCLeaving(NPCBase npc)
        {
            if (Random.NextFloat() > .49f)
            {
                float cost = PenalizeFood(npc.Colony, 0.05f);
                PandaChat.Send(npc.Colony, $"A colonist has left your colony taking {cost} food.", ChatColor.red);
            }
            else
            {
                var numberOfItems = Random.Next(1, 10);

                for (var i = 0; i < numberOfItems; i++)
                {
                    var randItem = Random.Next(npc.Colony.Stockpile.ItemCount);
                    var item     = npc.Colony.Stockpile.GetByIndex(randItem);

                    if (item.Type != BuiltinBlocks.Air && item.Amount != 0)
                    {
                        var leaveTax = Pipliz.Math.RoundToInt(item.Amount * .10);
                        npc.Colony.Stockpile.TryRemove(item.Type, leaveTax);
                    }
                }

                PandaChat.Send(npc.Colony, $"A colonist has left your colony taking 10% of {numberOfItems} items from your stockpile.", ChatColor.red);
            }

            npc.health = 0;
            npc.OnDeath();
        }

        public static float PenalizeFood(Colony c, float percent)
        {
            var cost = (float)Math.Ceiling(c.Stockpile.TotalFood * percent);
            var num = 0f;

            if (cost < 1)
                cost = 1;

            c.Stockpile.TryRemoveFood(ref num, cost);
            return cost;
        }

        private static bool EvaluateBeds(ColonyState state)
        {
            var update = false;

            try
            {
                if (!TimeCycle.IsDay && Time.SecondsSinceStartDouble > _nextbedTime)
                {
                    if (state.ColonyRef.OwnerIsOnline())
                    {
                        // TODO Fix bed count
                        var remainingBeds = ServerManager.BlockEntityTracker.BedTracker.CalculateBedCount(state.ColonyRef) - state.ColonyRef.FollowerCount;
                        var left          = 0;

                        if (remainingBeds >= 0)
                        {
                            state.NeedsABed = 0;
                        }
                        else
                        {
                            if (state.NeedsABed == 0)
                            {
                                state.NeedsABed = Time.SecondsSinceStartDouble + LOABOROR_LEAVE_HOURS;
                                PandaChat.Send(state.ColonyRef, SettlerReasoning.GetNeedBed(), ChatColor.grey);
                            }

                            if (state.NeedsABed != 0 && state.NeedsABed < Time.SecondsSinceStartDouble)
                            {
                                foreach (var follower in state.ColonyRef.Followers)
                                    if (follower.UsedBed == null)
                                    {
                                        left++;
                                        NPCLeaving(follower);
                                    }

                                state.NeedsABed = 0;
                            }

                            if (left > 0)
                            {
                                PandaChat.Send(state.ColonyRef, string.Concat(SettlerReasoning.GetNoBed(), string.Format(" {0} colonists have left your colony.", left)), ChatColor.red);
                                update = true;
                            }
                        }

                        state.ColonyRef.SendUpdate();
                    }

                    _nextbedTime = Time.SecondsSinceStartDouble + Random.Next(5, 8) * IN_GAME_HOUR_IN_SECONDS * 24;
                }
            }
            catch (Exception ex)
            {
                PandaLogger.LogError(ex, "EvaluateBeds");
            }

            return update;
        }
    }
}