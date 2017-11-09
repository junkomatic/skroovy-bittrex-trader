using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.IO;
using System.Data;
using System.Data.SQLite;
using System.Diagnostics;
using Trady.Core;
using Trady.Analysis;
using BtrexTrader.Interface;
using BtrexTrader.Data;
using BtrexTrader.Data.Market;
using BtrexTrader.Strategy.Core;

namespace BtrexTrader.Strategy.EMAofRSI1
{
    class EofR1control
    { 
        private StratData_MultiPeriods StratData = new StratData_MultiPeriods();
        private DataSet Holdings = new DataSet();
        
        private List<NewOrder> NewOrders = new List<NewOrder>();
        private List<NewOrder> PendingOrders = new List<NewOrder>();
        private ConcurrentQueue<string> SQLDataWrites = new ConcurrentQueue<string>();

        private const string dataFile = "EMAofRSI1trades.data";
        private SQLiteConnection conn;

        private IReadOnlyList<string> SpecificDeltas = new List<string>()
        {
            "BTC-XLM"//, "BTC-ADA", "BTC-ETH", "BTC-QTUM", "BTC-OMG"
        };

        private decimal WagerAmt = 0.001M;
        


        public async Task Initialize()
        {
            //await SubTopMarketsByVol(50);
            await SubSpecificMarkets();

            OpenSQLiteConn();
            LoadHoldings();

            await StratData.PreloadCandleDicts(26);            
        }

        public async Task Start()
        {
            StopLossController.StartWatching();

            using (var cmd = new SQLiteCommand(conn))
            {
                while (!(Console.KeyAvailable && Console.ReadKey(true).Key == ConsoleKey.Backspace))
                {
                    //WRITE/SAVE SQL DATA CHANGES:
                    SaveSQLData();
                    

                    //BEGIN CANDLES ASSESSMENTS:
                    foreach (Market m in BtrexData.Markets.Values)
                    {
                        //CHECK FOR NEW CANDLES:
                        if (m.TradeHistory.LastStoredCandle > StratData.Candles5m[m.MarketDelta].Last().DateTime)
                        {
                            //Get new 5m candles:
                            var Importer = new TradyCandleImporter();
                            var newCandles = await Importer.ImportAsync(m.MarketDelta, StratData.Candles5m[m.MarketDelta].Last().DateTime.AddMinutes(5));
                            StratData.Candles5m[m.MarketDelta].AddRange(newCandles);
                            CheckStrategy(StratData.Candles5m[m.MarketDelta], m.MarketDelta, "5m");

                            //Build new 20m candles + Check strategy for buy/sell signals:
                            var CandleCurrentTime = m.TradeHistory.LastStoredCandle.AddMinutes(5);
                            if (CandleCurrentTime > StratData.Candles20m[m.MarketDelta].Last().DateTime.AddMinutes(40))
                            {
                                if (StratData.BuildNew20mCndls(m.MarketDelta))
                                    CheckStrategy(StratData.Candles20m[m.MarketDelta], m.MarketDelta, "20m");

                                //Build new 1h candles + Check strategy for buy/sell signals:
                                if (CandleCurrentTime > StratData.Candles1h[m.MarketDelta].Last().DateTime.AddHours(2))
                                {
                                    if (StratData.BuildNew1hCndls(m.MarketDelta))
                                        CheckStrategy(StratData.Candles1h[m.MarketDelta], m.MarketDelta, "1h");

                                    //Build new 4h candles + Check strategy for buy/sell signals:
                                    if (CandleCurrentTime > StratData.Candles4h[m.MarketDelta].Last().DateTime.AddHours(8) && StratData.BuildNew4hCndls(m.MarketDelta))
                                        CheckStrategy(StratData.Candles4h[m.MarketDelta], m.MarketDelta, "4h");

                                    //Build new 12h candles + Check strategy for buy/sell signals:
                                    if (CandleCurrentTime > StratData.Candles12h[m.MarketDelta].Last().DateTime.AddHours(24) && StratData.BuildNew12hCndls(m.MarketDelta))
                                        CheckStrategy(StratData.Candles12h[m.MarketDelta], m.MarketDelta, "12h");
                                }
                            }
                        }                        
                        
                    }


                    //EXECUTE ALL List<NewOrders>:
                    if (NewOrders.Count > 0)
                    {
                        PendingOrders.AddRange(NewOrders);
                        var ords = new List<NewOrder>(NewOrders);
                        NewOrders.Clear();
                        BtrexREST.TradeController.ExecuteNewOrderList(ords);
                    }
                          
                    
                    Thread.Sleep(TimeSpan.FromSeconds(5));

                }
            }
            conn.Close();
            
        }
        

        private bool? EMAofRSI1_STRATEGY(List<decimal> closes)
        {
            var closesRSI = closes.Rsi(21);
            var EMAofRSI = closesRSI.Ema(14);

            if (closesRSI.Last() > EMAofRSI.Last() && closesRSI[closesRSI.Count - 2] <= EMAofRSI[EMAofRSI.Count - 2])
            {
                    //RSI has crossed above its EMA and is RISING:
                    return true;                
            }
            else if (closesRSI.Last() < EMAofRSI.Last() && closesRSI[closesRSI.Count - 2] >= EMAofRSI[EMAofRSI.Count - 2])
            {
                    //RSI has crossed below its EMA and is FALLING:
                    return false;                
            }
            
            return null;
        }


        public void CheckStrategy(List<Candle> candles, string delta, string periodName)
        {
            if (PendingOrders.Where(o => (o.CandlePeriod == periodName) && (o.MarketDelta == delta)).Count() == 0)
            {
                var closes = new List<decimal>(candles.Select(c => c.Close));
                bool? call = EMAofRSI1_STRATEGY(closes);

                if (call != null)
                {
                    bool owned = Holdings.Tables[periodName].AsEnumerable().Any(row => delta == row.Field<string>("MarketDelta"));

                    if (call == true && !owned)
                    {
                        //Add BUY order on period
                        NewOrders.Add(new NewOrder(delta, "BUY", WagerAmt, null, o => NewOrderCallback(o), periodName));
                    }
                    else if (call == false && owned)
                    {
                        //ADD SELL ORDER on period
                        NewOrders.Add(new NewOrder(delta, "SELL", WagerAmt, null, o => NewOrderCallback(o), periodName));
                    }
                }
            }

        }


        private async Task SubTopMarketsByVol(int n)
        {
            List<string> topMarkets = await BtrexREST.GetTopMarketsByBVbtcOnly(n);
            foreach (string mk in topMarkets)
                await BtrexWS.subscribeMarket("BTC-" + mk);
        }


        private async Task SubSpecificMarkets()
        {
            foreach (string mk in SpecificDeltas)
                await BtrexWS.subscribeMarket(mk);
        }


        private bool OpenSQLiteConn()
        {
            if (!File.Exists(dataFile))
            {
                Console.WriteLine("CREATING NEW '{0}' FILE...", dataFile);
                SQLiteConnection.CreateFile(dataFile);
                conn = new SQLiteConnection("Data Source=" + dataFile + ";Version=3;");
                conn.Open();
                using (var cmd = new SQLiteCommand(conn))
                {
                    using (var tx = conn.BeginTransaction())
                    {
                        cmd.CommandText = "CREATE TABLE IF NOT EXISTS 5m (MarketDelta TEXT, DateTimeBUY TEXT, Qty TEXT, BoughtRate TEXT, DateTimeSELL TEXT, SoldRate TEXT, StopLossRate TEXT, SL_Executed INTEGER)";
                        cmd.ExecuteNonQuery();
                        cmd.CommandText = "CREATE TABLE IF NOT EXISTS 20m (MarketDelta TEXT, DateTimeBUY TEXT, Qty TEXT, BoughtRate TEXT, DateTimeSELL TEXT, SoldRate TEXT, StopLossRate TEXT, SL_Executed INTEGER)";
                        cmd.ExecuteNonQuery();
                        cmd.CommandText = "CREATE TABLE IF NOT EXISTS 1h (MarketDelta TEXT, DateTimeBUY TEXT, Qty TEXT, BoughtRate TEXT, DateTimeSELL TEXT, SoldRate TEXT, StopLossRate TEXT, SL_Executed INTEGER)";
                        cmd.ExecuteNonQuery();
                        cmd.CommandText = "CREATE TABLE IF NOT EXISTS 4h (MarketDelta TEXT, DateTimeBUY TEXT, Qty TEXT, BoughtRate TEXT, DateTimeSELL TEXT, SoldRate TEXT, StopLossRate TEXT, SL_Executed INTEGER)";
                        cmd.ExecuteNonQuery();
                        cmd.CommandText = "CREATE TABLE IF NOT EXISTS 12h (MarketDelta TEXT, DateTimeBUY TEXT, Qty TEXT, BoughtRate TEXT, DateTimeSELL TEXT, SoldRate TEXT, StopLossRate TEXT, SL_Executed INTEGER)";
                        cmd.ExecuteNonQuery();

                        tx.Commit();
                    }
                }

                return true;
            }
            else
            {
                conn = new SQLiteConnection("Data Source=" + dataFile + ";Version=3;");
                conn.Open();

                return false;
            }
        }


        private void LoadHoldings()
        {
            //Load held assets, stoploss amts, from SQLite for each period:
            AddHoldingsTable("5m");
            AddHoldingsTable("20m");
            AddHoldingsTable("1h");
            AddHoldingsTable("4h");
            AddHoldingsTable("12h");          

            //TODO: REGISTER NEW STOP-LOSSES




        }

        private void AddHoldingsTable(string periodName)
        {
            var dt = new DataTable();
            using (var sqlAdapter = new SQLiteDataAdapter("SELECT * from "+ periodName +" WHERE DateTimeSELL = 'OWNED'", conn))
                sqlAdapter.Fill(dt);
            Holdings.Tables.Add(dt);
        }

        
       
        //TODO: CALLBACK FUNCTIONS FOR STOPLOSS AND ORDER CREATION/EXECUTION
        public void NewOrderCallback(string o)
        {
            //Pull from pending orders, enter into holdings, drop/save table, create stoploss and reg



        }
        

        public void StopLossCallback()
        {




        }
        

        private void SaveSQLData()
        {
            //TODO: SAVE DATASET CHANGES




        }
        

    }

    
}
