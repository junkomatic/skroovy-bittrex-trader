using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Data;
using Trady.Core;
using Trady.Core.Infrastructure;
using Trady.Core.Period;
using System.Data.SQLite;


namespace BtrexTrader.Data
{
    public class TradyCandleImporter : IImporter
    {
        public async Task<IReadOnlyList<Candle>> ImportAsync(string symbol, DateTime? startTime = null, DateTime? endTime = null, PeriodOption period = PeriodOption.Daily, CancellationToken token = default(CancellationToken))
        {
            //Import Candles from Data.Candle5m, get more data from sqlite if needed:
            if (endTime == null)
                endTime = DateTime.UtcNow;

            List<HistDataLine> HistLines = new List<HistDataLine>(BtrexData.Markets[symbol].TradeHistory.Candles5m);
            List<Candle> cndls = new List<Candle>();

            foreach (HistDataLine histLine in HistLines)
                if (histLine.T >= startTime || histLine.T <= endTime)
                    cndls.Add(new Candle(histLine.T, histLine.O, histLine.H, histLine.L, histLine.C, histLine.V));

            if (startTime >= HistLines.First().T)
            {
                //Call lines from SQLite and set cndls in correct order:
                List<Candle> sqlCndls = new List<Candle>();
                using (SQLiteConnection conn = new SQLiteConnection("Data Source=" + HistoricalData.dbName + ";Version=3;"))
                {
                    conn.Open();
                    string startTimeString = startTime.Value.ToString("yyyy-MM-dd HH:mm:ss"),
                           endTimeString = endTime.Value.ToString("yyyy-MM-dd HH:mm:ss");
                    
                    DataTable dt = new DataTable();
                    string commandString = "SELECT * FROM " + symbol.Replace('-', '_') +
                                           " WHERE datetime(DateTime) >= datetime(" + startTimeString + ")" +
                                           " AND datetime(DateTime) <= datetime("+ endTimeString +")" +
                                           " ORDER BY datetime(DateTime) ASC";

                    using (var sqlAdapter = new SQLiteDataAdapter(commandString, conn))
                        sqlAdapter.Fill(dt);

                    foreach (DataRow line in dt.Rows)
                        sqlCndls.Add(new Candle(Convert.ToDateTime(line["DateTime"]), Convert.ToDecimal(line["Open"]), Convert.ToDecimal(line["High"]), Convert.ToDecimal(line["Low"]), Convert.ToDecimal(line["Close"]), Convert.ToDecimal(line["Volume"])));

                    conn.Close();
                }
                cndls.InsertRange(0, sqlCndls);
            }
            
            return cndls;
        }




    }
}
