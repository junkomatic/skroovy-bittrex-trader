using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Linq;
using System.Data;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Data.SQLite;
using BtrexTrader.Interface;


namespace BtrexTrader.Data
{
    public static class HistoricalData
    {
        private static ConcurrentQueue<HistDataResponse> DataQueue = new ConcurrentQueue<HistDataResponse>();
        private static int //downloaded = 0,
                    saved = 0,
                    totalCount = 0;
        private static bool savedComplete = false;
        public const string dbName = "MarketHistory.data";


        public static async Task UpdateHistData()
        {
            Console.WriteLine();

            //Start ProcessQueue thread:
            var SQLthread = new Thread(() => ProcessDataQueue());
            SQLthread.IsBackground = true;
            SQLthread.Name = "Saving-Market-Histories";
            SQLthread.Start();


            //Call list of available markets from Btrex:
            GetMarketsResponse markets = await BtrexREST.GetMarkets();
            if (markets.success != true)
            {
                Console.WriteLine("    !!!!ERR GET-MARKETS>>> " + markets.message);
                return;
            }


            //Create string list of BTC market deltas only: 
            List<string> BTCmarketDeltas = new List<string>();
            foreach (GetMarketsResult market in markets.result)
            {
                if (market.MarketName.Split('-')[0] == "BTC")
                    BTCmarketDeltas.Add(market.MarketName);
            }

            totalCount = BTCmarketDeltas.Count;

            //Download all histories, enqueue responses:
            var downloadHists = BTCmarketDeltas.Select(EnqueueData).ToArray();
            await Task.WhenAll(downloadHists);

            //Wait for Save-Data thread to complete:
            while (!savedComplete)
                Thread.Sleep(100);

            SQLthread.Abort();
            Console.WriteLine();

            //Update CSV files:
            Console.Write("Updating .CSVs - Just a moment...");
            UpdateOrCreateCSVs();

            //Garbage Collection to clean up SQLiteConnection
            GC.Collect();
            GC.WaitForPendingFinalizers();

            Console.WriteLine("\rUpdating .CSVs - [COMPLETE]        ");
        }


        private static async Task EnqueueData(string delta)
        {
            HistDataResponse histData = await BtrexREST.GetMarketHistoryV2(delta, "fiveMin");
            if (histData.success != true)
            {
                Console.WriteLine("    !!!!ERR GET-HISTORY>>> " + histData.message);
                return;
            }
            //downloaded++;

            DataQueue.Enqueue(histData);
        }


        private static void ProcessDataQueue()
        {
            bool newDB = checkNewDB();

            using (SQLiteConnection conn = new SQLiteConnection("Data Source=" + dbName + ";Version=3;"))
            {
                conn.Open();
                using (var cmd = new SQLiteCommand(conn))
                {
                    using (var tx = conn.BeginTransaction())
                    {
                        do
                        {
                            while (DataQueue.IsEmpty)
                                Thread.Sleep(100);

                            bool DQed = DataQueue.TryDequeue(out HistDataResponse history);
                            if (DQed)
                            {
                                if (newDB)
                                    CreateNewDataTable(history, cmd);
                                else
                                    UpdateDataTable(history, cmd);
                            }

                        } while (saved < totalCount);

                        tx.Commit();
                    }
                }
                conn.Close();
            }
            savedComplete = true;
        }


        private static void CreateNewDataTable(HistDataResponse data, SQLiteCommand cmd)
        {
            cmd.CommandText = string.Format("CREATE TABLE IF NOT EXISTS {0} (DateTime TEXT, Open TEXT, Close TEXT, Low TEXT, High TEXT, Volume TEXT, BaseVolume TEXT)", data.MarketDelta);
            cmd.ExecuteNonQuery();

            foreach (HistDataLine line in data.result)
                EnterSQLiteRow(line, cmd, data.MarketDelta);

            saved++;
            Console.Write("\rDownloading Candle Data: {0}/{1}", saved, totalCount);
        }


        private static void UpdateDataTable(HistDataResponse data, SQLiteCommand cmd)
        {
            //look for market. if !exist then CreateNewDataTable()
            cmd.CommandText = string.Format("SELECT CASE WHEN tbl_name = '{0}' THEN 1 ELSE 0 END FROM sqlite_master "
                                            + "WHERE tbl_name = '{0}' AND type = 'table'", data.MarketDelta);

            if (!Convert.ToBoolean(cmd.ExecuteScalar()))
            {
                CreateNewDataTable(data, cmd);
                return;
            }


            //TODO: REPLACE NULL VALUES IN BV COLUMN WITH DATA


            cmd.CommandText = string.Format("SELECT * FROM {0} ORDER BY datetime(DateTime) DESC Limit 1", data.MarketDelta);
            DateTime dateTime = Convert.ToDateTime(cmd.ExecuteScalar());

            foreach (HistDataLine line in data.result)
            {
                if (line.T <= dateTime)
                    continue;
                else
                    EnterSQLiteRow(line, cmd, data.MarketDelta);
            }
            saved++;
            Console.Write("\rDownloading Candle Data: {0}/{1}", saved, totalCount);
        }


        private static void EnterSQLiteRow(HistDataLine line, SQLiteCommand cmd, string delta)
        {
            cmd.CommandText = string.Format(
                                    "INSERT INTO {0} (DateTime, Open, High, Low, Close, Volume, BaseVolume) "
                                    + "VALUES ('{1}', '{2}', '{3}', '{4}', '{5}', '{6}', {7})",
                                    delta,
                                    line.T.ToString("yyyy-MM-dd HH:mm:ss"), line.O, line.H, line.L, line.C, line.V, line.BV);

            cmd.ExecuteNonQuery();
        }


        private static bool checkNewDB()
        {
            if (!File.Exists(dbName))
            {
                Console.WriteLine("LOCAL DATA FILE NOT FOUND\r\nCREATING NEW '{0}' FILE...", dbName);
                SQLiteConnection.CreateFile(dbName);
                return true;
            }
            else
                return false;
        }


        public static void UpdateOrCreateCSVs()
        {
            using (SQLiteConnection conn = new SQLiteConnection("Data Source=" + dbName + ";Version=3;"))
            {
                conn.Open();
                using (var cmd = new SQLiteCommand(conn))
                {
                    List<string> tableNames = new List<string>();
                    cmd.CommandText = "SELECT name FROM sqlite_master WHERE type = 'table' ORDER BY 1";
                    SQLiteDataReader r = cmd.ExecuteReader();
                    while (r.Read())
                    {
                        tableNames.Add(r["name"].ToString());
                    }

                    Directory.CreateDirectory("BtrexCSVs");
                    
                    foreach (string tName in tableNames)
                    {
                        DataTable dt = new DataTable();
                        using (var sqlAdapter = new SQLiteDataAdapter("SELECT * from " + tName, conn))
                            sqlAdapter.Fill(dt);

                        string path = @"BtrexCSVs\" + tName + ".csv";

                        if (!File.Exists(path))
                        {
                           GenerateNewCSV(dt, path);                            
                        }
                        else
                        {
                            UpdateExistingCSV(dt, path);                            
                        }
                    }
                    conn.Close();
                }
            }
        }


        private static void GenerateNewCSV(DataTable table, string path)
        {
            IEnumerable<string> colHeadings = table.Columns.OfType<DataColumn>().Select(col => col.ColumnName);
            using (StreamWriter writer = File.AppendText(path))
            {
                writer.WriteLine(string.Join(",", colHeadings));

                foreach (DataRow row in table.Rows)
                    writer.WriteLine(string.Join(",", row.ItemArray));
            }
        }


        private static void UpdateExistingCSV(DataTable table, string path)
        {
            DateTime lastDateTime = Convert.ToDateTime(File.ReadLines(path).Last().Split(',')[0]);
            using (StreamWriter writer = File.AppendText(path))
            {
                foreach (DataRow row in table.Rows)
                {
                    DateTime rowTime = Convert.ToDateTime(row.ItemArray[0]);

                    if (rowTime <= lastDateTime)
                        continue;
                    else
                        writer.WriteLine(string.Join(",", row.ItemArray));
                }
            }            
        }

    }


}
