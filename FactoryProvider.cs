using System.Collections.Generic;

namespace NexusLogistics
{
    /// <summary>
    /// Provides optimized access to game factory data, minimizing GC pressure.
    /// </summary>
    public class FactoryProvider
    {
        public PlanetFactory[] GetFactories(out int count)
        {
            if (GameMain.data == null)
            {
                count = 0;
                return null;
            }
            count = GameMain.data.factoryCount;
            return GameMain.data.factories;
        }

        public StationComponent[] GetStationPool(PlanetFactory factory, out int cursor)
        {
            if (factory?.transport == null)
            {
                cursor = 0;
                return null;
            }
            cursor = factory.transport.stationCursor;
            return factory.transport.stationPool;
        }

        public AssemblerComponent[] GetAssemblerPool(PlanetFactory factory, out int cursor)
        {
            if (factory?.factorySystem == null)
            {
                cursor = 0;
                return null;
            }
            cursor = factory.factorySystem.assemblerCursor;
            return factory.factorySystem.assemblerPool;
        }

        public MinerComponent[] GetMinerPool(PlanetFactory factory, out int cursor)
        {
            if (factory?.factorySystem == null)
            {
                cursor = 0;
                return null;
            }
            cursor = factory.factorySystem.minerCursor;
            return factory.factorySystem.minerPool;
        }

        public LabComponent[] GetLabPool(PlanetFactory factory, out int cursor)
        {
            if (factory?.factorySystem == null)
            {
                cursor = 0;
                return null;
            }
            cursor = factory.factorySystem.labCursor;
            return factory.factorySystem.labPool;
        }

        public PowerGeneratorComponent[] GetGeneratorPool(PlanetFactory factory, out int cursor)
        {
            if (factory?.powerSystem == null)
            {
                cursor = 0;
                return null;
            }
            cursor = factory.powerSystem.genCursor;
            return factory.powerSystem.genPool;
        }

        public PowerExchangerComponent[] GetExchangerPool(PlanetFactory factory, out int cursor)
        {
            if (factory?.powerSystem == null)
            {
                cursor = 0;
                return null;
            }
            cursor = factory.powerSystem.excCursor;
            return factory.powerSystem.excPool;
        }

        public SiloComponent[] GetSiloPool(PlanetFactory factory, out int cursor)
        {
            if (factory?.factorySystem == null)
            {
                cursor = 0;
                return null;
            }
            cursor = factory.factorySystem.siloCursor;
            return factory.factorySystem.siloPool;
        }

        public EjectorComponent[] GetEjectorPool(PlanetFactory factory, out int cursor)
        {
            if (factory?.factorySystem == null)
            {
                cursor = 0;
                return null;
            }
            cursor = factory.factorySystem.ejectorCursor;
            return factory.factorySystem.ejectorPool;
        }

        public StorageComponent[] GetStoragePool(PlanetFactory factory, out int cursor)
        {
            if (factory?.factoryStorage == null)
            {
                cursor = 0;
                return null;
            }
            cursor = factory.factoryStorage.storageCursor;
            return factory.factoryStorage.storagePool;
        }

        public TankComponent[] GetTankPool(PlanetFactory factory, out int cursor)
        {
            if (factory?.factoryStorage == null)
            {
                cursor = 0;
                return null;
            }
            cursor = factory.factoryStorage.tankCursor;
            return factory.factoryStorage.tankPool;
        }

        public TurretComponent[] GetTurretPool(PlanetFactory factory, out int cursor)
        {
            if (factory?.defenseSystem?.turrets == null)
            {
                cursor = 0;
                return null;
            }
            cursor = factory.defenseSystem.turrets.cursor;
            return factory.defenseSystem.turrets.buffer;
        }

        public BattleBaseComponent[] GetBattleBasePool(PlanetFactory factory, out int cursor)
        {
            if (factory?.defenseSystem?.battleBases == null)
            {
                cursor = 0;
                return null;
            }
            cursor = factory.defenseSystem.battleBases.cursor;
            return factory.defenseSystem.battleBases.buffer;
        }
    }
}
