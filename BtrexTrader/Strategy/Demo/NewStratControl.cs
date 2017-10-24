using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Trady.Core;
using BtrexTrader.Interface;
using BtrexTrader.Data;
using BtrexTrader.Data.Market;

namespace BtrexTrader.Strategy.Demo
{
    class NewStratControl
    {
        private Dictionary<string, List<Candle>> mCandles = new Dictionary<string, List<Candle>>();

        private IReadOnlyList<string> SpecificDeltas = new List<string>()
        {
            "BTC-XLM", "BTC-NEO"//, "BTC-ETH", "BTC-QTUM", "BTC-OMG"
        };


        public async Task Initialize()
        {
            //await SubTopMarketsByVol(10);
            await SubSpecificMarkets();
        }

        public void StartMarketsDemo()
        {
            //Display All Data, switch between markets with spacebar:
            while (true)
            {
                foreach (Market m in BtrexData.Markets.Values)
                {
                    while (!(Console.KeyAvailable && Console.ReadKey(true).Key == ConsoleKey.Spacebar))
                    {
                        //TODOS: 
                        //Every iteration, check BtrexData.Market.LastCandleTime to check if add new mCandles:


                        Console.Clear();
                        //Print MarketTitle


                        //Print Candles:
                        foreach (Candle c in mCandles[m.MarketDelta])
                            Console.WriteLine("candle");         
                        

                        //Print demo EMA calcs:


                        //Print Bids/Asks:

                        
                        //Print Recent Fills:


                        //Print [active market] ribbon:
                        Console.Write("\r\nMarkets currently being tracked:\r\n    ");
                        foreach (string mk in mCandles.Keys)
                        {
                            if (m.MarketDelta == mk)
                            {
                                Console.ForegroundColor = ConsoleColor.Green;
                                Console.Write(" [{0}]", mk);
                            }
                            else
                            {
                                Console.ForegroundColor = ConsoleColor.White;
                                Console.Write(" [{0}]", mk);
                            }
                            
                            if (mk != mCandles.Keys.Last())
                                Console.Write(",");
                        }
                        Console.WriteLine();
                        Console.ForegroundColor = ConsoleColor.DarkCyan;
                        Thread.Sleep(1000);
                    }
                }
            }






        }


        private async Task SubTopMarketsByVol(int n)
        {
            List<string> topMarkets = await BtrexREST.TradeMethods.GetTopMarketsByBVbtcOnly(n);
            foreach (string mk in topMarkets)
                await BtrexWS.subscribeMarket("BTC-" + mk);

            await PreloadCandlesDict(3);
        }


        private async Task SubSpecificMarkets()
        {
            foreach (string mk in SpecificDeltas)
                await BtrexWS.subscribeMarket(mk);

            await PreloadCandlesDict(3);
        }


        private async Task PreloadCandlesDict(int hour)
        {
            //Aggregate in mCandles Dict:
            DateTime startTime = DateTime.UtcNow.Subtract(TimeSpan.FromHours(hour));
            foreach (Market market in BtrexData.Markets.Values)
            {
                var importer = new TradyCandleImporter();
                var preCandles = await importer.ImportAsync(market.MarketDelta, startTime);
                mCandles.Add(market.MarketDelta, new List<Candle>(preCandles));
            }
        }

    }


}
