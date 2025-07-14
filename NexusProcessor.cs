using System;
using System.Collections.Generic;
using System.Linq;
using BepInEx.Logging;
using HarmonyLib;

namespace NexusLogistics
{
    /// <summary>
    /// Contains all the core game-mutating logic for the mod.
    /// Each "Process" method handles a specific type of building or system.
    /// </summary>
    public class NexusProcessor
    {
        private readonly ManualLogSource _logger;
        private readonly Dictionary<int, int> _incPool = new Dictionary<int, int> { { 1141, 0 }, { 1142, 0 }, { 1143, 0 } };
        private readonly List<(int, int)> _proliferators = new List<(int, int)>
        {
            (ItemIds.ProliferatorMk3, 4),
            (ItemIds.ProliferatorMk2, 2),
            (ItemIds.ProliferatorMk1, 1)
        };
        private readonly Dictionary<EAmmoType, List<int>> _ammos = new Dictionary<EAmmoType, List<int>>
        {
            { EAmmoType.Bullet, new List<int> { 1603, 1602, 1601 } },
            { EAmmoType.Missile, new List<int> { 1611, 1610, 1609 } },
            { EAmmoType.Cannon, new List<int> { 1606, 1605, 1604 } },
            { EAmmoType.Plasma, new List<int> { 1608, 1607 } },
            { EAmmoType.EMCapsule, new List<int> { 1613, 1612 } }
        };

        public NexusProcessor(ManualLogSource logger)
        {
            _logger = logger;
        }

        /// <summary>
        /// Runs all processing tasks sequentially. This is the main entry point for the logic thread.
        /// </summary>
        public void RunAllTasks()
        {
            if (NexusLogistics.InfSand.Value && GameMain.mainPlayer.sandCount != 1000000000)
            {
                Traverse.Create(GameMain.mainPlayer).Property("sandCount").SetValue(1000000000);
            }

            // FIX: Tech checks are now run on the main thread to prevent crashes.
            // CheckTech(); 
            
            ProcessSpraying();
            ProcessDeliveryPackage();
            ProcessTransport();
            if (NexusLogistics.UseStorage.Value)
            {
                ProcessStorage();
            }
            ProcessMiner();
            ProcessAssembler();
            ProcessLab();
            ProcessPowerGenerator();
            ProcessPowerExchanger();
            ProcessSilo();
            ProcessEjector();
            ProcessTurret();
            ProcessBattleBase();
            if (NexusLogistics.AutoReplenishPackage.Value)
            {
                ProcessPackage();
            }
        }

        private bool IsVein(int itemId)
        {
            return LDB.veins.GetVeinTypeByItemId(itemId) != EVeinType.None || itemId == 1000 || itemId == 1116 || itemId == 1120 || itemId == 1121;
        }

        private bool IsItemInfinite(int itemId)
        {
            ItemProto item = LDB.items.Select(itemId);
            if (item == null) return false;

            return (NexusLogistics.InfItems.Value) ||
                   (NexusLogistics.InfVeins.Value && IsVein(itemId)) ||
                   (NexusLogistics.InfBuildings.Value && item.CanBuild) ||
                   (NexusLogistics.InfAmmo.Value && item.isAmmo) ||
                   (NexusLogistics.InfFleet.Value && item.isFighter);
        }

        private int[] TakeItem(int itemId, int count)
        {
            return NexusStorage.TakeItem(itemId, count, IsItemInfinite(itemId));
        }

        private int[] AddItem(int itemId, int count, int inc)
        {
            return NexusStorage.AddItem(itemId, count, inc);
        }

        // This method is now called from the main thread via NexusLogistics.Update()
        public void CheckTech()
        {
            var history = GameMain.history;
            var player = GameMain.mainPlayer;
            var deliveryPackage = player.deliveryPackage;
            
            if (history.TechUnlocked(1608))
            {
                if (history.TechUnlocked(2307) && deliveryPackage.colCount < 10)
                {
                    deliveryPackage.colCount = 10;
                    deliveryPackage.NotifySizeChange();
                }
                else if (history.TechUnlocked(2304) && deliveryPackage.colCount < 8)
                {
                    deliveryPackage.colCount = 8;
                    deliveryPackage.NotifySizeChange();
                }
            }
            else if (deliveryPackage.colCount < 6 || !deliveryPackage.unlocked)
            {
                if (!deliveryPackage.unlocked) deliveryPackage.unlocked = true;
                deliveryPackage.colCount = 6;
                deliveryPackage.NotifySizeChange();
            }

            int targetSize = 90;
            if (history.TechUnlocked(2301)) targetSize = 100;
            if (history.TechUnlocked(2302)) targetSize = 110;
            if (history.TechUnlocked(2303)) targetSize = 120;
            if (history.TechUnlocked(2304)) targetSize = 130;
            if (history.TechUnlocked(2305)) targetSize = 140;
            if (history.TechUnlocked(2306)) targetSize = 150;
            if (history.TechUnlocked(2307)) targetSize = 160;
            if (player.package.size < targetSize) player.package.SetSize(targetSize);

            if (history.TechUnlocked(3509)) history.remoteStationExtraStorage = 15000;
            if (history.TechUnlocked(3510)) history.remoteStationExtraStorage = 40000;
        }

        private void ProcessSpraying()
        {
            if (!NexusLogistics.AutoSpray.Value) return;

            var activeProliferators = new List<(int, int)>();
            switch (NexusLogistics.ProliferatorTier.Value)
            {
                case ProliferatorSelection.Mk1: activeProliferators.Add(_proliferators[2]); break;
                case ProliferatorSelection.Mk2: activeProliferators.Add(_proliferators[1]); break;
                case ProliferatorSelection.Mk3: activeProliferators.Add(_proliferators[0]); break;
                default: activeProliferators.AddRange(_proliferators); break;
            }

            if (NexusLogistics.CostProliferator.Value)
            {
                foreach (var proliferator in activeProliferators)
                {
                    int proliferatorId = proliferator.Item1;
                    int factor = 0;
                    if (proliferatorId == ItemIds.ProliferatorMk3) factor = 75;
                    else if (proliferatorId == ItemIds.ProliferatorMk2) factor = 30;
                    else if (proliferatorId == ItemIds.ProliferatorMk1) factor = 15;

                    if (factor > 0 && NexusStorage.TryGetItem(proliferatorId, out var pItem) && pItem.Count > 0)
                    {
                        _incPool[proliferatorId] += pItem.Count * factor;
                        pItem.Count = 0;
                    }
                }
            }

            foreach (var pair in NexusStorage.GetStorageEntries())
            {
                int itemId = pair.Key;
                var storageItem = pair.Value;
                if (itemId <= 0 || storageItem.Count <= 0) continue;

                if (itemId == ItemIds.ProliferatorMk1 || itemId == ItemIds.ProliferatorMk2 || itemId == ItemIds.ProliferatorMk3)
                {
                    storageItem.Inc = storageItem.Count * 4;
                    continue;
                }

                ItemProto itemProto = LDB.items.Select(itemId);
                if (itemProto.CanBuild || itemProto.isFighter) continue;

                if (!NexusLogistics.CostProliferator.Value)
                {
                    int maxSprayLevel = activeProliferators.Count > 0 ? activeProliferators.Max(p => p.Item2) : 4;
                    if (storageItem.Inc < storageItem.Count * maxSprayLevel) storageItem.Inc = storageItem.Count * maxSprayLevel;
                    continue;
                }

                foreach (var proliferator in activeProliferators)
                {
                    int sprayLevel = proliferator.Item2;
                    int proliferatorId = proliferator.Item1;
                    int expectedInc = storageItem.Count * sprayLevel - storageItem.Inc;
                    if (expectedInc <= 0) break;

                    int pointsToTake = Math.Min(expectedInc, _incPool[proliferatorId]);
                    if (pointsToTake > 0)
                    {
                        storageItem.Inc += pointsToTake;
                        _incPool[proliferatorId] -= pointsToTake;
                    }
                }
            }
        }

        private void ProcessDeliveryPackage()
        {
            var deliveryPackage = GameMain.mainPlayer.deliveryPackage;
            if (!deliveryPackage.unlocked) return;

            for (int i = 0; i < deliveryPackage.gridLength; i++)
            {
                var grid = deliveryPackage.grids[i];
                if (grid.itemId <= 0) continue;

                if (grid.requireCount > grid.count)
                {
                    int needCount = grid.requireCount - grid.count;
                    int[] result = TakeItem(grid.itemId, needCount);
                    if (result[0] > 0)
                    {
                        deliveryPackage.grids[i].count += result[0];
                        deliveryPackage.grids[i].inc += result[1];
                    }
                }
                else if (grid.recycleCount < grid.count)
                {
                    int supplyCount = grid.count - grid.recycleCount;
                    int[] result = AddItem(grid.itemId, supplyCount, 0);
                    if (result[0] > 0)
                    {
                        deliveryPackage.grids[i].count -= result[0];
                    }
                }
            }
        }

        private void ProcessTransport()
        {
            foreach (var pf in GameMain.data.factories)
            {
                if (pf == null) continue;
                foreach (StationComponent sc in pf.transport.stationPool)
                {
                    if (sc == null || sc.id <= 0 || sc.isCollector || sc.isVeinCollector) continue;

                    for (int i = 0; i < sc.storage.Length; i++)
                    {
                        StationStore ss = sc.storage[i];
                        if (ss.itemId <= 0) continue;

                        ELogisticStorage logic = sc.isStellar ? ss.remoteLogic : ss.localLogic;
                        if (logic == ELogisticStorage.Supply && ss.count > 0)
                        {
                            int[] result = AddItem(ss.itemId, ss.count, ss.inc);
                            sc.storage[i].count -= result[0];
                            sc.storage[i].inc -= result[1];
                        }
                        else if (logic == ELogisticStorage.Demand)
                        {
                            int order = sc.isStellar ? ss.remoteOrder : ss.localOrder;
                            int expectCount = ss.max - order - ss.count;
                            if (expectCount > 0)
                            {
                                int[] result = TakeItem(ss.itemId, expectCount);
                                sc.storage[i].count += result[0];
                                sc.storage[i].inc += result[1];
                            }
                        }
                    }
                }
            }
        }

        private void ProcessStorage()
        {
            foreach (var pf in GameMain.data.factories)
            {
                if (pf == null) continue;

                foreach (StorageComponent sc in pf.factoryStorage.storagePool)
                {
                    if (sc == null || sc.isEmpty) continue;
                    for (int i = 0; i < sc.grids.Length; i++)
                    {
                        StorageComponent.GRID grid = sc.grids[i];
                        if (grid.itemId <= 0 || grid.count <= 0) continue;
                        int[] result = AddItem(grid.itemId, grid.count, grid.inc);
                        if (result[0] > 0)
                        {
                            sc.grids[i].count -= result[0];
                            sc.grids[i].inc -= result[1];
                            if (sc.grids[i].count <= 0)
                            {
                                sc.grids[i].itemId = sc.grids[i].filter;
                            }
                        }
                    }
                    // FIX: Removed sc.NotifyStorageChange();
                }

                for (int i = 0; i < pf.factoryStorage.tankPool.Length; i++)
                {
                    TankComponent tc = pf.factoryStorage.tankPool[i];
                    if (tc.id == 0 || tc.fluidId == 0 || tc.fluidCount == 0) continue;
                    int[] result = AddItem(tc.fluidId, tc.fluidCount, tc.fluidInc);
                    if(result[0] > 0)
                    {
                        pf.factoryStorage.tankPool[i].fluidCount -= result[0];
                        pf.factoryStorage.tankPool[i].fluidInc -= result[1];
                        if (pf.factoryStorage.tankPool[i].fluidCount <= 0)
                        {
                            pf.factoryStorage.tankPool[i].fluidId = 0;
                            pf.factoryStorage.tankPool[i].fluidInc = 0;
                        }
                    }
                }
            }
        }

        private void ProcessAssembler()
        {
            foreach (var pf in GameMain.data.factories)
            {
                if (pf == null) continue;
                foreach (AssemblerComponent ac in pf.factorySystem.assemblerPool)
                {
                    if (ac.id <= 0 || ac.recipeId <= 0) continue;
                    for (int i = 0; i < ac.products.Length; i++)
                    {
                        if (ac.produced[i] > 0)
                            ac.produced[i] -= AddItem(ac.products[i], ac.produced[i], 0)[0];
                    }
                    for (int i = 0; i < ac.requires.Length; i++)
                    {
                        int expectCount = Math.Max(ac.requireCounts[i] * 5 - ac.served[i], 0);
                        if (expectCount > 0)
                        {
                            int[] result = TakeItem(ac.requires[i], expectCount);
                            ac.served[i] += result[0];
                            ac.incServed[i] += result[1];
                        }
                    }
                }
            }
        }

        private void ProcessMiner()
        {
            foreach (var pf in GameMain.data.factories)
            {
                if (pf == null) continue;

                for (int i = 0; i < pf.factorySystem.minerPool.Length; i++)
                {
                    MinerComponent mc = pf.factorySystem.minerPool[i];
                    if (mc.id <= 0 || mc.productId <= 0 || mc.productCount <= 0) continue;
                    int[] result = AddItem(mc.productId, mc.productCount, 0);
                    pf.factorySystem.minerPool[i].productCount -= result[0];
                }

                foreach (StationComponent sc in pf.transport.stationPool)
                {
                    if (sc == null || sc.id <= 0) continue;
                    if (sc.isStellar && sc.isCollector)
                    {
                        for (int i = 0; i < sc.storage.Length; i++)
                        {
                            StationStore ss = sc.storage[i];
                            if (ss.itemId > 0 && ss.count > 0 && ss.remoteLogic == ELogisticStorage.Supply)
                            {
                                int[] result = AddItem(ss.itemId, ss.count, 0);
                                sc.storage[i].count -= result[0];
                            }
                        }
                    }
                    else if (sc.isVeinCollector)
                    {
                        StationStore ss = sc.storage[0];
                        if (ss.itemId > 0 && ss.count > 0 && ss.localLogic == ELogisticStorage.Supply)
                        {
                            int[] result = AddItem(ss.itemId, ss.count, 0);
                            sc.storage[0].count -= result[0];
                        }
                    }
                }
            }
        }
        
        private void ProcessPowerGenerator()
        {
            foreach (var pf in GameMain.data.factories)
            {
                if (pf == null) continue;
                for (int i = 0; i < pf.powerSystem.genPool.Length; i++)
                {
                    PowerGeneratorComponent pgc = pf.powerSystem.genPool[i];
                    if (pgc.id <= 0) continue;
                    if (pgc.gamma) // Artificial Star
                    {
                        if (pgc.catalystPoint + pgc.catalystIncPoint < 3600)
                        {
                            int[] result = TakeItem(ItemIds.CriticalPhoton, 3);
                            if (result[0] > 0)
                            {
                                pf.powerSystem.genPool[i].catalystId = ItemIds.CriticalPhoton;
                                pf.powerSystem.genPool[i].catalystPoint += result[0] * 3600;
                                pf.powerSystem.genPool[i].catalystIncPoint += result[1] * 3600;
                            }
                        }
                        if (pgc.productId > 0 && pgc.productCount >= 1)
                        {
                            int[] result = AddItem(pgc.productId, (int)pgc.productCount, 0);
                            pf.powerSystem.genPool[i].productCount -= result[0];
                        }
                        continue;
                    }

                    int fuelId = 0;
                    switch (pgc.fuelMask)
                    {
                        case 1: if (NexusLogistics.AutoReplenishTPPFuel.Value) fuelId = GetThermalPowerPlantFuel(); break;
                        case 2: fuelId = ItemIds.DeuteronFuelRod; break;
                        case 4: fuelId = TakeItem(ItemIds.AntimatterRod, 1)[0] > 0 ? ItemIds.AntimatterRod : 1803; break;
                    }
                    if (fuelId == 0) continue;

                    if (fuelId != pgc.fuelId && pgc.fuelCount == 0)
                    {
                        int[] result = TakeItem(fuelId, 5);
                        pf.powerSystem.genPool[i].SetNewFuel(fuelId, (short)result[0], (short)result[1]);
                    }
                    else if (fuelId == pgc.fuelId && pgc.fuelCount < 5)
                    {
                        int[] result = TakeItem(fuelId, 5 - pgc.fuelCount);
                        pf.powerSystem.genPool[i].fuelCount += (short)result[0];
                        pf.powerSystem.genPool[i].fuelInc += (short)result[1];
                    }
                }
            }
        }

        private int GetThermalPowerPlantFuel()
        {
            int fuelConfig = NexusLogistics.FuelId.Value;
            if (fuelConfig != 0)
            {
                if (NexusStorage.TryGetItem(fuelConfig, out var item) && item.Count > 0) return fuelConfig;
            }
            
            if (TakeItem(1114, 1)[0] > 0) return 1114;
            if (TakeItem(1120, 1)[0] > 0) return 1120;
            if (TakeItem(1006, 1)[0] > 0) return 1006;

            return 0;
        }
        
        private void ProcessPowerExchanger()
        {
            foreach (var pf in GameMain.data.factories)
            {
                if (pf == null) continue;
                for (int i = 0; i < pf.powerSystem.excPool.Length; i++)
                {
                    PowerExchangerComponent pec = pf.powerSystem.excPool[i];
                    if (pec.id <= 0) continue;
                    if (pec.targetState == -1) // Discharge
                    {
                        if (pec.fullCount < 3)
                        {
                            int[] result = TakeItem(pec.fullId, 3 - pec.fullCount);
                            pf.powerSystem.excPool[i].fullCount += (short)result[0];
                        }
                        if (pec.emptyCount > 0)
                        {
                            int[] result = AddItem(pec.emptyId, pec.emptyCount, 0);
                            pf.powerSystem.excPool[i].emptyCount -= (short)result[0];
                        }
                    }
                    else if (pec.targetState == 1) // Charge
                    {
                        if (pec.emptyCount < 5)
                        {
                            int[] result = TakeItem(pec.emptyId, 5 - pec.emptyCount);
                            pf.powerSystem.excPool[i].emptyCount += (short)result[0];
                        }
                        if (pec.fullCount > 0)
                        {
                            int[] result = AddItem(pec.fullId, pec.fullCount, 0);
                            pf.powerSystem.excPool[i].fullCount -= (short)result[0];
                        }
                    }
                }
            }
        }

        private void ProcessSilo()
        {
            foreach (var pf in GameMain.data.factories)
            {
                if (pf == null) continue;
                for (int i = 0; i < pf.factorySystem.siloPool.Length; i++)
                {
                    SiloComponent sc = pf.factorySystem.siloPool[i];
                    if (sc.id > 0 && sc.bulletCount <= 3)
                    {
                        int[] result = TakeItem(sc.bulletId, 10);
                        pf.factorySystem.siloPool[i].bulletCount += result[0];
                        pf.factorySystem.siloPool[i].bulletInc += result[1];
                    }
                }
            }
        }

        private void ProcessEjector()
        {
            foreach (var pf in GameMain.data.factories)
            {
                if (pf == null) continue;
                for (int i = 0; i < pf.factorySystem.ejectorPool.Length; i++)
                {
                    EjectorComponent ec = pf.factorySystem.ejectorPool[i];
                    if (ec.id > 0 && ec.bulletCount <= 5)
                    {
                        int[] result = TakeItem(ec.bulletId, 15);
                        pf.factorySystem.ejectorPool[i].bulletCount += result[0];
                        pf.factorySystem.ejectorPool[i].bulletInc += result[1];
                    }
                }
            }
        }

        private void ProcessLab()
        {
            foreach (var pf in GameMain.data.factories)
            {
                if (pf == null) continue;
                foreach (LabComponent lc in pf.factorySystem.labPool)
                {
                    if (lc.id <= 0) continue;
                    if (lc.recipeId > 0)
                    {
                        for (int i = 0; i < lc.products.Length; i++)
                        {
                            if (lc.produced[i] > 0)
                            {
                                int[] result = AddItem(lc.products[i], lc.produced[i], 0);
                                lc.produced[i] -= result[0];
                            }
                        }
                        for (int i = 0; i < lc.requires.Length; i++)
                        {
                            int expectCount = lc.requireCounts[i] * 3 - lc.served[i] - lc.incServed[i];
                            if(expectCount > 0)
                            {
                                int[] result = TakeItem(lc.requires[i], expectCount);
                                lc.served[i] += result[0];
                                lc.incServed[i] += result[1];
                            }
                        }
                    }
                    else if (lc.researchMode)
                    {
                        for (int i = 0; i < lc.matrixPoints.Length; i++)
                        {
                            if (lc.matrixPoints[i] <= 0 || lc.matrixServed[i] >= lc.matrixPoints[i] * 3600) continue;
                            int[] result = TakeItem(LabComponent.matrixIds[i], lc.matrixPoints[i]);
                            lc.matrixServed[i] += result[0] * 3600;
                            lc.matrixIncServed[i] += result[1] * 3600;
                        }
                    }
                }
            }
        }

        private void ProcessTurret()
        {
            foreach (var pf in GameMain.data.factories)
            {
                if (pf == null) continue;
                for (int i = 0; i < pf.defenseSystem.turrets.buffer.Length; i++)
                {
                    TurretComponent tc = pf.defenseSystem.turrets.buffer[i];
                    if (tc.id == 0 || tc.type == ETurretType.Laser || tc.ammoType == EAmmoType.None || tc.itemCount > 0 || tc.bulletCount > 0) continue;
                    foreach (int itemId in _ammos[tc.ammoType])
                    {
                        int[] result = TakeItem(itemId, 50 - tc.itemCount);
                        if (result[0] != 0)
                        {
                            pf.defenseSystem.turrets.buffer[i].SetNewItem(itemId, (short)result[0], (short)result[1]);
                            break;
                        }
                    }
                }
            }
        }

        private void ProcessBattleBase()
        {
            int[] fighters = { 5103, 5102, 5101 };
            foreach (var pf in GameMain.data.factories)
            {
                if (pf == null) continue;
                for (int i = 0; i < pf.defenseSystem.battleBases.buffer.Length; i++)
                {
                    BattleBaseComponent bbc = pf.defenseSystem.battleBases.buffer[i];
                    if (bbc == null || bbc.combatModule == null) continue;

                    ModuleFleet fleet = bbc.combatModule.moduleFleets[0];
                    for (int j = 0; j < fleet.fighters.Length; j++)
                    {
                        if (fleet.fighters[j].count == 0)
                        {
                            foreach (int itemId in fighters)
                            {
                                int[] result = TakeItem(itemId, 1);
                                if (result[0] != 0)
                                {
                                    fleet.AddFighterToPort(j, itemId);
                                    break;
                                }
                            }
                        }
                    }

                    if (NexusLogistics.UseStorage.Value) continue;
                    StorageComponent sc = bbc.storage;
                    if (sc.isEmpty) continue;
                    for (int j = 0; j < sc.grids.Length; j++)
                    {
                        StorageComponent.GRID grid = sc.grids[j];
                        if (grid.itemId <= 0 || grid.count <= 0) continue;
                        int[] result = AddItem(grid.itemId, grid.count, grid.inc);
                        if (result[0] != 0)
                        {
                            sc.grids[j].count -= result[0];
                            sc.grids[j].inc -= result[1];
                            if (sc.grids[j].count <= 0)
                            {
                                sc.grids[j].itemId = sc.grids[j].filter;
                            }
                        }
                    }
                    // FIX: Removed sc.NotifyStorageChange();
                }
            }
        }

        public static void ClearBattleBase()
        {
            var bans = GameMain.data.trashSystem.enemyDropBans;
            foreach (var pf in GameMain.data.factories)
            {
                if (pf == null) continue;
                for (int i = 0; i < pf.defenseSystem.battleBases.buffer.Length; i++)
                {
                    BattleBaseComponent bbc = pf.defenseSystem.battleBases.buffer[i];
                    if (bbc == null || bbc.storage == null || bbc.storage.isEmpty) continue;
                    StorageComponent sc = bbc.storage;
                    for (int j = 0; j < sc.grids.Length; j++)
                    {
                        if (bans.Contains(sc.grids[j].itemId))
                        {
                            sc.grids[j].count = 0;
                            sc.grids[j].inc = 0;
                            sc.grids[j].itemId = sc.grids[j].filter;
                        }
                    }
                    // FIX: Removed sc.NotifyStorageChange();
                }
            }
        }

        private void ProcessPackage()
        {
            StorageComponent package = GameMain.mainPlayer.package;
            for (int i = 0; i < package.grids.Length; i++)
            {
                StorageComponent.GRID grid = package.grids[i];
                if (grid.filter != 0 && grid.count < grid.stackSize)
                {
                    int[] result = TakeItem(grid.itemId, grid.stackSize - grid.count);
                    if (result[0] != 0)
                    {
                        package.grids[i].count += result[0];
                        package.grids[i].inc += result[1];
                    }
                }
            }
            // FIX: Removed package.NotifyStorageChange();
        }
    }
}
