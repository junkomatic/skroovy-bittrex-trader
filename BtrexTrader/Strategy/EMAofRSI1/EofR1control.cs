using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.IO;
using System.Data.SQLite;
using Trady.Core;
using Trady.Analysis;
using BtrexTrader.Interface;
using BtrexTrader.Data;
using BtrexTrader.Data.Market;

namespace BtrexTrader.Strategy.EMAofRSI1
{
    class EofR1control
    {
        private Dictionary<string, List<Candle>> Candles5m = new Dictionary<string, List<Candle>>();
        private Dictionary<string, List<Candle>> Candles20m = new Dictionary<string, List<Candle>>();
        private Dictionary<string, List<Candle>> Candles1h = new Dictionary<string, List<Candle>>();
        private Dictionary<string, List<Candle>> Candles4h = new Dictionary<string, List<Candle>>();
        private Dictionary<string, List<Candle>> Candles12h = new Dictionary<string, List<Candle>>();

        private const string dataFile = "EMAofRSI1trades.data";

        private IReadOnlyList<string> SpecificDeltas = new List<string>()
        {
            "BTC-XLM"//, "BTC-ADA", "BTC-ETH", "BTC-QTUM", "BTC-OMG"
        };
        

        public async Task Initialize()
        {
            //await SubTopMarketsByVol(50);
            await SubSpecificMarkets();

            checkNewTradesHistData();
            await PreloadCandleDicts(21);

            //TODO:
            //CheckHoldings()  ...load in holdings w/Stop-Loss-limits dataset
        }

        public async Task Start()
        {
            while (true)
            {







                Thread.Sleep(1000);


                //foreach (Market m in BtrexData.Markets.Values)
                //{
                //    IEnumerable<Candle> RecentCandles = Candles5m[m.MarketDelta].Skip(Math.Max(0, Candles5m[m.MarketDelta].Count - showPastPeriods));

                //    //PRECALC EMAS HERE
                //    var closes = Candles5m[m.MarketDelta].Select(x => x.Close);
                //    var EMA26 = closes.Ema(26).Skip(Math.Max(0, Candles5m[m.MarketDelta].Count - showPastPeriods)).ToArray();
                //    var EMA12 = closes.Ema(12).Skip(Math.Max(0, Candles5m[m.MarketDelta].Count - showPastPeriods)).ToArray();
                //    var EMA9 = closes.Ema(9).Skip(Math.Max(0, Candles5m[m.MarketDelta].Count - showPastPeriods)).ToArray();

                //    while (!(Console.KeyAvailable && Console.ReadKey(true).Key == ConsoleKey.Spacebar))
                //    {
                //        //TODOS: 
                //        //Every iteration, check BtrexData.Market.LastCandleTime to check if add new mCandles:
                //        if (m.TradeHistory.LastStoredCandle > Candles5m[m.MarketDelta].Last().DateTime)
                //        {
                //            var Importer = new TradyCandleImporter();
                //            foreach (var c in Candles5m)
                //            {
                //                var newCandles = await Importer.ImportAsync(c.Key, Candles5m[m.MarketDelta].Last().DateTime.AddMinutes(5));
                //                c.Value.AddRange(newCandles);
                //            }

                //            RecentCandles = Candles5m[m.MarketDelta].Skip(Math.Max(0, Candles5m[m.MarketDelta].Count - showPastPeriods));
                //            closes = Candles5m[m.MarketDelta].Select(x => x.Close);
                //            EMA26 = closes.Ema(26).Skip(Math.Max(0, Candles5m[m.MarketDelta].Count - showPastPeriods)).ToArray();
                //            EMA12 = closes.Ema(12).Skip(Math.Max(0, Candles5m[m.MarketDelta].Count - showPastPeriods)).ToArray();
                //            EMA9 = closes.Ema(9).Skip(Math.Max(0, Candles5m[m.MarketDelta].Count - showPastPeriods)).ToArray();
                //        }

                //        //Assign Orderbook before Console.Clear()
                //        List<KeyValuePair<decimal, decimal>> bidsTop10 = m.OrderBook.Bids.ToArray().OrderByDescending(k => k.Key).Take(10).ToList();
                //        List<KeyValuePair<decimal, decimal>> asksTop10 = m.OrderBook.Asks.ToArray().OrderBy(k => k.Key).Take(10).ToList();


                //        Console.Clear();
                //        //Print MarketTitle
                //        Console.Write("\r\n------------------------------------------------------");
                //        Console.ForegroundColor = ConsoleColor.White;
                //        Console.Write("[{0}]", m.MarketDelta);
                //        Console.ForegroundColor = ConsoleColor.DarkCyan;
                //        Console.WriteLine("--------------------------------------------------------\r\n" +
                //                        "=======================================================================================================================");

                //        //Print Candles:
                //        Console.ForegroundColor = ConsoleColor.White;
                //        Console.WriteLine("\r\n#CANDLES:");
                //        Console.ForegroundColor = ConsoleColor.DarkCyan;
                //        int index = 0;
                //        foreach (Candle c in RecentCandles)
                //        {
                //            Console.WriteLine("    T:{0}...O:{1:0.00000000}...H:{2:0.00000000}...L:{3:0.00000000}...C:{4:0.00000000}...V:{5:0.00000000}", c.DateTime, c.Open, c.High, c.Low, c.Close, c.Volume);
                //            Console.ForegroundColor = ConsoleColor.DarkGray;
                //            Console.WriteLine("        EMA(26)={0:0.00000000}, EMA(12)={1:0.00000000}, EMA(9)={2:0.00000000}", EMA26[index], EMA12[index], EMA9[index]);
                //            Console.ForegroundColor = ConsoleColor.DarkCyan;
                //            index++;
                //        }


                //        //Print Bids/Asks:
                //        Console.ForegroundColor = ConsoleColor.White;
                //        Console.WriteLine("\r\n                                         #BIDS(Top10)    #ASKS(Top10)");
                //        Console.ForegroundColor = ConsoleColor.DarkCyan;
                //        Console.WriteLine("             ----------------------------------------    ----------------------------------------");
                //        for (int i = 0; i < 10; i++)     
                //            Console.WriteLine("             {0,25:0.00000000}  |  {1:0.00000000}    {2:0.00000000}  |  {3:0.00000000}              ", bidsTop10[i].Value, bidsTop10[i].Key, asksTop10[i].Key, asksTop10[i].Value);




                //        //Print Recent Fills:
                //        Console.ForegroundColor = ConsoleColor.White;
                //        Console.WriteLine("\r\n#RECENT FILLS(Last20):");
                //        Console.ForegroundColor = ConsoleColor.DarkCyan;
                //        foreach (mdFill fill in m.TradeHistory.RecentFills.ToArray().OrderByDescending(x => x.TimeStamp).Take(20))
                //            Console.WriteLine("{0,8} :: {1}.....Rate={2:0.00000000}, Qty={3:0.########}", fill.OrderType, fill.TimeStamp, fill.Rate, fill.Quantity);



                //        //Print [active market] ribbon:
                //        Console.Write("\r\nMarkets currently being tracked:\r\n    ");
                //        int n = 1;
                //        foreach (string mk in Candles5m.Keys)
                //        {
                //            if (mk == m.MarketDelta)
                //                Console.ForegroundColor = ConsoleColor.Green;
                //            else
                //                Console.ForegroundColor = ConsoleColor.White;
                            
                //            Console.Write(" [{0}]", mk);

                //            if (mk != Candles5m.Keys.Last())
                //                Console.Write(",");
                //            else
                //                break;

                //            if (n % 10 == 0)
                //            {
                //                Console.Write("\r\n    ");
                //            }
                //            n++;
                //        }
                //        Console.ForegroundColor = ConsoleColor.DarkCyan;
                //        Console.Write("\r\n\r\n                                         -");
                //        Console.ForegroundColor = ConsoleColor.White;
                //        Console.Write("Press SPACEBAR for Next Market");
                //        Console.ForegroundColor = ConsoleColor.DarkCyan;
                //        Console.WriteLine("-");


                //        Thread.Sleep(1000);
                //    }
                //}
            }            
        }


        private async Task SubTopMarketsByVol(int n)
        {
            List<string> topMarkets = await BtrexREST.TradeMethods.GetTopMarketsByBVbtcOnly(n);
            foreach (string mk in topMarkets)
                await BtrexWS.subscribeMarket("BTC-" + mk);
        }


        private async Task SubSpecificMarkets()
        {
            foreach (string mk in SpecificDeltas)
                await BtrexWS.subscribeMarket(mk);
        }


        private async Task PreloadCandleDicts(int numPeriods)
        {
            //Aggregate in Candles Dicts:
            DateTime startTime = DateTime.UtcNow.Subtract(TimeSpan.FromDays(11));
            foreach (string marketDelta in BtrexData.Markets.Keys)
            {
                Candles12h.Add(marketDelta, new List<Candle>());
                Candles4h.Add(marketDelta, new List<Candle>());
                Candles1h.Add(marketDelta, new List<Candle>());
                Candles20m.Add(marketDelta, new List<Candle>());
                Candles5m.Add(marketDelta, new List<Candle>());

                var importer = new TradyCandleImporter();
                var preCandles = await importer.ImportAsync(marketDelta, startTime);
                
                //GET FIRST CANDLE TIME FOR 12h:
                var offsetSpan12h = new TimeSpan();   
              
                if (DateTime.UtcNow.Hour < 2)
                    offsetSpan12h = (DateTime.UtcNow - DateTime.UtcNow.Date) + TimeSpan.FromHours(10);
                else if (DateTime.UtcNow.Hour < 14)
                    offsetSpan12h = DateTime.UtcNow - DateTime.UtcNow.Date.AddHours(2);
                else if (DateTime.UtcNow.Hour >= 14)
                    offsetSpan12h = DateTime.UtcNow - DateTime.UtcNow.Date.AddHours(14);

                var candleTime12h = (DateTime.UtcNow.Subtract(offsetSpan12h)) - TimeSpan.FromDays(Convert.ToDouble(numPeriods) / 2D);


                //GET FIRST CANDLE TIME FOR 4h:
                var candleTime4h = DateTime.UtcNow.Date
                                    .AddHours((int)(4M * Math.Floor(DateTime.UtcNow.Hour / 4M)))
                                    .Subtract(TimeSpan.FromHours(4 * numPeriods));


                //GET FIRST CANDLE TIME FOR 1h
                var candleTime1h = DateTime.UtcNow.Date
                                    .AddHours(DateTime.UtcNow.Hour)
                                    .Subtract(TimeSpan.FromHours(numPeriods));


                //GET FIRST CANDLE TIME FOR 20m




                //GET FIRST CANDLE TIME FOR 5m



                

                for (int i = 0; i < numPeriods; i++)
                {
                    //ADD NEXT 12h CANDLE:
                    var nextCandleTime12h = candleTime12h.AddHours(12);
                    var CandleRange12h =
                        from Candles in preCandles
                        where Candles.DateTime >= candleTime12h && Candles.DateTime < nextCandleTime12h
                        select Candles;

                    Candles12h[marketDelta].Add(new Candle(candleTime12h, 
                                                           CandleRange12h.First().Open, 
                                                           CandleRange12h.Max(x => x.High), 
                                                           CandleRange12h.Min(x => x.Low), 
                                                           CandleRange12h.Last().Close, 
                                                           CandleRange12h.Sum(x => x.Volume)
                                                          )
                                                );
                    candleTime12h = nextCandleTime12h;


                    //ADD NEXT 4h CANDLE:
                    var nextCandleTime4h = candleTime4h.AddHours(4);
                    var CandleRange4h =
                        from Candles in preCandles
                        where Candles.DateTime >= candleTime4h && Candles.DateTime < nextCandleTime4h
                        select Candles;

                    Candles4h[marketDelta].Add(new Candle(candleTime4h,
                                                           CandleRange4h.First().Open,
                                                           CandleRange4h.Max(x => x.High),
                                                           CandleRange4h.Min(x => x.Low),
                                                           CandleRange4h.Last().Close,
                                                           CandleRange4h.Sum(x => x.Volume)
                                                          )
                                                );
                    candleTime4h = nextCandleTime4h;


                    //ADD NEXT 1h CANDLE:
                    var nextCandleTime1h = candleTime1h.AddHours(1);
                    var CandleRange1h =
                        from Candles in preCandles
                        where Candles.DateTime >= candleTime1h && Candles.DateTime < nextCandleTime1h
                        select Candles;

                    Candles1h[marketDelta].Add(new Candle(candleTime1h,
                                                           CandleRange1h.First().Open,
                                                           CandleRange1h.Max(x => x.High),
                                                           CandleRange1h.Min(x => x.Low),
                                                           CandleRange1h.Last().Close,
                                                           CandleRange1h.Sum(x => x.Volume)
                                                          )
                                                );
                    candleTime1h = nextCandleTime1h;


                    //ADD NEXT 20m CANDLE:








                }











                //Candles5m.Add(market.MarketDelta, new List<Candle>(preCandles));
            }
        }


        private static bool checkNewTradesHistData()
        {
            if (!File.Exists(dataFile))
            {
                Console.WriteLine("CREATING NEW '{0}' FILE...", dataFile);
                SQLiteConnection.CreateFile(dataFile);
                using (SQLiteConnection conn = new SQLiteConnection("Data Source=" + dataFile + ";Version=3;"))
                {
                    conn.Open();
                    using (var cmd = new SQLiteCommand(conn))
                    {
                        using (var tx = conn.BeginTransaction())
                        {
                            cmd.CommandText = string.Format("CREATE TABLE IF NOT EXISTS EMAofRSI1_5m (DateTime TEXT, Open TEXT, Rate TEXT, Qty TEXT)");
                            cmd.ExecuteNonQuery();

                            cmd.CommandText = string.Format("CREATE TABLE IF NOT EXISTS EMAofRSI1_20m (DateTime TEXT, Open TEXT, Rate TEXT, Qty TEXT)");
                            cmd.ExecuteNonQuery();

                            cmd.CommandText = string.Format("CREATE TABLE IF NOT EXISTS EMAofRSI1_1h (DateTime TEXT, Open TEXT, Rate TEXT, Qty TEXT)");
                            cmd.ExecuteNonQuery();

                            cmd.CommandText = string.Format("CREATE TABLE IF NOT EXISTS EMAofRSI1_4h (DateTime TEXT, Open TEXT, Rate TEXT, Qty TEXT)");
                            cmd.ExecuteNonQuery();

                            cmd.CommandText = string.Format("CREATE TABLE IF NOT EXISTS EMAofRSI1_12h (DateTime TEXT, Open TEXT, Rate TEXT, Qty TEXT)");
                            cmd.ExecuteNonQuery();

                            tx.Commit();
                        }                        
                    }
                    conn.Close();
                }
                return true;
            }
            else
                return false;
        }

    }


}
