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
        public bool CandlesRectified = false;


        public TradeHistory(MarketQueryResponse snap)
        {
            //Pull candles from SQLite data and rectify with snap data,
            //if cant rectify imediately, enter rectefication process/state
            MarketDelta = snap.MarketName;
            RecentFills = new List<mdFill>();
            
            if (snap.Fills.Count() > 0)
            {
                foreach (Fill fill in snap.Fills)
                    RecentFills.Add(new mdFill(fill.TimeStamp, fill.Price, fill.Quantity, fill.OrderType));
            }

            //Compare last-time from .data, and first-time from snap:
            DateTime snapTime = Convert.ToDateTime(RecentFills.Last().TimeStamp);
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
            
            if (candleTime >= snapTime)
                CandlesRectified = true;                
        }


        public object Clone()
        {
            return this.MemberwiseClone();
        }
    }
}
