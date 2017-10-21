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
        public List<HistDataLine> Candles5m { get; private set; }
        public DateTime LastStoredCandle { get; private set; }
        private bool CandlesResolved = false;

        public TradeHistory(MarketQueryResponse snap)
        {
            //Pull candles from SQLite data and rectify with snap data,
            //if cant rectify imediately, enter rectefication process/state
            MarketDelta = snap.MarketName;
            RecentFills = new List<mdFill>();
            Candles5m = new List<HistDataLine>();
            
            if (snap.Fills.Count() > 0)
            {
                snap.Fills.Reverse();
                foreach (Fill fill in snap.Fills)
                    RecentFills.Add(new mdFill(Convert.ToDateTime(fill.TimeStamp), fill.Price, fill.Quantity, fill.OrderType));
            }

            //Compare last-time from .data, and first-time from snap:
            using (SQLiteConnection conn = new SQLiteConnection("Data Source=" + HistoricalData.dbName + ";Version=3;"))
            {
                conn.Open();
                using (var cmd = new SQLiteCommand(conn))
                {
                    cmd.CommandText = string.Format("SELECT * FROM {0} ORDER BY datetime(DateTime) DESC Limit 1", MarketDelta.Replace('-', '_'));
                    LastStoredCandle = Convert.ToDateTime(cmd.ExecuteScalar());
                }
                conn.Close();
            }

            //Candle Time is the START time of the 5m period. This means it is current to +5min from that time.
            DateTime NextCandleTime = LastStoredCandle.AddMinutes(5);

            if (NextCandleTime > RecentFills.Last().TimeStamp)
            {
                //NO TRADES MADE IN THIS CANDLE PERIOD.
                CandlesResolved = true;
                return;
            }
            else if (NextCandleTime >= RecentFills.First().TimeStamp)
            {
                //If candle is current, Candles are Resolved
                if (NextCandleTime.AddMinutes(5) > DateTime.UtcNow)
                {
                    CandlesResolved = true;
                    return;
                }

                BuildCandleFromRecentFills(NextCandleTime);

                //Console.WriteLine("@@@@@ NEW CANDLE = T:{0} O:{1} H:{2} L:{3} C:{4} V:{5}",
                    //nextCandle.DateTime, nextCandle.Open, nextCandle.High, nextCandle.Low, nextCandle.Close, nextCandle.Volume);
                
                CandlesResolved = true;
            }
                              
        }


        public async Task Resolve5mCandles()
        {
            DateTime NextCandleTime = LastStoredCandle.AddMinutes(5);
            if (CandlesResolved)
            {
                Console.WriteLine("[{1}] CANDLES RESOLVED - LastCandleStart: {0}", LastStoredCandle, MarketDelta);
                return;
            }
             

            Console.Beep();


            Console.WriteLine("RESOLVING [{0}] CANDLES...", MarketDelta);
            Console.WriteLine("\r\n***Last5mCandleStart: {0}\r\n...CurrTime: {1}\r\n...SnapData Begins: {2}\r\n", LastStoredCandle, DateTime.UtcNow, RecentFills.First().TimeStamp);

            HistDataResponse response = await BtrexREST.GetMarketHistoryV2(MarketDelta, "oneMin");
            if (!response.success)
            {
                Console.WriteLine("    !!!!ERR GET-1m-CANDLES: [{0}]", MarketDelta);
                return;
            }

            DateTime last1mCandleCurrTime = response.result.Last().T.AddMinutes(1);
            DateTime firstFillTime = RecentFills.First().TimeStamp;

            if (last1mCandleCurrTime >= firstFillTime)
            {
                //Build latest 5m candle with 1m data and RecentFills:
                Console.WriteLine("*TRUE* === {0} > {1} :: [{2}]\r\n", last1mCandleCurrTime, firstFillTime, MarketDelta);
                
                Console.WriteLine("\r\n*************1mCandles***************");
                List<HistDataLine> Candles1m = new List<HistDataLine>();
                foreach (HistDataLine line in response.result)
                {
                    if (line.T >= LastStoredCandle)
                    {
                        Candles1m.Add(line);
                        Console.WriteLine("{0} [O:{1}...H:{2}...L:{3}...C:{4}...V:{5}...BV:{6}]", line.T, line.O, line.H, line.L, line.C, line.V, line.BV);
                    }
                }
                
                //Grab O:H:L:V (noC) from 1mCandles
                //simulate 2 mdFills (H&L:V) and add to beginning of RecentFills
                Decimal O = Candles1m.First().O,
                        H = Candles1m.Max(x => x.H),
                        L = Candles1m.Min(x => x.L),
                        V = Candles1m.Sum(x => x.V);
                List<mdFill> RevisedFills = new List<mdFill>();
                RevisedFills.Add(new mdFill(LastStoredCandle.AddMinutes(5), O, (V / 3M), "BUY"));
                RevisedFills.Add(new mdFill(LastStoredCandle.AddMinutes(5.1), H, (V / 3M), "BUY"));
                RevisedFills.Add(new mdFill(LastStoredCandle.AddMinutes(5.15), L, (V / 3M), "SELL"));
                
                //check current, if not, rectify
                //If candle is current, Candles are Resolved
                if (NextCandleTime.AddMinutes(5) > DateTime.UtcNow)
                {
                    Console.WriteLine("@@@@@ NOT NEXT-CANDLE-TIME YET");
                    CandlesResolved = true;
                    return;
                }

                Console.WriteLine("************RecentFills**************");
                foreach (mdFill fill in RecentFills)
                {
                    if (fill.TimeStamp >= last1mCandleCurrTime)
                    {
                        RevisedFills.Add(fill);
                        Console.WriteLine("{0} {1} == R:{2}...V:{3}...BV:{4}", fill.TimeStamp, fill.OrderType, fill.Rate, fill.Quantity, (fill.Quantity * fill.Rate));
                    }
                }
                Console.WriteLine("*************************************");

                RecentFills = new List<mdFill>(RevisedFills);

                
                HistDataLine nextCandle = BuildCandleFromRecentFills(NextCandleTime);

                Console.WriteLine("@@@@@ NEW CANDLE(w/1m!) = T:{0} O:{1} H:{2} L:{3} C:{4} V:{5}, BV:{6}",
                    nextCandle.T, nextCandle.O, nextCandle.H, nextCandle.L, nextCandle.C, nextCandle.V, nextCandle.BV);

                CandlesResolved = true;
            }
            else
            {
                Console.WriteLine("    !!!!ERR CANT_RECTIFY_CANDLES\r\nLast1mCandleCurrent: {0} < LastFill: {1} :: [{2}]", last1mCandleCurrTime, firstFillTime, MarketDelta);

                //CANT RECTIFY WITH 1m CANDLES, 
                //TODO: WAIT FOR NEXT 1m CANDLE-PULL AND RETRY



                Console.Beep();
                Console.Beep();
                Console.Beep();
                Console.Beep();
            }
        }


        public HistDataLine BuildCandleFromRecentFills(DateTime NextCandleTime)
        {
            Decimal BV = 0;
            List<mdFill> candleFills = new List<mdFill>();
            foreach (mdFill fill in RecentFills)
            {
                if (fill.TimeStamp < NextCandleTime)
                    continue;
                else if (fill.TimeStamp >= NextCandleTime.AddMinutes(5))
                    break;
                else
                {
                    BV += (fill.Quantity * fill.Rate);
                    candleFills.Add(fill);
                }
                    
            }

            Decimal O = candleFills.First().Rate,
                    H = candleFills.Max(x => x.Rate),
                    L = candleFills.Min(x => x.Rate),
                    C = candleFills.Last().Rate,
                    V = candleFills.Sum(x => x.Quantity);


            HistDataLine candle = new HistDataLine(NextCandleTime, O, H, L, C, V, BV);
            Candles5m.Add(candle);
            LastStoredCandle = LastStoredCandle.AddMinutes(5);

            //ONLY RETURNING FOR DEBUG (1m candleResolve - see above)
            return candle;
        }

        public void SavePurgeCandlesSQLite(SQLiteCommand cmd)
        {
            cmd.CommandText = string.Format("SELECT * FROM {0} ORDER BY datetime(DateTime) DESC Limit 1", MarketDelta);
            DateTime dateTime = Convert.ToDateTime(cmd.ExecuteScalar());

            List<HistDataLine> removeList = new List<HistDataLine>();

            foreach (HistDataLine line in Candles5m)
            {

                if (line.T >= LastStoredCandle.Subtract(TimeSpan.FromHours(3)))
                    break;
                if (line.T <= dateTime)
                    continue;
                else
                {
                    cmd.CommandText = string.Format(
                        "INSERT INTO {0} (DateTime, Open, High, Low, Close, Volume, BaseVolume) "
                        + "VALUES ('{1}', '{2}', '{3}', '{4}', '{5}', '{6}', '{7}')",
                        MarketDelta,
                        line.T.ToString("yyyy-MM-dd HH:mm:ss"), line.O, line.H, line.L, line.C, line.V, line.BV);

                    cmd.ExecuteNonQuery();
                    removeList.Add(line);
                }
            }

            Candles5m = (List<HistDataLine>)Candles5m.Except(removeList);
        }

        public void CullRecentFills()
        {
            //CULL RecentFills to 20 minutes before LastCandleTime
            DateTime cullTime = LastStoredCandle.Subtract(TimeSpan.FromMinutes(20));
            List<mdFill> culledFills = new List<mdFill>();
            foreach (mdFill fill in RecentFills)
                if (fill.TimeStamp < cullTime)
                    culledFills.Add(fill);

            RecentFills = (List<mdFill>)RecentFills.Except(culledFills);
        }


        public object Clone()
        {
            return this.MemberwiseClone();
        }
    }
}
