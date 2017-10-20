using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Trady.Core;
using BtrexTrader.Data;
using BtrexTrader.Data.MarketData;

namespace BtrexTrader.Strategy.Demo
{
    class NewStratData
    { 
        public string coinName { get; private set; }
        public ConcurrentDictionary<Decimal, Decimal> Bids { get; private set; }
        public ConcurrentDictionary<Decimal, Decimal> Asks { get; private set; }
        public List<mdFill> RecentFills { get; private set; }
        public List<HistDataLine> RecentCandleHist { get; private set; }


        public NewStratData(OrderBook ob, TradeHistory hist)
        {
            coinName = ob.MarketDelta.Split('-')[1];
            Bids = ob.Bids;
            Asks = ob.Asks;
            RecentFills = hist.RecentFills;
            RecentCandleHist = hist.Candles5m;
        }

        public async Task<IReadOnlyList<Candle>> GetCandles(DateTime start)
        {
            var importer = new CandleImporter();
            IReadOnlyList<Candle> candles = await importer.ImportAsync(coinName, start);

            return candles;
        }



    }
}
