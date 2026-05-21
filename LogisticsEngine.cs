using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using UnityEngine;
using BepInEx.Logging;

namespace NexusLogistics
{
    /// <summary>
    /// High-performance logistics engine for NexusLogistics.
    /// Processes item movements, production, and consumption logic on the main thread.
    /// </summary>
    public class LogisticsEngine
    {
        private readonly StorageService _storageService;
        private readonly FactoryProvider _factoryProvider;
        private readonly ManualLogSource _logger;

        // Configuration
        public bool EnableMod { get; set; }
        public bool InfSand { get; set; }
        public bool UseStorage { get; set; }
        public bool AutoReplenishPackage { get; set; }
        public bool AutoCleanInventory { get; set; }
        public bool AutoSpray { get; set; }
        public bool CostProliferator { get; set; }
        public int ProliferatorSelectionValue { get; set; } // 0: All, 1: Mk1, 2: Mk2, 3: Mk3
        public bool InfItems { get; set; }
        public bool InfVeins { get; set; }
        public bool InfBuildings { get; set; }
        public bool InfAmmo { get; set; }
        public bool InfFleet { get; set; }
        public bool AutoReplenishTPPFuel { get; set; }
        public bool AutoReplenishFPPFuel { get; set; }
        public int FuelId { get; set; }
        public int StarFuelId { get; set; }

        // State
        private long _playerBalance;
        public long PlayerBalance 
        { 
            get => Interlocked.Read(ref _playerBalance);
            set => Interlocked.Exchange(ref _playerBalance, value);
        }

        public int TradeRoutesTier1 { get; set; }
        public int TradeRoutesTier2 { get; set; }
        public int TradeRoutesTier3 { get; set; }
        public HashSet<int> UnlockedItems { get; } = new HashSet<int>();

        private readonly List<int> _thermalFuelsByPriority = new List<int>
        {
            ItemIds.HydrogenFuelRod,
            ItemIds.EnergyShard,
            ItemIds.CombustionUnit,
            ItemIds.FireIce,
            ItemIds.Hydrogen,
            ItemIds.RefinedOil,
            ItemIds.CrudeOil,
            ItemIds.Graphite,
            ItemIds.Coal,
            ItemIds.Wood,
            ItemIds.PlantFuel
        };

        private readonly Dictionary<EAmmoType, List<int>> _ammos = new Dictionary<EAmmoType, List<int>>();

        private int _tickCounter = 0;
        private const int UpdateIntervalTicks = 60; // Run every 1 second at 60 FPS
        private float _tradeRouteIncomeTimer = 0f;
        private const float TradeRouteIncomeInterval = 1.0f;

        public LogisticsEngine(StorageService storageService, FactoryProvider factoryProvider, ManualLogSource logger)
        {
            _storageService = storageService;
            _factoryProvider = factoryProvider;
            _logger = logger;
            InitializeAmmoData();
        }

        private void InitializeAmmoData()
        {
            _ammos[EAmmoType.Bullet] = new List<int> { ItemIds.TitaniumBullet, ItemIds.SuperalloyBullet, ItemIds.GravitonBullet };
            _ammos[EAmmoType.Cannon] = new List<int> { ItemIds.ShellSet, ItemIds.HighExplosiveShellSet, ItemIds.CrystalShellSet };
            _ammos[EAmmoType.EMCapsule] = new List<int> { ItemIds.PlasmaCapsule, ItemIds.AntimatterCapsule, ItemIds.JammingCapsule, ItemIds.SuppressionCapsule };
            _ammos[EAmmoType.Missile] = new List<int> { ItemIds.SupersonicMissileSet, ItemIds.GravitonMissileSet };
        }

        /// <summary>
        /// Main entry point for the engine, called from the plugin's Update or FixedUpdate.
        /// </summary>
        public void OnUpdate()
        {
            if (GameMain.instance == null || GameMain.instance.isMenuDemo || GameMain.isPaused || !GameMain.isRunning || GameMain.data == null)
            {
                return;
            }

            _tickCounter++;
            if (_tickCounter < UpdateIntervalTicks)
            {
                return;
            }
            _tickCounter = 0;

            if (EnableMod)
            {
                ProcessLogic();
            }
        }

        private void ProcessLogic()
        {
            try
            {
                if (InfSand && GameMain.mainPlayer.sandCount != 1000000000)
                {
                    GameMain.mainPlayer.SetSandCount(1000000000, ESandSource.Other);
                }

                ProcessSpraying();
                ProcessDeliveryPackage();
                ProcessTransport();
                ProcessAssembler();
                ProcessMiner();
                ProcessPowerGenerator();
                ProcessPowerExchanger();
                ProcessSilo();
                ProcessEjector();
                ProcessLab();
                ProcessTurret();
                ProcessBattleBase();

                if (UseStorage) ProcessStorage();
                if (AutoReplenishPackage) ProcessPackage();
                if (AutoCleanInventory) ProcessInventoryToLogistics();

                ProcessMarketOrders();
                ProcessTradeRoutes();
            }
            catch (Exception ex)
            {
                _logger.LogError($"LogisticsEngine critical error: {ex}");
            }
        }

        #region Processing Methods

        private void ProcessSpraying()
        {
            if (!AutoSpray) return;

            // Snapshot storage for calculation
            var items = _storageService.GetAllItems().ToList();
            var storageSnapshot = items.ToDictionary(k => k.Key, v => (count: v.Value.count, inc: v.Value.inc));
            
            var activeProliferators = new List<(int id, int level)>();
            switch (ProliferatorSelectionValue)
            {
                case 1: activeProliferators.Add((ItemIds.ProliferatorMk1, 1)); break;
                case 2: activeProliferators.Add((ItemIds.ProliferatorMk2, 2)); break;
                case 3: activeProliferators.Add((ItemIds.ProliferatorMk3, 4)); break;
                default:
                    activeProliferators.Add((ItemIds.ProliferatorMk1, 1));
                    activeProliferators.Add((ItemIds.ProliferatorMk2, 2));
                    activeProliferators.Add((ItemIds.ProliferatorMk3, 4));
                    break;
            }

            foreach (var itemId in storageSnapshot.Keys.ToList())
            {
                var (count, inc) = storageSnapshot[itemId];
                if (itemId <= 0 || count <= 0) continue;

                if (itemId >= ItemIds.ProliferatorMk1 && itemId <= ItemIds.ProliferatorMk3)
                {
                    _storageService.UpdateStock(itemId, 0, count * 4 - inc);
                    continue;
                }

                ItemProto itemProto = LDB.items.Select(itemId);
                if (itemProto == null || itemProto.CanBuild || itemProto.isFighter) continue;

                if (!CostProliferator)
                {
                    int maxSprayLevel = activeProliferators.Count > 0 ? activeProliferators.Max(p => p.level) : 4;
                    if (inc < count * maxSprayLevel)
                    {
                        _storageService.UpdateStock(itemId, 0, count * maxSprayLevel - inc);
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
                if (deliveryPackage.grids[i].itemId <= 0) continue;

                if (deliveryPackage.grids[i].requireCount > deliveryPackage.grids[i].count)
                {
                    int needCount = deliveryPackage.grids[i].requireCount - deliveryPackage.grids[i].count;
                    int[] result = TakeItem(deliveryPackage.grids[i].itemId, needCount);
                    if (result[0] > 0)
                    {
                        deliveryPackage.grids[i].count += result[0];
                        deliveryPackage.grids[i].inc += result[1];
                    }
                }
                else if (deliveryPackage.grids[i].recycleCount < deliveryPackage.grids[i].count)
                {
                    int supplyCount = deliveryPackage.grids[i].count - deliveryPackage.grids[i].recycleCount;
                    int supplyInc = SplitInc(deliveryPackage.grids[i].count, deliveryPackage.grids[i].inc, supplyCount);
                    int[] result = AddItem(deliveryPackage.grids[i].itemId, supplyCount, supplyInc, true);
                    if (result[0] > 0)
                    {
                        deliveryPackage.grids[i].count -= result[0];
                        deliveryPackage.grids[i].inc -= result[1];
                    }
                }
            }
        }

        private void ProcessTransport()
        {
            var factories = _factoryProvider.GetFactories(out int factoryCount);
            for (int fIdx = 0; fIdx < factoryCount; fIdx++)
            {
                var pf = factories[fIdx];
                if (pf == null) continue;

                var stationPool = _factoryProvider.GetStationPool(pf, out int stationCursor);
                for (int i = 1; i < stationCursor; i++)
                {
                    if (stationPool[i].id <= 0 || stationPool[i].isCollector || stationPool[i].isVeinCollector) continue;

                    for (int j = 0; j < stationPool[i].storage.Length; j++)
                    {
                        int itemId = stationPool[i].storage[j].itemId;
                        if (itemId <= 0) continue;

                        var logic = stationPool[i].isStellar ? stationPool[i].storage[j].remoteLogic : stationPool[i].storage[j].localLogic;
                        var order = stationPool[i].isStellar ? stationPool[i].storage[j].remoteOrder : stationPool[i].storage[j].localOrder;

                        if (logic == ELogisticStorage.Supply && stationPool[i].storage[j].count > 0)
                        {
                            int[] result = AddItem(itemId, stationPool[i].storage[j].count, stationPool[i].storage[j].inc);
                            stationPool[i].storage[j].count -= result[0];
                            stationPool[i].storage[j].inc -= result[1];
                        }
                        else if (logic == ELogisticStorage.Demand)
                        {
                            int expectCount = stationPool[i].storage[j].max - order - stationPool[i].storage[j].count;
                            if (expectCount <= 0) continue;
                            int[] result = TakeItem(itemId, expectCount);
                            stationPool[i].storage[j].count += result[0];
                            stationPool[i].storage[j].inc += result[1];
                        }
                    }
                }
            }
        }

        private void ProcessStorage()
        {
            var factories = _factoryProvider.GetFactories(out int factoryCount);
            for (int fIdx = 0; fIdx < factoryCount; fIdx++)
            {
                var pf = factories[fIdx];
                if (pf == null) continue;

                var storagePool = _factoryProvider.GetStoragePool(pf, out int storageCursor);
                for (int i = 1; i < storageCursor; i++)
                {
                    if (storagePool[i] == null || storagePool[i].id <= 0 || storagePool[i].isEmpty) continue;
                    bool changed = false;
                    for (int j = 0; j < storagePool[i].grids.Length; j++)
                    {
                        if (storagePool[i].grids[j].itemId <= 0 || storagePool[i].grids[j].count <= 0) continue;
                        int[] result = AddItem(storagePool[i].grids[j].itemId, storagePool[i].grids[j].count, storagePool[i].grids[j].inc);
                        if (result[0] > 0)
                        {
                            storagePool[i].grids[j].count -= result[0];
                            storagePool[i].grids[j].inc -= result[1];
                            if (storagePool[i].grids[j].count <= 0) storagePool[i].grids[j].itemId = storagePool[i].grids[j].filter;
                            changed = true;
                        }
                    }
                    if (changed) storagePool[i].NotifyStorageChange();
                }

                var tankPool = _factoryProvider.GetTankPool(pf, out int tankCursor);
                for (int i = 1; i < tankCursor; i++)
                {
                    if (tankPool[i].id <= 0 || tankPool[i].fluidId <= 0 || tankPool[i].fluidCount <= 0) continue;
                    int[] result = AddItem(tankPool[i].fluidId, tankPool[i].fluidCount, tankPool[i].fluidInc);
                    tankPool[i].fluidCount -= result[0];
                    tankPool[i].fluidInc -= result[1];
                    if (tankPool[i].fluidCount <= 0)
                    {
                        tankPool[i].fluidId = 0;
                        tankPool[i].fluidInc = 0;
                    }
                }
            }
        }

        private void ProcessAssembler()
        {
            var factories = _factoryProvider.GetFactories(out int factoryCount);
            for (int fIdx = 0; fIdx < factoryCount; fIdx++)
            {
                var pf = factories[fIdx];
                if (pf == null) continue;

                var assemblerPool = _factoryProvider.GetAssemblerPool(pf, out int assemblerCursor);
                for (int i = 1; i < assemblerCursor; i++)
                {
                    if (assemblerPool[i].id <= 0 || assemblerPool[i].recipeId <= 0) continue;
                    
                    for (int j = 0; j < assemblerPool[i].recipeExecuteData.products.Length; j++)
                    {
                        if (assemblerPool[i].produced[j] > 0)
                        {
                            int[] result = AddItem(assemblerPool[i].recipeExecuteData.products[j], assemblerPool[i].produced[j], 0);
                            assemblerPool[i].produced[j] -= result[0];
                        }
                    }
                    for (int j = 0; j < assemblerPool[i].recipeExecuteData.requires.Length; j++)
                    {
                        int expectCount = Math.Max(assemblerPool[i].recipeExecuteData.requireCounts[j] * 5 - assemblerPool[i].served[j], 0);
                        if (expectCount > 0)
                        {
                            int[] result = TakeItem(assemblerPool[i].recipeExecuteData.requires[j], expectCount);
                            assemblerPool[i].served[j] += result[0];
                            assemblerPool[i].incServed[j] += result[1];
                        }
                    }
                }
            }
        }

        private void ProcessMiner()
        {
            var factories = _factoryProvider.GetFactories(out int factoryCount);
            for (int fIdx = 0; fIdx < factoryCount; fIdx++)
            {
                var pf = factories[fIdx];
                if (pf == null) continue;

                var minerPool = _factoryProvider.GetMinerPool(pf, out int minerCursor);
                for (int i = 1; i < minerCursor; i++)
                {
                    if (minerPool[i].id <= 0 || minerPool[i].productId <= 0 || minerPool[i].productCount <= 0) continue;
                    int[] result = AddItem(minerPool[i].productId, minerPool[i].productCount, 0);
                    minerPool[i].productCount -= result[0];
                }

                var stationPool = _factoryProvider.GetStationPool(pf, out int stationCursor);
                for (int i = 1; i < stationCursor; i++)
                {
                    if (stationPool[i].id <= 0) continue;
                    if (stationPool[i].isStellar && stationPool[i].isCollector)
                    {
                        for (int j = 0; j < stationPool[i].storage.Length; j++)
                        {
                            int itemId = stationPool[i].storage[j].itemId;
                            if (itemId <= 0 || stationPool[i].storage[j].count <= 0 || stationPool[i].storage[j].remoteLogic != ELogisticStorage.Supply) continue;
                            int[] result = AddItem(itemId, stationPool[i].storage[j].count, 0);
                            stationPool[i].storage[j].count -= result[0];
                        }
                    }
                    else if (stationPool[i].isVeinCollector)
                    {
                        int itemId = stationPool[i].storage[0].itemId;
                        if (itemId <= 0 || stationPool[i].storage[0].count <= 0 || stationPool[i].storage[0].localLogic != ELogisticStorage.Supply) continue;
                        int[] result = AddItem(itemId, stationPool[i].storage[0].count, 0);
                        stationPool[i].storage[0].count -= result[0];
                    }
                }
            }
        }

        private void ProcessPowerGenerator()
        {
            var factories = _factoryProvider.GetFactories(out int factoryCount);
            for (int fIdx = 0; fIdx < factoryCount; fIdx++)
            {
                var pf = factories[fIdx];
                if (pf == null) continue;

                var genPool = _factoryProvider.GetGeneratorPool(pf, out int genCursor);
                for (int i = 1; i < genCursor; i++)
                {
                    if (genPool[i].id <= 0) continue;

                    if (genPool[i].gamma)
                    {
                        if (genPool[i].catalystPoint + genPool[i].catalystIncPoint < 3600)
                        {
                            int[] result = TakeItem(ItemIds.CriticalPhoton, 3);
                            if (result[0] > 0)
                            {
                                genPool[i].catalystId = ItemIds.CriticalPhoton;
                                genPool[i].catalystPoint += result[0] * 3600;
                                genPool[i].catalystIncPoint += result[1] * 3600;
                            }
                        }
                        if (genPool[i].productId > 0 && genPool[i].productCount >= 1)
                        {
                            int[] result = AddItem(genPool[i].productId, (int)genPool[i].productCount, 0);
                            genPool[i].productCount -= result[0];
                        }
                        continue;
                    }

                    List<int> fuelPriority = null;
                    int fuelCapacity = 0;

                    switch (genPool[i].fuelMask)
                    {
                        case 1: // Thermal
                            if (AutoReplenishTPPFuel)
                            {
                                fuelPriority = _thermalFuelsByPriority;
                                fuelCapacity = 50;
                            }
                            break;
                        case 2: // Mini Fusion
                            if (AutoReplenishFPPFuel)
                            {
                                fuelPriority = new List<int> { ItemIds.DeuteronFuelRod };
                                fuelCapacity = 5;
                            }
                            break;
                        case 4: // Artificial Star
                            if (AutoReplenishFPPFuel)
                            {
                                fuelPriority = StarFuelId != 0 ? new List<int> { StarFuelId } : new List<int> { ItemIds.StrangeAnnihilationFuelRod, ItemIds.AntimatterFuelRod };
                                fuelCapacity = 5;
                            }
                            break;
                    }

                    if (fuelPriority != null && genPool[i].fuelCount == 0)
                    {
                        foreach (var fuelIdToTry in fuelPriority)
                        {
                            int[] result = TakeItem(fuelIdToTry, fuelCapacity);
                            if (result[0] > 0)
                            {
                                genPool[i].SetNewFuel(fuelIdToTry, (short)result[0], (short)result[1]);
                                break;
                            }
                        }
                    }
                    else if (fuelPriority != null && genPool[i].fuelId > 0 && genPool[i].fuelCount < fuelCapacity)
                    {
                        if (fuelPriority.Contains(genPool[i].fuelId))
                        {
                            int[] result = TakeItem(genPool[i].fuelId, fuelCapacity - genPool[i].fuelCount);
                            if (result[0] > 0)
                            {
                                genPool[i].fuelCount += (short)result[0];
                                genPool[i].fuelInc += (short)result[1];
                            }
                        }
                    }
                }
            }
        }

        private void ProcessPowerExchanger()
        {
            var factories = _factoryProvider.GetFactories(out int factoryCount);
            for (int fIdx = 0; fIdx < factoryCount; fIdx++)
            {
                var pf = factories[fIdx];
                if (pf == null) continue;

                var excPool = _factoryProvider.GetExchangerPool(pf, out int excCursor);
                for (int i = 1; i < excCursor; i++)
                {
                    if (excPool[i].id <= 0) continue;

                    if (excPool[i].targetState == -1) // Discharge
                    {
                        if (excPool[i].fullCount < 3)
                        {
                            int[] result = TakeItem(excPool[i].fullId, 3 - excPool[i].fullCount);
                            excPool[i].fullCount += (short)result[0];
                        }
                        if (excPool[i].emptyCount > 0)
                        {
                            int[] result = AddItem(excPool[i].emptyId, excPool[i].emptyCount, 0);
                            excPool[i].emptyCount -= (short)result[0];
                        }
                    }
                    else if (excPool[i].targetState == 1) // Charge
                    {
                        if (excPool[i].emptyCount < 5)
                        {
                            int[] result = TakeItem(excPool[i].emptyId, 5 - excPool[i].emptyCount);
                            excPool[i].emptyCount += (short)result[0];
                        }
                        if (excPool[i].fullCount > 0)
                        {
                            int[] result = AddItem(excPool[i].fullId, excPool[i].fullCount, 0);
                            excPool[i].fullCount -= (short)result[0];
                        }
                    }
                }
            }
        }

        private void ProcessSilo()
        {
            var factories = _factoryProvider.GetFactories(out int factoryCount);
            for (int fIdx = 0; fIdx < factoryCount; fIdx++)
            {
                var pf = factories[fIdx];
                if (pf == null) continue;

                var siloPool = _factoryProvider.GetSiloPool(pf, out int siloCursor);
                for (int i = 1; i < siloCursor; i++)
                {
                    if (siloPool[i].id > 0 && siloPool[i].bulletCount <= 3)
                    {
                        int[] result = TakeItem(siloPool[i].bulletId, 10);
                        siloPool[i].bulletCount += result[0];
                        siloPool[i].bulletInc += result[1];
                    }
                }
            }
        }

        private void ProcessEjector()
        {
            var factories = _factoryProvider.GetFactories(out int factoryCount);
            for (int fIdx = 0; fIdx < factoryCount; fIdx++)
            {
                var pf = factories[fIdx];
                if (pf == null) continue;

                var ejectorPool = _factoryProvider.GetEjectorPool(pf, out int ejectorCursor);
                for (int i = 1; i < ejectorCursor; i++)
                {
                    if (ejectorPool[i].id > 0 && ejectorPool[i].bulletCount <= 5)
                    {
                        int[] result = TakeItem(ejectorPool[i].bulletId, 15);
                        ejectorPool[i].bulletCount += result[0];
                        ejectorPool[i].bulletInc += result[1];
                    }
                }
            }
        }

        private void ProcessLab()
        {
            var factories = _factoryProvider.GetFactories(out int factoryCount);
            for (int fIdx = 0; fIdx < factoryCount; fIdx++)
            {
                var pf = factories[fIdx];
                if (pf == null) continue;

                var labPool = _factoryProvider.GetLabPool(pf, out int labCursor);
                for (int i = 1; i < labCursor; i++)
                {
                    if (labPool[i].id <= 0) continue;
                    if (labPool[i].recipeId > 0)
                    {
                        for (int j = 0; j < labPool[i].recipeExecuteData.products.Length; j++)
                        {
                            if (labPool[i].produced[j] > 0)
                            {
                                int[] result = AddItem(labPool[i].recipeExecuteData.products[j], labPool[i].produced[j], 0);
                                labPool[i].produced[j] -= result[0];
                            }
                        }
                        for (int j = 0; j < labPool[i].recipeExecuteData.requires.Length; j++)
                        {
                            int expectCount = labPool[i].recipeExecuteData.requireCounts[j] * 3 - labPool[i].served[j] - labPool[i].incServed[j];
                            if (expectCount > 0)
                            {
                                int[] result = TakeItem(labPool[i].recipeExecuteData.requires[j], expectCount);
                                labPool[i].served[j] += result[0];
                                labPool[i].incServed[j] += result[1];
                            }
                        }
                    }
                    else if (labPool[i].researchMode)
                    {
                        for (int j = 0; j < LabComponent.matrixPoints.Length; j++)
                        {
                            if (LabComponent.matrixPoints[j] <= 0 || labPool[i].matrixServed[j] >= LabComponent.matrixPoints[j] * 3600) continue;
                            int[] result = TakeItem(LabComponent.matrixIds[j], LabComponent.matrixPoints[j]);
                            labPool[i].matrixServed[j] += result[0] * 3600;
                            labPool[i].matrixIncServed[j] += result[1] * 3600;
                        }
                    }
                }
            }
        }

        private void ProcessTurret()
        {
            var factories = _factoryProvider.GetFactories(out int factoryCount);
            for (int fIdx = 0; fIdx < factoryCount; fIdx++)
            {
                var pf = factories[fIdx];
                if (pf == null) continue;

                var turretPool = _factoryProvider.GetTurretPool(pf, out int turretCursor);
                for (int i = 1; i < turretCursor; i++)
                {
                    if (turretPool[i].id == 0 || turretPool[i].type == ETurretType.Laser || turretPool[i].ammoType == EAmmoType.None || turretPool[i].itemCount > 0 || turretPool[i].bulletCount > 0) continue;
                    if (_ammos.TryGetValue(turretPool[i].ammoType, out var ammoIds))
                    {
                        foreach (int itemId in ammoIds)
                        {
                            int[] result = TakeItem(itemId, 50 - turretPool[i].itemCount);
                            if (result[0] != 0)
                            {
                                turretPool[i].SetNewItem(itemId, (short)result[0], (short)result[1]);
                                break;
                            }
                        }
                    }
                }
            }
        }

        private void ProcessBattleBase()
        {
            int[] fighters = { ItemIds.Destroyer, ItemIds.Corvette, ItemIds.AttackDrone };
            var factories = _factoryProvider.GetFactories(out int factoryCount);
            for (int fIdx = 0; fIdx < factoryCount; fIdx++)
            {
                var pf = factories[fIdx];
                if (pf == null) continue;

                var battleBasePool = _factoryProvider.GetBattleBasePool(pf, out int battleBaseCursor);
                for (int i = 1; i < battleBaseCursor; i++)
                {
                    if (battleBasePool[i] == null || battleBasePool[i].id <= 0 || battleBasePool[i].combatModule == null) continue;

                    ModuleFleet fleet = battleBasePool[i].combatModule.moduleFleets[0];
                    for (int fIndex = 0; fIndex < fleet.fighters.Length; fIndex++)
                    {
                        if (fleet.fighters[fIndex].count == 0)
                        {
                            foreach (int itemId in fighters)
                            {
                                if (TakeItem(itemId, 1)[0] != 0)
                                {
                                    fleet.AddFighterToPort(fIndex, itemId);
                                    break;
                                }
                            }
                        }
                    }

                    if (UseStorage) continue;
                    StorageComponent sc = battleBasePool[i].storage;
                    if (sc == null || sc.isEmpty) continue;
                    bool changed = false;
                    for (int gIndex = 0; gIndex < sc.grids.Length; gIndex++)
                    {
                        if (sc.grids[gIndex].itemId <= 0 || sc.grids[gIndex].count <= 0) continue;
                        int[] result = AddItem(sc.grids[gIndex].itemId, sc.grids[gIndex].count, sc.grids[gIndex].inc);
                        if (result[0] > 0)
                        {
                            sc.grids[gIndex].count -= result[0];
                            sc.grids[gIndex].inc -= result[1];
                            if (sc.grids[gIndex].count <= 0) sc.grids[gIndex].itemId = sc.grids[gIndex].filter;
                            changed = true;
                        }
                    }
                    if (changed) sc.NotifyStorageChange();
                }
            }
        }

        private void ProcessMarketOrders()
        {
            var orders = _storageService.GetAllMarketOrders().ToList();
            if (orders.Count == 0) return;

            foreach (var pair in orders)
            {
                int itemId = pair.Key;
                var order = pair.Value;

                int currentStock = _storageService.GetItemCount(itemId);

                // Sell Logic
                if (order.SellThreshold > 0 && currentStock > order.SellThreshold)
                {
                    int amountToSell = currentStock - order.SellThreshold;
                    int[] soldItems = TakeItem(itemId, amountToSell);
                    if (soldItems[0] > 0)
                    {
                        Interlocked.Add(ref _playerBalance, soldItems[0] * 100); // Placeholder price
                    }
                }

                // Buy Logic
                if (order.BuyThreshold > 0 && currentStock < order.BuyThreshold)
                {
                    int amountToBuy = order.BuyThreshold - currentStock;
                    if (amountToBuy > 0)
                    {
                        long cost = amountToBuy * 150; // Placeholder price
                        if (PlayerBalance >= cost)
                        {
                            Interlocked.Add(ref _playerBalance, -cost);
                            AddItem(itemId, amountToBuy, 0, true);
                        }
                    }
                }
            }
        }

        private void ProcessTradeRoutes()
        {
            _tradeRouteIncomeTimer += 1.0f; 
            if (_tradeRouteIncomeTimer >= TradeRouteIncomeInterval)
            {
                _tradeRouteIncomeTimer = 0;
                long income = (TradeRoutesTier1 * 1000L) + (TradeRoutesTier2 * 12500L) + (TradeRoutesTier3 * 150000L);
                if (income > 0)
                {
                    PlayerBalance += income;
                }
            }
        }

        private void ProcessPackage()
        {
            bool changed = false;
            StorageComponent package = GameMain.mainPlayer.package;
            for (int i = 0; i < package.grids.Length; i++)
            {
                if (package.grids[i].filter != 0 && package.grids[i].count < package.grids[i].stackSize)
                {
                    int[] result = TakeItem(package.grids[i].itemId, package.grids[i].stackSize - package.grids[i].count);
                    if (result[0] != 0)
                    {
                        package.grids[i].count += result[0];
                        package.grids[i].inc += result[1];
                        changed = true;
                    }
                }
            }
            if (changed) package.NotifyStorageChange();
        }

        private void ProcessInventoryToLogistics()
        {
            var player = GameMain.mainPlayer;
            if (player == null) return;

            var mainInventory = player.package;
            var logisticsInventory = player.deliveryPackage;
            if (!logisticsInventory.unlocked) return;

            bool changed = false;
            for (int i = 0; i < mainInventory.size; i++)
            {
                int itemId = mainInventory.grids[i].itemId;
                if (itemId <= 0 || mainInventory.grids[i].count <= 0) continue;

                for (int j = 0; j < logisticsInventory.gridLength; j++)
                {
                    if (logisticsInventory.grids[j].itemId == itemId)
                    {
                        int amountToMove = mainInventory.grids[i].count;
                        logisticsInventory.grids[j].count += amountToMove;
                        logisticsInventory.grids[j].inc += mainInventory.grids[i].inc;
                        mainInventory.TakeItem(itemId, amountToMove, out _);
                        changed = true;
                        break;
                    }
                }
            }
            if (changed) mainInventory.NotifyStorageChange();
        }

        #endregion

        #region Helpers

        public int[] AddItem(int itemId, int count, int inc, bool bypassLimit = false)
        {
            if (itemId <= 0 || count <= 0) return new int[] { 0, 0 };

            int limit = _storageService.GetItemLimit(itemId);
            int currentCount = _storageService.GetItemCount(itemId);
            int amountToAdd = count;

            if (!bypassLimit)
            {
                int spaceAvailable = limit - currentCount;
                if (spaceAvailable <= 0) return new int[] { 0, 0 };
                amountToAdd = Math.Min(count, spaceAvailable);
            }

            if (amountToAdd <= 0) return new int[] { 0, 0 };

            int incToAdd = SplitInc(count, inc, amountToAdd);
            _storageService.UpdateStock(itemId, amountToAdd, incToAdd);

            return new int[] { amountToAdd, incToAdd };
        }

        public int[] TakeItem(int itemId, int count)
        {
            ItemProto item = LDB.items.Select(itemId);
            if (item == null) return new int[] { 0, 0 };

            bool isInfinite = InfItems ||
                              (InfVeins && IsVein(itemId)) ||
                              (InfBuildings && item.CanBuild) ||
                              (InfAmmo && item.isAmmo) ||
                              (InfFleet && item.isFighter);

            if (isInfinite)
            {
                int inc = (AutoSpray && !CostProliferator && !item.CanBuild && !item.isFighter) ? count * 4 : 0;
                return new int[] { count, inc };
            }

            if (_storageService.TryGetItem(itemId, out var storageItem))
            {
                int availableCount = storageItem.count;
                if (availableCount > 0)
                {
                    int takenCount = Math.Min(count, availableCount);
                    int takenInc = SplitInc(availableCount, storageItem.inc, takenCount);
                    _storageService.UpdateStock(itemId, -takenCount, -takenInc);
                    return new int[] { takenCount, takenInc };
                }
            }
            return new int[] { 0, 0 };
        }

        private int SplitInc(int totalCount, int totalInc, int splitCount)
        {
            if (totalCount <= 0 || splitCount <= 0) return 0;
            if (splitCount >= totalCount) return totalInc;
            return (int)((long)totalInc * splitCount / totalCount);
        }

        private bool IsVein(int itemId)
        {
            int[] items = { ItemIds.Water, ItemIds.SulfuricAcid, ItemIds.Hydrogen, ItemIds.Deuterium };
            return items.Contains(itemId) || LDB.veins.GetVeinTypeByItemId(itemId) != EVeinType.None;
        }

        #endregion
    }
}
