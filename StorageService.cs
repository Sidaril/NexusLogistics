using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;

namespace NexusLogistics
{
    public class RemoteStorageItem
    {
        public int count;
        public int inc;
        public int limit = 1000000;

        public void Export(BinaryWriter w)
        {
            w.Write(count);
            w.Write(inc);
            w.Write(limit);
        }

        public static RemoteStorageItem Import(BinaryReader r, int saveVersion)
        {
            var item = new RemoteStorageItem
            {
                count = r.ReadInt32(),
                inc = r.ReadInt32(),
                limit = 1000000
            };
            if (saveVersion >= 2)
            {
                item.limit = r.ReadInt32();
            }
            return item;
        }
    }

    public class MarketOrder
    {
        public int BuyThreshold;
        public int SellThreshold;

        public void Export(BinaryWriter w)
        {
            w.Write(BuyThreshold);
            w.Write(SellThreshold);
        }

        public static MarketOrder Import(BinaryReader r, int saveVersion)
        {
            var order = new MarketOrder
            {
                BuyThreshold = r.ReadInt32(),
                SellThreshold = r.ReadInt32()
            };
            return order;
        }
    }

    public class StorageService
    {
        private readonly ConcurrentDictionary<int, RemoteStorageItem> _remoteStorage = new ConcurrentDictionary<int, RemoteStorageItem>();
        private readonly ConcurrentDictionary<int, MarketOrder> _marketOrders = new ConcurrentDictionary<int, MarketOrder>();

        public int GetItemCount(int itemId)
        {
            return _remoteStorage.TryGetValue(itemId, out var item) ? item.count : 0;
        }

        public int GetItemLimit(int itemId)
        {
            return _remoteStorage.TryGetValue(itemId, out var item) ? item.limit : 1000000;
        }

        public void SetItemLimit(int itemId, int limit)
        {
            _remoteStorage.AddOrUpdate(itemId,
                (id) => new RemoteStorageItem { limit = limit },
                (id, existing) => { existing.limit = limit; return existing; });
        }

        public bool TryGetItem(int itemId, out RemoteStorageItem item)
        {
            return _remoteStorage.TryGetValue(itemId, out item);
        }

        public IEnumerable<KeyValuePair<int, RemoteStorageItem>> GetAllItems()
        {
            return _remoteStorage;
        }

        public void ClearStorage()
        {
            _remoteStorage.Clear();
        }

        public void AddOrUpdateStorage(int itemId, int count, int inc, int limit)
        {
            _remoteStorage.AddOrUpdate(itemId,
                (id) => new RemoteStorageItem { count = count, inc = inc, limit = limit },
                (id, existing) =>
                {
                    existing.count = count;
                    existing.inc = inc;
                    existing.limit = limit;
                    return existing;
                });
        }

        public void UpdateStock(int itemId, int deltaCount, int deltaInc)
        {
            var item = _remoteStorage.GetOrAdd(itemId, (id) => new RemoteStorageItem());
            Interlocked.Add(ref item.count, deltaCount);
            Interlocked.Add(ref item.inc, deltaInc);
        }

        // Market Orders
        public bool TryGetMarketOrder(int itemId, out MarketOrder order)
        {
            return _marketOrders.TryGetValue(itemId, out order);
        }

        public IEnumerable<KeyValuePair<int, MarketOrder>> GetAllMarketOrders()
        {
            return _marketOrders.ToArray();
        }

        public void SetMarketOrder(int itemId, int buyThreshold, int sellThreshold)
        {
            if (buyThreshold <= 0 && sellThreshold <= 0)
            {
                _marketOrders.TryRemove(itemId, out _);
            }
            else
            {
                _marketOrders.AddOrUpdate(itemId,
                    (id) => new MarketOrder { BuyThreshold = buyThreshold, SellThreshold = sellThreshold },
                    (id, existing) =>
                    {
                        existing.BuyThreshold = buyThreshold;
                        existing.SellThreshold = sellThreshold;
                        return existing;
                    });
            }
        }

        public void ClearMarketOrders()
        {
            _marketOrders.Clear();
        }

        public void Export(BinaryWriter w)
        {
            // Remote Storage
            var storageItems = _remoteStorage.ToArray();
            w.Write(storageItems.Length);
            foreach (var item in storageItems)
            {
                w.Write(item.Key);
                item.Value.Export(w);
            }

            // Market Orders
            var orders = _marketOrders.ToArray();
            w.Write(orders.Length);
            foreach (var order in orders)
            {
                w.Write(order.Key);
                order.Value.Export(w);
            }
        }

        public void Import(BinaryReader r, int saveVersion)
        {
            _remoteStorage.Clear();
            int storageCount = r.ReadInt32();
            for (int i = 0; i < storageCount; i++)
            {
                int itemId = r.ReadInt32();
                _remoteStorage[itemId] = RemoteStorageItem.Import(r, saveVersion);
            }

            if (saveVersion >= 5)
            {
                _marketOrders.Clear();
                int orderCount = r.ReadInt32();
                for (int i = 0; i < orderCount; i++)
                {
                    int itemId = r.ReadInt32();
                    _marketOrders[itemId] = MarketOrder.Import(r, saveVersion);
                }
            }
        }
    }
}
