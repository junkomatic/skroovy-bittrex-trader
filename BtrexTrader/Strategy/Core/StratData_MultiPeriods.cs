using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Trady.Core;
using BtrexTrader.Data;

namespace BtrexTrader.Strategy.Core
{
    class StratData_MultiPeriods
    {
        public Dictionary<string, List<Candle>> Candles5m { get; private set; }
        public Dictionary<string, List<Candle>> Candles20m { get; private set; }
        public Dictionary<string, List<Candle>> Candles1h { get; private set; }
        public Dictionary<string, List<Candle>> Candles4h { get; private set; }
        public Dictionary<string, List<Candle>> Candles12h { get; private set; }



        public StratData_MultiPeriods()
        {
            Candles5m = new Dictionary<string, List<Candle>>();
            Candles20m = new Dictionary<string, List<Candle>>();
            Candles1h = new Dictionary<string, List<Candle>>();
            Candles4h = new Dictionary<string, List<Candle>>();
            Candles12h = new Dictionary<string, List<Candle>>();
    
        }

        public async Task PreloadCandleDicts(int numPeriods)
        {
            //Aggregate in Candles Dicts:
            DateTime startTime = DateTime.UtcNow.Subtract(TimeSpan.FromDays((numPeriods / 2D) + 0.5));

            foreach (string marketDelta in BtrexData.Markets.Keys)
            {
                
                Console.Write("\rPRELOADING MULTI-PERIOD CANDLES: {0}        ", marketDelta);

                Candles12h.Add(marketDelta, new List<Candle>());
                Candles4h.Add(marketDelta, new List<Candle>());
                Candles1h.Add(marketDelta, new List<Candle>());
                Candles20m.Add(marketDelta, new List<Candle>());

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
                var candleTime20m = DateTime.UtcNow.Date
                                     .AddHours(DateTime.UtcNow.Hour)
                                     .AddMinutes((int)(20M * Math.Floor(DateTime.UtcNow.Minute / 20M)))
                                     .Subtract(TimeSpan.FromMinutes(20 * numPeriods));


                //GET FIRST CANDLE TIME FOR 5m
                var candleTime5m = DateTime.UtcNow.Date
                                     .AddHours(DateTime.UtcNow.Hour)
                                     .AddMinutes((int)(5M * Math.Floor(DateTime.UtcNow.Minute / 5M)))
                                     .Subtract(TimeSpan.FromMinutes(5 * numPeriods));


                //FORM ALL CANDLES:
                for (int i = 0; i < numPeriods; i++)
                {
                    //ADD NEXT 12h CANDLE:
                    var nextCandleTime12h = candleTime12h.AddHours(12);
                    var CandleRange12h =
                        from Candles in preCandles
                        where (Candles.DateTime >= candleTime12h) && (Candles.DateTime < nextCandleTime12h)
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
                        where (Candles.DateTime >= candleTime4h) && (Candles.DateTime < nextCandleTime4h)
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
                        where (Candles.DateTime >= candleTime1h) && (Candles.DateTime < nextCandleTime1h)
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
                    var nextCandleTime20m = candleTime20m.AddMinutes(20);
                    var CandleRange20m =
                        from Candles in preCandles
                        where (Candles.DateTime >= candleTime20m) && (Candles.DateTime < nextCandleTime20m)
                        select Candles;

                    Candles20m[marketDelta].Add(new Candle(candleTime20m,
                                                           CandleRange20m.First().Open,
                                                           CandleRange20m.Max(x => x.High),
                                                           CandleRange20m.Min(x => x.Low),
                                                           CandleRange20m.Last().Close,
                                                           CandleRange20m.Sum(x => x.Volume)
                                                          )
                                                );
                    candleTime20m = nextCandleTime20m;

                }

                //FINALLY, ADD ALL 5m CANDLES
                Candles5m[marketDelta] = new List<Candle>(
                                    from Candles in preCandles
                                    where Candles.DateTime >= candleTime5m
                                    select Candles);
            }


            Console.Write("\rPRELOADING MULTI-PERIOD CANDLES: [COMPLETE]");
        }


        public bool BuildNew20mCndls(string marketDelta)
        {
            bool changed = false;
            var candleTime20m = Candles20m[marketDelta].Last().DateTime.AddMinutes(20);
            while (BtrexData.Markets[marketDelta].TradeHistory.LastStoredCandle >= candleTime20m.AddMinutes(15))
            {
                var nextCandleTime20m = candleTime20m.AddMinutes(20);
                var CandleRange20m =
                    from Candles in Candles5m[marketDelta]
                    where (Candles.DateTime >= candleTime20m) && (Candles.DateTime < nextCandleTime20m)
                    select Candles;

                if (CandleRange20m.Count() == 0)
                {
                    candleTime20m = nextCandleTime20m;
                    continue;
                }

                Candles20m[marketDelta].Add(new Candle(candleTime20m,
                                                       CandleRange20m.First().Open,
                                                       CandleRange20m.Max(x => x.High),
                                                       CandleRange20m.Min(x => x.Low),
                                                       CandleRange20m.Last().Close,
                                                       CandleRange20m.Sum(x => x.Volume)
                                                      )
                                            );

                changed = true;
                candleTime20m = nextCandleTime20m;
            }
            return changed;
        }

        public bool BuildNew1hCndls(string marketDelta)
        {
            bool changed = false;
            var candleTime1h = Candles1h[marketDelta].Last().DateTime.AddHours(1);
            while (BtrexData.Markets[marketDelta].TradeHistory.LastStoredCandle >= candleTime1h.AddMinutes(55))
            {
                var nextCandleTime1h = candleTime1h.AddHours(1);
                var CandleRange1h =
                    from Candles in Candles5m[marketDelta]
                    where (Candles.DateTime >= candleTime1h) && (Candles.DateTime < nextCandleTime1h)
                    select Candles;

                if (CandleRange1h.Count() == 0)
                {
                    candleTime1h = nextCandleTime1h;
                    continue;
                }

                Candles1h[marketDelta].Add(new Candle(candleTime1h,
                                                        CandleRange1h.First().Open,
                                                        CandleRange1h.Max(x => x.High),
                                                        CandleRange1h.Min(x => x.Low),
                                                        CandleRange1h.Last().Close,
                                                        CandleRange1h.Sum(x => x.Volume)
                                                        )
                                            );

                changed = true;
                candleTime1h = nextCandleTime1h;
            }
            return changed;
        }

        public bool BuildNew4hCndls(string marketDelta)
        {
            bool changed = false;
            var candleTime4h = Candles4h[marketDelta].Last().DateTime.AddHours(4);
            while (BtrexData.Markets[marketDelta].TradeHistory.LastStoredCandle >= candleTime4h.AddMinutes(235))
            {
                var nextCandleTime4h = candleTime4h.AddHours(4);
                var CandleRange4h =
                    from Candles in Candles5m[marketDelta]
                    where (Candles.DateTime >= candleTime4h) && (Candles.DateTime < nextCandleTime4h)
                    select Candles;

                if (CandleRange4h.Count() == 0)
                {
                    candleTime4h = nextCandleTime4h;
                    continue;
                }

                Candles1h[marketDelta].Add(new Candle(candleTime4h,
                                                       CandleRange4h.First().Open,
                                                       CandleRange4h.Max(x => x.High),
                                                       CandleRange4h.Min(x => x.Low),
                                                       CandleRange4h.Last().Close,
                                                       CandleRange4h.Sum(x => x.Volume)
                                                      )
                                            );

                changed = true;
                candleTime4h = nextCandleTime4h;
            }
            return changed;
        }

        public bool BuildNew12hCndls(string marketDelta)
        {
            bool changed = false;
            var candleTime12h = Candles12h[marketDelta].Last().DateTime.AddHours(12);
            while (BtrexData.Markets[marketDelta].TradeHistory.LastStoredCandle.AddMinutes(5) >= candleTime12h.AddHours(12))
            {
                var nextCandleTime12h = candleTime12h.AddHours(12);
                var CandleRange12h =
                    from Candles in Candles5m[marketDelta]
                    where (Candles.DateTime >= candleTime12h) && (Candles.DateTime < nextCandleTime12h)
                    select Candles;

                if (CandleRange12h.Count() == 0)
                {
                    candleTime12h = nextCandleTime12h;
                    continue;
                }

                Candles1h[marketDelta].Add(new Candle(candleTime12h,
                                                       CandleRange12h.First().Open,
                                                       CandleRange12h.Max(x => x.High),
                                                       CandleRange12h.Min(x => x.Low),
                                                       CandleRange12h.Last().Close,
                                                       CandleRange12h.Sum(x => x.Volume)
                                                      )
                                            );

                changed = true;
                candleTime12h = nextCandleTime12h;
            }
            return changed;
        }
        

    }


}
