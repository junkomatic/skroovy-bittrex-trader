using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Data.SQLite;
using Trady.Core;
using BtrexTrader.Interface;

namespace BtrexTrader.Data.MarketData
{
    public class TradeHistory : ICloneable
    {
        public string MarketDelta { get; private set; }
        public List<mdFill> RecentFills { get; private set; }
        public DateTime LastStoredCandle { get; private set; }
        private bool CandlesResolved = false;
        public List<Candle> Candles5m { get; private set; }

        public TradeHistory(MarketQueryResponse snap)
        {
            //Pull candles from SQLite data and rectify with snap data,
            //if cant rectify imediately, enter rectefication process/state
            MarketDelta = snap.MarketName;
            RecentFills = new List<mdFill>();
            
            if (snap.Fills.Count() > 0)
            {
                snap.Fills.Reverse();
                foreach (Fill fill in snap.Fills)
                    RecentFills.Add(new mdFill(fill.TimeStamp, fill.Price, fill.Quantity, fill.OrderType));
            }

            //Compare last-time from .data, and first-time from snap:
            DateTime snapTime = RecentFills.First().TimeStamp;
            DateTime candleTime;

            using (SQLiteConnection conn = new SQLiteConnection("Data Source=" + HistoricalData.dbName + ";Version=3;"))
            {
                conn.Open();
                using (var cmd = new SQLiteCommand(conn))
                {
                    cmd.CommandText = string.Format("SELECT * FROM {0} ORDER BY datetime(DateTime) DESC Limit 1", MarketDelta.Replace('-', '_'));
                    candleTime = Convert.ToDateTime(cmd.ExecuteScalar());
                }
                conn.Close();
            }

            LastStoredCandle = candleTime;
            DateTime NextCandleTime = LastStoredCandle.AddMinutes(5);
            Console.WriteLine("\r\n***Last5mCandle: {0}.....CurrTime: {1}", LastStoredCandle, DateTime.UtcNow);
            
            if (NextCandleTime >= snapTime)
            {
                if (NextCandleTime.AddMinutes(5) > DateTime.UtcNow)
                {
                    Console.WriteLine("@@@@@ NOT NEXT-CANDLE-TIME YET");
                    CandlesResolved = true;
                    return;
                }

                Candle nextCandle = BuildCandleFromRecentFills(NextCandleTime);

                Console.WriteLine("@@@@@ NEW CANDLE = T:{0} O:{1} H:{2} L:{3} C:{4} V:{5}",
                    nextCandle.DateTime, nextCandle.Open, nextCandle.High, nextCandle.Low, nextCandle.Close, nextCandle.Volume);
                
                CandlesResolved = true;
            }
                              
        }


        public async Task Resolve5mCandles()
        {
            if (CandlesResolved)
            {
                Console.WriteLine("[{2}] CANDLES RESOLVED - {0}, {1}", LastStoredCandle, RecentFills.First().TimeStamp, MarketDelta);
                return;
            }
                

            Console.WriteLine("RESOLVING [{0}] CANDLES...", MarketDelta);

            HistDataResponse response = await BtrexREST.GetMarketHistoryV2(MarketDelta, "oneMin");
            if (!response.success)
            {
                Console.WriteLine("    !!!!ERR GET-1m-CANDLES: [{0}]", MarketDelta);
                return;
            }

            DateTime last1mCandleTime = response.result.Last().T;
            DateTime firstFillTime = RecentFills.First().TimeStamp;

            if (last1mCandleTime >= firstFillTime)
            {
                //TODO: Build latest 5m candle with 1m data and RecentFills:
                Console.WriteLine("*TRUE* === {0} > {1} :: [{2}]\r\n", last1mCandleTime, firstFillTime, MarketDelta);
                Console.Beep();


                Console.WriteLine("\r\n*************1mCandles***************");
                foreach (HistDataLine line in response.result)
                {
                    if (line.T >= LastStoredCandle)
                    {
                        Console.WriteLine("{0} [O:{1}...H:{2}...L:{3}...C:{4}...V:{5}...BV:{6}]", line.T, line.O, line.H, line.L, line.C, line.V, line.BV);
                        



                    }

                }


                DateTime LastStoredPlus5 = LastStoredCandle.AddMinutes(5);
                Console.WriteLine("************RecentFills**************");
                foreach (mdFill fill in RecentFills)
                {
                    //if (fill.TimeStamp < LastStoredPlus5)
                        Console.WriteLine("{0} {1} == R:{2}...V:{3}...BV:{4}", fill.TimeStamp, fill.OrderType, fill.Rate, fill.Quantity, (fill.Quantity * fill.Rate));






                }
                Console.WriteLine("*************************************");





                CandlesResolved = true;
            }
            else
            {
                Console.WriteLine("    !!!!ERR CANT_RECTIFY_CANDLES\r\nLast1mCandle: {0} < LastFill: {1} :: [{2}]", last1mCandleTime, firstFillTime, MarketDelta);

                //CANT RECTIFY WITH 1m CANDLES, 
                //TODO: WAIT AND RETRY

                Console.Beep();
                Console.Beep();
                Console.Beep();
                Console.Beep();




            }
        }
        
        private Candle BuildCandleFromRecentFills(DateTime NextCandleTime)
        {
            List<mdFill> candleFills = new List<mdFill>();
            foreach (mdFill fill in RecentFills)
            {
                if (fill.TimeStamp < NextCandleTime)
                    continue;
                else if (fill.TimeStamp >= NextCandleTime.AddMinutes(5))
                    break;
                else
                    candleFills.Add(fill);
                Console.WriteLine("{0} {1} == R:{2}...V:{3}...BV:{4}", fill.TimeStamp, fill.OrderType, fill.Rate, fill.Quantity, (fill.Quantity * fill.Rate));
            }

            Decimal O = candleFills.First().Rate,
                    H = candleFills.Max(x => x.Rate),
                    L = candleFills.Min(x => x.Rate),
                    C = candleFills.Last().Rate,
                    V = candleFills.Sum(x => x.Quantity);

            return new Candle(NextCandleTime, O, H, L, C, V);
        }

        public object Clone()
        {
            return this.MemberwiseClone();
        }
    }
}
