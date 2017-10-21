using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Trady.Core;
using BtrexTrader.Interface;
using BtrexTrader.Data;
using BtrexTrader.Data.Market;

namespace BtrexTrader.Strategy.Demo
{
    class NewStratControl
    {
        private ConcurrentDictionary<string, Market> mData = new ConcurrentDictionary<string, Market>();
        private Dictionary<string, List<Candle>> mCandles = new Dictionary<string, List<Candle>>();

        private IReadOnlyList<string> SpecificDeltas = new List<string>()
        {
            "BTC-ETH", "BTC-NEO", "BTC-XLM", "BTC-QTUM", "BTC-OMG"
        };

        public async Task Initialize()
        {
            await SubTopMarketsByVol(10);
            //await SubSpecificMarkets();
        }

        public async Task StartDemoMarkets()
        {
            //Every iteration, check LastCandleTime to check if refresh mCandles





        }


        private async Task SubTopMarketsByVol(int n)
        {
            List<string> topMarkets = await BtrexREST.TradeMethods.GetTopMarketsByBVbtcOnly(n);
            foreach (string mk in topMarkets)
            {
                await BtrexWS.subscribeMarket("BTC-" + mk);
            }
            //Create SHALLOW COPY (CLONE) of ConDict
            mData = BtrexData.Markets;
            await PreloadCandles(3);
        }

        private async Task SubSpecificMarkets()
        {
            foreach (string mk in SpecificDeltas)
            {
                await BtrexWS.subscribeMarket("BTC-" + mk);
            }
            //Create SHALLOW COPY (CLONE) of ConDict
            mData = BtrexData.Markets;
            await PreloadCandles(3);
        }

        private async Task PreloadCandles(int hour)
        {
            //TODO: Import (3) hours of canldes into memory for each market.
            //   Aggregate in mCandles Dict
            DateTime startTime = DateTime.UtcNow.Subtract(TimeSpan.FromHours(hour));
            foreach (Market market in mData.Values)
            {
                var importer = new CandleImporter();
                var pre = await importer.ImportAsync(market.MarketDelta, startTime);
                List<Candle> preCandles = new List<Candle>(pre);
                mCandles.Add(market.MarketDelta, preCandles);
            }

        }

    }
}
