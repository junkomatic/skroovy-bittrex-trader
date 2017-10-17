using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BtrexTrader.Data.MarketData
{
    public class OrderBook : ICloneable
    {
        public string MarketDelta { get; private set; }
        public ConcurrentDictionary<Decimal, Decimal> Bids { get; private set; }
        public ConcurrentDictionary<Decimal, Decimal> Asks { get; private set; }


        public OrderBook(MarketQueryResponse snapshot)
        {
            MarketDelta = snapshot.MarketName;
            Bids = new ConcurrentDictionary<decimal, decimal>();
            Asks = new ConcurrentDictionary<decimal, decimal>();
            setAsksSnap(snapshot);
            setBidsSnap(snapshot);

        }
        

        private void setBidsSnap(MarketQueryResponse snap)
        {
            Bids.Clear();
            foreach (Buy bid in snap.Buys)
            {
                bool added = false;
                while (!added)
                {
                    added = Bids.TryAdd(bid.Rate, bid.Quantity);
                }
            }
        }

        private void setAsksSnap(MarketQueryResponse snap)
        {
            Asks.Clear();
            foreach (Sell ask in snap.Sells)
            {
                bool added = false;
                while (!added)
                {
                    added = Asks.TryAdd(ask.Rate, ask.Quantity);
                }
            }
        }

        public object Clone()
        {
            return this.MemberwiseClone();
        }
    }
}
