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
        private ConcurrentQueue<SaveDataUpdate> SQLDataWrites = new ConcurrentQueue<SaveDataUpdate>();

        private const string dataFile = "EMAofRSI1trades.data";
        private SQLiteConnection conn;

        private IReadOnlyList<string> SpecificDeltas = new List<string>()
        {
            "BTC-XLM"//, "BTC-ADA", "BTC-ETH", "BTC-QTUM", "BTC-OMG"
        };

        private decimal WagerAmt = 0.001M;

        private bool VirtualOnOff = true;
        

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
                    SaveSQLData(cmd);


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
                        BtrexREST.TradeController.ExecuteNewOrderList(ords, VirtualOnOff).Start();
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
                        var rate = BtrexData.Markets[delta].TradeHistory.RecentFills.Last().Rate;
                        var amt = WagerAmt / (rate * 1.0025M);
                        NewOrders.Add(new NewOrder(delta, "BUY", amt, rate, (a) => OrderExecutedCallback(a), periodName));
                    }
                    else if (call == false && owned)
                    {
                        //ADD SELL ORDER on period
                        var rate = BtrexData.Markets[delta].TradeHistory.RecentFills.Last().Rate;
                        var amt = (decimal)Holdings.Tables[periodName].AsEnumerable().Where(o => (string)o["MarketDelta"] == delta).First()["Qty"];

                        NewOrders.Add(new NewOrder(delta, "SELL", amt, rate, (a) => OrderExecutedCallback(a), periodName));
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

        
        private void AddHoldingsTable(string periodName)
        {
            var dt = new DataTable();
            using (var sqlAdapter = new SQLiteDataAdapter("SELECT * from " + periodName + " WHERE DateTimeSELL = 'OWNED'", conn))
                sqlAdapter.Fill(dt);
            Holdings.Tables.Add(dt);
        }

        private void LoadHoldings()
        {
            //Load held assets, stoploss amts, from SQLite for each period:
            AddHoldingsTable("5m");
            AddHoldingsTable("20m");
            AddHoldingsTable("1h");
            AddHoldingsTable("4h");
            AddHoldingsTable("12h");

            //TODO: EVALUATE/EXECUTE/REGISTER NEW STOP-LOSSES FOR EACH HOLDING




        }


        public void OrderExecutedCallback(NewOrder OrderData)
        { 
            var TimeCompleted = DateTime.UtcNow;

            //Create and register stoploss
            decimal stoplossRate = OrderData.Rate - CalcStoplossMargin(OrderData.MarketDelta, OrderData.CandlePeriod);
            StopLossController.RegisterStoploss(new StopLoss(OrderData.MarketDelta, OrderData.Rate, OrderData.Qty, (a, b, c) => ReCalcStoploss(a, b, c), (a, b) => StopLossExecutedCallback(a, b), OrderData.CandlePeriod), string.Format("{0}_{1}", OrderData.CandlePeriod, OrderData.MarketDelta));

            //Pull from pending orders
            PendingOrders.RemoveAll(o => o.MarketDelta == OrderData.MarketDelta && o.CandlePeriod == OrderData.CandlePeriod);

            if (OrderData.BUYorSELL == "BUY")
            { 
                //ENTER INTO HOLDINGS:
                var newHoldingsRow = Holdings.Tables[OrderData.CandlePeriod].NewRow();
                newHoldingsRow["MarketDelta"] = OrderData.MarketDelta;
                newHoldingsRow["DateTimeBUY"] = TimeCompleted;
                newHoldingsRow["Qty"] = OrderData.Qty;
                newHoldingsRow["BoughtRate"] = OrderData.Rate;
                newHoldingsRow["DateTimeSELL"] = "OWNED";
                newHoldingsRow["SoldRate"] = "OWNED";
                newHoldingsRow["StopLossRate"] = stoplossRate;
                newHoldingsRow["SL_Executed"] = 0;
                Holdings.Tables[OrderData.CandlePeriod].Rows.Add(newHoldingsRow);

                //CREATE/ADD SQL UPDATE:
                var update = new SaveDataUpdate(OrderData.CandlePeriod, OrderData.MarketDelta, "BUY", TimeCompleted, OrderData.Qty, OrderData.Rate, stoplossRate);
                SQLDataWrites.Enqueue(update);
            }
            else if (OrderData.BUYorSELL == "SELL")
            {
                //REMOVE FROM HOLDINGS TABLE:
                var holdingRows = Holdings.Tables[OrderData.CandlePeriod].Select(string.Format("MarketDelta = '{0}'", OrderData.MarketDelta));
                foreach (var row in holdingRows)
                    Holdings.Tables[OrderData.CandlePeriod].Rows.Remove(row);

                //CREATE/ADD SQL UPDATE:             
                var update = new SaveDataUpdate(OrderData.CandlePeriod, OrderData.MarketDelta, "SELL", TimeCompleted, OrderData.Qty, OrderData.Rate);
                SQLDataWrites.Enqueue(update);
            }
            
        }


        //CALLBACK FUNCTIONS FOR STOPLOSS EXE AND CALC-MOVE:
        public void StopLossExecutedCallback(GetOrderResponse OrderResponse, string period)
        {
            //REMOVE FROM HOLDINGS:
            var holdingRows = Holdings.Tables[period].Select(string.Format("MarketDelta = '{0}'", OrderResponse.result.Exchange));
            foreach (var row in holdingRows)
                Holdings.Tables[period].Rows.Remove(row);

            //CREATE & ENQUEUE SQLDatawrite obj:



        }

        public void ReCalcStoploss(string market, decimal oldRate, string period)
        {
            //RECALC NEW STOPLOSSRATE, THEN RAISE REGISTERED RATE IF HIGHER NOW:
            var stoplossRate = BtrexData.Markets[market].TradeHistory.RecentFills.Last().Rate - CalcStoplossMargin(market, period);

            if (stoplossRate > oldRate)            
                StopLossController.RaiseStoploss(string.Format("{0}_{1}", period, market), stoplossRate);

            //TODO: CHANGE IN HOLDINGS:



            //CREATE & ENQUEUE SQLDataWrite:





        }

        //LOGIC FOR CALCLULATING STOPLOSS MARGIN
        private decimal CalcStoplossMargin(string delta, string cPeriod)
        {
            int ATRparameter = 7;
            decimal ATRmultiple = 2;
            decimal ATR = new decimal();

            switch (cPeriod)
            {
                case "5m":
                    ATR = StratData.Candles5m[delta].Skip(Math.Max(0, StratData.Candles5m[delta].Count - 10)).Atr(ATRparameter).Last().Tick.Value;
                    break;
                case "20m":
                    ATR = StratData.Candles20m[delta].Skip(Math.Max(0, StratData.Candles20m[delta].Count - 10)).Atr(ATRparameter).Last().Tick.Value;
                    break;
                case "1h":
                    ATR = StratData.Candles1h[delta].Skip(Math.Max(0, StratData.Candles1h[delta].Count - 10)).Atr(ATRparameter).Last().Tick.Value;
                    break;
                case "4h":
                    ATR = StratData.Candles4h[delta].Skip(Math.Max(0, StratData.Candles4h[delta].Count - 10)).Atr(ATRparameter).Last().Tick.Value;
                    break;
                case "12h":
                    ATR = StratData.Candles12h[delta].Skip(Math.Max(0, StratData.Candles12h[delta].Count - 10)).Atr(ATRparameter).Last().Tick.Value;
                    break;
            }
            
            return ATR * ATRmultiple;
        }

        private void SaveSQLData(SQLiteCommand cmd)
        {
            if (!SQLDataWrites.IsEmpty)
            { 
                using (var tx = conn.BeginTransaction())
                {
                    //SAVE DATA EVENT CHANGES
                    while (!SQLDataWrites.IsEmpty)
                    {

                        SaveDataUpdate update;
                        bool dqed;
                        do
                        {
                            dqed = SQLDataWrites.TryDequeue(out update);
                        } while (!dqed);

                        //Execute SaveData SQL commands:
                        if (update.BUYorSELLorSL_MOVE.ToUpper() == "BUY")
                        {
                            cmd.CommandText = string.Format("INSERT INTO {0} VALUES ({1}, {2}, {3}, {4}, {5}, {6}, {7}, {8})", update.PeriodName, update.MarketName, update.TimeStamp, update.Quantity, update.Rate, "OWNED", "OWNED", update.StopLossRate, "0");
                            cmd.ExecuteNonQuery();

                        }
                        else if (update.BUYorSELLorSL_MOVE.ToUpper() == "SELL")
                        {
                            cmd.CommandText = string.Format("UPDATE {0} SET DateTimeSELL = {1}, SoldRate = {2}, SL_Executed = {3} WHERE MarketName = {4}", update.PeriodName, update.TimeStamp, update.Rate, update.StopLossExecuted ? "1" : "0", update.MarketName);
                            cmd.ExecuteNonQuery();
                        } 
                        else if (update.BUYorSELLorSL_MOVE.ToUpper() == "SL_MOVE")
                        {
                            cmd.CommandText = string.Format("UPDATE {0} SET StopLossRate = {1} WHERE MarketName = {2}", update.PeriodName, update.StopLossRate, update.MarketName);
                            cmd.ExecuteNonQuery();
                        }   
                        
                    }

                    tx.Commit();
                }
            }
        }


    }


    

    public class SaveDataUpdate
    { 
        public string PeriodName { get; set; }
        public string MarketName { get; set; }
        public string BUYorSELLorSL_MOVE { get; set; }
        public DateTime TimeStamp{ get; set; }
        public decimal Quantity { get; set; }
        public decimal Rate { get; set; }
        public decimal? StopLossRate { get; set; }
        public bool StopLossExecuted { get; set; }

        public SaveDataUpdate(string period, string market, string buyORsellORsl_move, DateTime time, decimal qty, decimal price, decimal? SL_rate = null, bool stoplossExe = false)
        {
            PeriodName = period;
            MarketName = market;
            BUYorSELLorSL_MOVE = buyORsellORsl_move.ToUpper();
            TimeStamp = time;
            Quantity = qty;
            Rate = price;
            StopLossExecuted = stoplossExe;

            if (SL_rate != null)
                StopLossRate = SL_rate;
        }
    }



}
