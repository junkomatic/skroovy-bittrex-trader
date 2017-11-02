using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Trady.Core;
using Trady.Analysis;
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
            "BTC-XLM", "BTC-ADA"//, "BTC-ETH", "BTC-QTUM", "BTC-OMG"
        };

        int showPastPeriods = 36;

        public async Task Initialize()
        {
            await SubTopMarketsByVol(50);
            //await SubSpecificMarkets();
        }

        public async Task StartMarketsDemo()
        {
            Console.WindowHeight = 100;
            //Display All Data, switch between markets with spacebar:
            while (true)
            {
                foreach (Market m in BtrexData.Markets.Values)
                {
                    IEnumerable<Candle> RecentCandles = mCandles[m.MarketDelta].Skip(Math.Max(0, mCandles[m.MarketDelta].Count - showPastPeriods));

                    //PRECALC EMAS HERE
                    var closes = mCandles[m.MarketDelta].Select(x => x.Close);
                    var EMA26 = closes.Ema(26).Skip(Math.Max(0, mCandles[m.MarketDelta].Count - showPastPeriods)).ToArray();
                    var EMA12 = closes.Ema(12).Skip(Math.Max(0, mCandles[m.MarketDelta].Count - showPastPeriods)).ToArray();
                    var EMA9 = closes.Ema(9).Skip(Math.Max(0, mCandles[m.MarketDelta].Count - showPastPeriods)).ToArray();

                    while (!(Console.KeyAvailable && Console.ReadKey(true).Key == ConsoleKey.Spacebar))
                    {
                        //Every iteration, check BtrexData.Market.LastCandleTime to check if add new mCandles:
                        if (m.TradeHistory.LastStoredCandle > mCandles[m.MarketDelta].Last().DateTime)
                        {
                            var Importer = new TradyCandleImporter();
                            foreach (var c in mCandles)
                            {
                                var newCandles = await Importer.ImportAsync(c.Key, mCandles[m.MarketDelta].Last().DateTime.AddMinutes(5));
                                c.Value.AddRange(newCandles);
                            }

                            RecentCandles = mCandles[m.MarketDelta].Skip(Math.Max(0, mCandles[m.MarketDelta].Count - showPastPeriods));
                            closes = mCandles[m.MarketDelta].Select(x => x.Close);
                            EMA26 = closes.Ema(26).Skip(Math.Max(0, mCandles[m.MarketDelta].Count - showPastPeriods)).ToArray();
                            EMA12 = closes.Ema(12).Skip(Math.Max(0, mCandles[m.MarketDelta].Count - showPastPeriods)).ToArray();
                            EMA9 = closes.Ema(9).Skip(Math.Max(0, mCandles[m.MarketDelta].Count - showPastPeriods)).ToArray();
                        }

                        //Assign Orderbook before Console.Clear()
                        List<KeyValuePair<decimal, decimal>> bidsTop10 = m.OrderBook.Bids.ToArray().OrderByDescending(k => k.Key).Take(10).ToList();
                        List<KeyValuePair<decimal, decimal>> asksTop10 = m.OrderBook.Asks.ToArray().OrderBy(k => k.Key).Take(10).ToList();


                        Console.Clear();
                        //Print MarketTitle
                        Console.Write("\r\n------------------------------------------------------");
                        Console.ForegroundColor = ConsoleColor.White;
                        Console.Write("[{0}]", m.MarketDelta);
                        Console.ForegroundColor = ConsoleColor.DarkCyan;
                        Console.WriteLine("--------------------------------------------------------\r\n" +
                                        "=======================================================================================================================");

                        //Print Candles:
                        Console.ForegroundColor = ConsoleColor.White;
                        Console.WriteLine("\r\n#CANDLES:");
                        Console.ForegroundColor = ConsoleColor.DarkCyan;
                        int index = 0;
                        foreach (Candle c in RecentCandles)
                        {
                            Console.WriteLine("    T:{0}...O:{1:0.00000000}...H:{2:0.00000000}...L:{3:0.00000000}...C:{4:0.00000000}...V:{5:0.00000000}", c.DateTime, c.Open, c.High, c.Low, c.Close, c.Volume);
                            Console.ForegroundColor = ConsoleColor.DarkGray;
                            Console.WriteLine("        EMA(26)={0:0.00000000}, EMA(12)={1:0.00000000}, EMA(9)={2:0.00000000}", EMA26[index], EMA12[index], EMA9[index]);
                            Console.ForegroundColor = ConsoleColor.DarkCyan;
                            index++;
                        }


                        //Print Bids/Asks:
                        Console.ForegroundColor = ConsoleColor.White;
                        Console.WriteLine("\r\n                                         #BIDS(Top10)    #ASKS(Top10)");
                        Console.ForegroundColor = ConsoleColor.DarkCyan;
                        Console.WriteLine("             ----------------------------------------    ----------------------------------------");
                        for (int i = 0; i < 10; i++)     
                            Console.WriteLine("             {0,25:0.00000000}  |  {1:0.00000000}    {2:0.00000000}  |  {3:0.00000000}              ", bidsTop10[i].Value, bidsTop10[i].Key, asksTop10[i].Key, asksTop10[i].Value);




                        //Print Recent Fills:
                        Console.ForegroundColor = ConsoleColor.White;
                        Console.WriteLine("\r\n#RECENT FILLS(Last20):");
                        Console.ForegroundColor = ConsoleColor.DarkCyan;
                        foreach (mdFill fill in m.TradeHistory.RecentFills.ToArray().OrderByDescending(x => x.TimeStamp).Take(20))
                            Console.WriteLine("{0,8} :: {1}.....Rate={2:0.00000000}, Qty={3:0.########}", fill.OrderType, fill.TimeStamp, fill.Rate, fill.Quantity);



                        //Print [active market] ribbon:
                        Console.Write("\r\nMarkets currently being tracked:\r\n    ");
                        int n = 1;
                        foreach (string mk in mCandles.Keys)
                        {
                            if (mk == m.MarketDelta)
                                Console.ForegroundColor = ConsoleColor.Green;
                            else
                                Console.ForegroundColor = ConsoleColor.White;
                            
                            Console.Write(" [{0}]", mk);

                            if (mk != mCandles.Keys.Last())
                                Console.Write(",");
                            else
                                break;

                            if (n % 10 == 0)
                            {
                                Console.Write("\r\n    ");
                            }
                            n++;
                        }
                        Console.ForegroundColor = ConsoleColor.DarkCyan;
                        Console.Write("\r\n\r\n                                         -");
                        Console.ForegroundColor = ConsoleColor.White;
                        Console.Write("Press SPACEBAR for Next Market");
                        Console.ForegroundColor = ConsoleColor.DarkCyan;
                        Console.WriteLine("-");
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

            await PreloadCandlesDict(6);
        }


        private async Task SubSpecificMarkets()
        {
            foreach (string mk in SpecificDeltas)
                await BtrexWS.subscribeMarket(mk);

            await PreloadCandlesDict(6);
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
