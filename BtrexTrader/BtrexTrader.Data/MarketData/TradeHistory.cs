using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Data.SQLite;
using BtrexTrader.Interface;

namespace BtrexTrader.Data.MarketData
{
    public class TradeHistory : ICloneable
    {
        public string MarketDelta { get; private set; }
        public List<mdFill> RecentFills { get; private set; }
        public DateTime LastStoredCandle { get; private set; }
        private bool CandlesResolved = false;


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
            DateTime snapTime = Convert.ToDateTime(RecentFills.First().TimeStamp);
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

            //STILL NOT RESOLVED!! BUILD CANDLE IF NOT RECENT!!!

            if (candleTime >= snapTime)
                CandlesResolved = true;                
        }


        public async Task Resolve5mCandles()
        {
            if (CandlesResolved)
            {
                Console.WriteLine("[{2}] CANDLES RESOLVED - {0}, {1}", LastStoredCandle, Convert.ToDateTime(RecentFills.First().TimeStamp), MarketDelta);
                return;
            }
                

            Console.WriteLine("RESOLVING [{0}] CANDLES...", MarketDelta);

            HistDataResponse response = await BtrexREST.Get1minCandles(MarketDelta);
            if (!response.success)
            {
                Console.WriteLine("    !!!!ERR GET-1m-CANDLES: [{0}]", MarketDelta);
                return;
            }

            DateTime last1mCandleTime = response.result.Last().T;
            DateTime firstFillTime = Convert.ToDateTime(RecentFills.First().TimeStamp);

            if (last1mCandleTime >= firstFillTime)
            {           
                Console.WriteLine("*TRUE*\r\n{0} > {1} :: [{2}]", last1mCandleTime, firstFillTime, MarketDelta);

                //Build latest 5m candle:






                CandlesResolved = true;
            }
            else
            {
                Console.WriteLine("    !!!!ERR CANT_RECTIFY_CANDLES\r\nLast1mCandle: {0} < LastFill: {1} :: [{2}]", last1mCandleTime, firstFillTime, MarketDelta);
                //CANT RECTIFY WITH 1m CANDLES, WAIT AND RETRY
            }
        }
        

        public object Clone()
        {
            return this.MemberwiseClone();
        }
    }
}
