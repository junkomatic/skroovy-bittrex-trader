using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BtrexTrader.Data
{
    public class OrderBook : ICloneable
    {
        public string MarketDelta { get; private set; }
        public ConcurrentDictionary<Decimal, Decimal> Bids { get; private set; }
        public ConcurrentDictionary<Decimal, Decimal> Asks { get; private set; }
        public int Nounce { get; private set; }



        public OrderBook(MarketQueryResponse snapshot)
        {
            MarketDelta = snapshot.MarketName;
            Nounce = snapshot.Nounce;
            Bids = new ConcurrentDictionary<decimal, decimal>();
            Asks = new ConcurrentDictionary<decimal, decimal>();
            setAsksSnap(snapshot);
            setBidsSnap(snapshot);

        }

        public void SetUpdate(MarketDataUpdate update)
        {
            if (update.Buys.Count > 0)
            {
                foreach (mdBuy bid in update.Buys)
                {
                    switch (bid.Type)
                    {
                        case 0:
                            Bids[bid.Rate] = bid.Quantity;
                            break;
                        case 1:
                            bool removed = false;
                            while (!removed)
                            {
                                removed = Bids.TryRemove(bid.Rate, out decimal q);
                            }
                            break;
                        case 2:
                            Bids[bid.Rate] = bid.Quantity;
                            break;
                    }
                }
            }
            
            if (update.Sells.Count > 0)
            {
                foreach (mdSell ask in update.Sells)
                {
                    switch (ask.Type)
                    {
                        case 0:
                            Asks[ask.Rate] = ask.Quantity;
                            break;
                        case 1:
                            bool removed = false;
                            while (!removed)
                            {
                                removed = Asks.TryRemove(ask.Rate, out decimal q);
                            }
                            break;
                        case 2:
                            Asks[ask.Rate] = ask.Quantity;
                            break;
                    }
                }
            }

            Nounce++;

            //Console.WriteLine("Update Processed: {0}   ={1}", update.MarketName, update.Nounce);

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
