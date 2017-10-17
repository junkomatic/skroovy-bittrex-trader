using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BtrexTrader.Data.MarketData
{
    public class Market
    {
        public string MarketDelta { get; private set; }
        public OrderBook OrderBook { get; private set; }
        public TradeHistory TradeHistory { get; private set; }
        public int Nounce { get; private set; }

        public Market(MarketQueryResponse snapshot)
        {
            MarketDelta = snapshot.MarketName;
            Nounce = snapshot.Nounce;
            OrderBook = new OrderBook(snapshot);
            TradeHistory = new TradeHistory(snapshot);
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
                            OrderBook.Bids[bid.Rate] = bid.Quantity;
                            break;
                        case 1:
                            bool removed = false;
                            while (!removed)
                            {
                                removed = OrderBook.Bids.TryRemove(bid.Rate, out decimal q);
                            }
                            break;
                        case 2:
                            OrderBook.Bids[bid.Rate] = bid.Quantity;
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
                            OrderBook.Asks[ask.Rate] = ask.Quantity;
                            break;
                        case 1:
                            bool removed = false;
                            while (!removed)
                            {
                                removed = OrderBook.Asks.TryRemove(ask.Rate, out decimal q);
                            }
                            break;
                        case 2:
                            OrderBook.Asks[ask.Rate] = ask.Quantity;
                            break;
                    }
                }
            }

            if (update.Fills.Count > 0)
            {
                update.Fills.Reverse();
                foreach (mdFill fill in update.Fills)
                    TradeHistory.RecentFills.Add(fill);
            }


            Nounce++;

        }

    }
}
