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
        private Thread EofR1ExeThread;
        private bool isStarted = false;

        private const string dataFile = "EMAofRSI1trades.data";
        private SQLiteConnection conn;

        private IReadOnlyList<string> SpecificDeltas = new List<string>()
        {
            "BTC-OMG", "BTC-XLM", "BTC-ETH", "BTC-BCC", "BTC-ADA"
        };

        private decimal WagerAmt = 0.001M;
        private decimal RunningTotal = 0.0M;
        private const int MaxMarketsEnteredPerPeriod = 10;
        private bool VirtualOnOff = true;
        

        public async Task Initialize()
        {
            await SubTopMarketsByVol(40);
            //await SubSpecificMarkets();

            OpenSQLiteConn();
            LoadHoldings();

            await StratData.PreloadCandleDicts(40);                       
        }

        public async Task Start()
        {
            StopLossController.StartWatching();

            if (!isStarted)
            {
                EofR1ExeThread = new Thread(() => WatchMarkets());
                EofR1ExeThread.IsBackground = true;
                EofR1ExeThread.Name = "EMAofRSI1-ExecutionLoop-Thread";
                EofR1ExeThread.Start();
                isStarted = true;
            }

        }

        private void WatchMarkets()
        {
            using (var cmd = new SQLiteCommand(conn))
            {
                while (true) // !(Console.KeyAvailable && Console.ReadKey(true).Key == ConsoleKey.Backspace))
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
                            var newCandles = Importer.ImportAsync(m.MarketDelta, StratData.Candles5m[m.MarketDelta].Last().DateTime.AddMinutes(5)).Result;
                            StratData.Candles5m[m.MarketDelta].AddRange(newCandles);
                            CheckStrategy(StratData.Candles5m[m.MarketDelta], m.MarketDelta, "period5m");

                            //Build new 20m candles + Check strategy for buy/sell signals:
                            var CandleCurrentTime = m.TradeHistory.LastStoredCandle.AddMinutes(5);
                            if (CandleCurrentTime > StratData.Candles20m[m.MarketDelta].Last().DateTime.AddMinutes(40))
                            {
                                if (StratData.BuildNew20mCndls(m.MarketDelta))
                                    CheckStrategy(StratData.Candles20m[m.MarketDelta], m.MarketDelta, "period20m");

                                //Build new 1h candles + Check strategy for buy/sell signals:
                                if (CandleCurrentTime > StratData.Candles1h[m.MarketDelta].Last().DateTime.AddHours(2))
                                {
                                    if (StratData.BuildNew1hCndls(m.MarketDelta))
                                        CheckStrategy(StratData.Candles1h[m.MarketDelta], m.MarketDelta, "period1h");

                                    //Build new 4h candles + Check strategy for buy/sell signals:
                                    if (CandleCurrentTime > StratData.Candles4h[m.MarketDelta].Last().DateTime.AddHours(8) && StratData.BuildNew4hCndls(m.MarketDelta))
                                        CheckStrategy(StratData.Candles4h[m.MarketDelta], m.MarketDelta, "period4h");

                                    //Build new 12h candles + Check strategy for buy/sell signals:
                                    if (CandleCurrentTime > StratData.Candles12h[m.MarketDelta].Last().DateTime.AddHours(24) && StratData.BuildNew12hCndls(m.MarketDelta))
                                        CheckStrategy(StratData.Candles12h[m.MarketDelta], m.MarketDelta, "period12h");
                                }
                            }
                        }

                    }
                    
                    //EXECUTE ALL List<NewOrders>:
                    if (NewOrders.Count > 0)
                    {
                        PendingOrders.AddRange(NewOrders);
                        var ords = new List<NewOrder>(NewOrders);
                        NewOrders = new List<NewOrder>();
                        BtrexREST.TradeController.ExecuteNewOrderList(ords, VirtualOnOff);
                    }
                    
                    Thread.Sleep(TimeSpan.FromSeconds(5));

                }
                StopLossController.Stop();
            }
            conn.Close();
        }


        private bool? EMAofRSI1_STRATEGY(List<decimal> closes)
        {
            var closesRSI = closes.Rsi(21);
            var RSIs = new List<decimal>();
            foreach (decimal? d in closesRSI)
            {
                if (d != null)
                {
                    RSIs.Add(Convert.ToDecimal(d));
                }
            }
            var EMAofRSI = RSIs.Ema(14);


            //OUTPUT RSI AND ITS EMA:
            //Console.WriteLine("    PREV RSI: {0} ... PREV EMAofRSI: {1}\r\n    CURRENT RSI: {2} ... CURRENT EMAofRSI: {3}", closesRSI[closesRSI.Count - 2], EMAofRSI[EMAofRSI.Count - 2], closesRSI.Last(), EMAofRSI.Last());


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

                //OUTPUT CANDLE:
                //var cndl = candles.Last();
                //Console.WriteLine("[NEW CANDLE|{0}|{1}] ::: T:{2} ... O:{3} ... H:{4} ... L:{5} ... C:{6} ... V:{7}", delta, periodName, cndl.DateTime, cndl.Open, cndl.High, cndl.Low, cndl.Close, cndl.Volume);


                if (call != null)
                {
                    //OUTPUT SIGNAL:
                    if (call == true)
                        Console.WriteLine("*BUY SIGNAL: {0} on {1} candles*", delta, periodName.Remove(0, 6));
                    else
                        Console.WriteLine("*SELL SIGNAL: {0} on {1} candles*", delta, periodName.Remove(0, 6));
                    
                    var held = Holdings.Tables[periodName].Select("MarketDelta = '"+ delta +"'");          //.AsEnumerable().Any(row => delta == (string)row["MarketDelta"]);
                    bool owned = false;
                    if (held.Length > 0)
                        owned = true;


                    if (call == true && !owned && (Holdings.Tables[periodName].Rows.Count < MaxMarketsEnteredPerPeriod))
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
                        //EXECUTE SELL ON SIGNAL IF RATE IS PROFITABLE (OTHERWISE BAG HOLD/STOPLOSS SELL)
                        if ((rate * 0.9975M) - Convert.ToDecimal(held[0]["BoughtRate"]) > 0)
                        {
                            var amt = Convert.ToDecimal(Holdings.Tables[periodName].AsEnumerable().Where(o => (string)o["MarketDelta"] == delta).First()["Qty"]);
                            NewOrders.Add(new NewOrder(delta, "SELL", amt, rate, (a) => OrderExecutedCallback(a), periodName));
                        }                        
                    }
                }
            }

        }


        private async Task SubTopMarketsByVol(int n)
        {
            List<string> topMarkets = await BtrexREST.GetTopMarketsByBVbtcOnly(n);
            foreach (string mk in topMarkets)
            {
                if (mk == "BTG" || mk == "POWR")
                    continue;
                await BtrexWS.subscribeMarket("BTC-" + mk);
            }
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
                        cmd.CommandText = "CREATE TABLE IF NOT EXISTS period5m (MarketDelta TEXT, DateTimeBUY TEXT, Qty TEXT, BoughtRate TEXT, DateTimeSELL TEXT, SoldRate TEXT, StopLossRate TEXT, SL_Executed INTEGER)";
                        cmd.ExecuteNonQuery();
                        cmd.CommandText = "CREATE TABLE IF NOT EXISTS period20m (MarketDelta TEXT, DateTimeBUY TEXT, Qty TEXT, BoughtRate TEXT, DateTimeSELL TEXT, SoldRate TEXT, StopLossRate TEXT, SL_Executed INTEGER)";
                        cmd.ExecuteNonQuery();
                        cmd.CommandText = "CREATE TABLE IF NOT EXISTS period1h (MarketDelta TEXT, DateTimeBUY TEXT, Qty TEXT, BoughtRate TEXT, DateTimeSELL TEXT, SoldRate TEXT, StopLossRate TEXT, SL_Executed INTEGER)";
                        cmd.ExecuteNonQuery();
                        cmd.CommandText = "CREATE TABLE IF NOT EXISTS period4h (MarketDelta TEXT, DateTimeBUY TEXT, Qty TEXT, BoughtRate TEXT, DateTimeSELL TEXT, SoldRate TEXT, StopLossRate TEXT, SL_Executed INTEGER)";
                        cmd.ExecuteNonQuery();
                        cmd.CommandText = "CREATE TABLE IF NOT EXISTS period12h (MarketDelta TEXT, DateTimeBUY TEXT, Qty TEXT, BoughtRate TEXT, DateTimeSELL TEXT, SoldRate TEXT, StopLossRate TEXT, SL_Executed INTEGER)";
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

            dt.TableName = periodName;
            Holdings.Tables.Add(dt);
        }

        private void LoadHoldings()
        {
            //Load held assets, stoploss amts, from SQLite for each period:
            AddHoldingsTable("period5m");
            AddHoldingsTable("period20m");
            AddHoldingsTable("period1h");
            AddHoldingsTable("period4h");
            AddHoldingsTable("period12h");

            //REGISTER EXISTING STOPLOSS RATES FOR EACH HOLDING
            foreach (DataTable dt in Holdings.Tables)
            {
                foreach (DataRow row in dt.Rows)
                {
                    Console.WriteLine("TABLE: {0}, MARKETNAME: {1}, SL_RATE: {2:0.00000000}", dt.TableName, row["MarketDelta"], Convert.ToDecimal(row["StopLossRate"]));
                    var stopLoss = new StopLoss((string)row["MarketDelta"], Convert.ToDecimal(row["StopLossRate"]), Convert.ToDecimal(row["Qty"]), (a, b, c) => ReCalcStoploss(a, b, c), (a, b) => StopLossExecutedCallback(a, b), dt.TableName, VirtualOnOff);
                    StopLossController.RegisterStoploss(stopLoss, string.Format("{0}_{1}", stopLoss.CandlePeriod, stopLoss.MarketDelta));
                    Console.WriteLine("    >>>SL RE-REGISTERED");
                }
            }
            
        }


        public void OrderExecutedCallback(NewOrder OrderData)
        { 
            var TimeCompleted = DateTime.UtcNow;

            //Pull from pending orders
            PendingOrders.RemoveAll(o => o.MarketDelta == OrderData.MarketDelta && o.CandlePeriod == OrderData.CandlePeriod);

            if (OrderData.BUYorSELL == "BUY")
            {
                //Create and register stoploss
                decimal stoplossRate = OrderData.Rate - CalcStoplossMargin(OrderData.MarketDelta, OrderData.CandlePeriod);
                StopLossController.RegisterStoploss(new StopLoss(OrderData.MarketDelta, stoplossRate, OrderData.Qty, (a, b, c) => ReCalcStoploss(a, b, c), (a, b) => StopLossExecutedCallback(a, b), OrderData.CandlePeriod, VirtualOnOff), string.Format("{0}_{1}", OrderData.CandlePeriod, OrderData.MarketDelta));
                
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

                //OUTPUT BOUGHT ON BUYSIGNAL:
                Console.WriteLine("{0}{1} Bought {2} at {3}, SL_Rate: {4:0.00000000}",
                    VirtualOnOff ? "[VIRTUAL|" + TimeCompleted + "] ::: " : "["+ TimeCompleted +"] ::: ", 
                    OrderData.CandlePeriod.Remove(0, 6),
                    OrderData.MarketDelta.Split('-')[1],
                    OrderData.Rate,
                    stoplossRate);
            }
            else if (OrderData.BUYorSELL == "SELL")
            {
                //CANCEL STOPLOSS
                StopLossController.CancelStoploss(string.Format("{0}_{1}", OrderData.CandlePeriod, OrderData.MarketDelta));
                
                //FIND + REMOVE FROM HOLDINGS TABLE:
                var holdingRows = Holdings.Tables[OrderData.CandlePeriod].Select(string.Format("MarketDelta = '{0}'", OrderData.MarketDelta));

                //OUTPUT SOLD ON SELLSIGNAL:
                Console.Write("{0}{1} Sold {2} at {3}\r\n    =PROFIT: ", 
                    VirtualOnOff ? "[VIRTUAL|" + TimeCompleted + "] ::: " : "[" + TimeCompleted + "] ::: ",
                    OrderData.CandlePeriod.Remove(0, 6),
                    OrderData.MarketDelta.Split('-')[1],
                    OrderData.Rate);
                //CALC PROFIT WITH BOUGHT RATE AND FEES INCLUDED, OUTPUT:
                var profit = ((OrderData.Rate / Convert.ToDecimal(holdingRows[0]["BoughtRate"])) - 1M);
                RunningTotal += profit;
                if (profit < 0)
                    Console.ForegroundColor = ConsoleColor.Red;
                else if (profit > 0)
                    Console.ForegroundColor = ConsoleColor.Green;
                Console.Write("{0:+0.###%;-0.###%;0}", profit);
                Console.ForegroundColor = ConsoleColor.DarkCyan;
                Console.Write(" ..... =Time-Held: {0} ..... ",                    
                    //CALC HELD TIME:
                    TimeCompleted - Convert.ToDateTime(holdingRows[0]["DateTimeBUY"]));
                if (RunningTotal < 0)
                    Console.ForegroundColor = ConsoleColor.Red;
                else if (RunningTotal > 0)
                    Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine("=RunningTotal: {0:+0.###%;-0.###%;0}", RunningTotal);
                Console.ForegroundColor = ConsoleColor.DarkCyan;

                foreach (var row in holdingRows)
                    Holdings.Tables[OrderData.CandlePeriod].Rows.Remove(row);

                //CREATE/ADD SQL UPDATE:             
                var update = new SaveDataUpdate(OrderData.CandlePeriod, OrderData.MarketDelta, "SELL", TimeCompleted, OrderData.Qty, OrderData.Rate);
                SQLDataWrites.Enqueue(update);
                                
            }
            
        }


        //CALLBACK FUNCTIONS FOR STOPLOSS EXE AND CALC-MOVE:
        public void StopLossExecutedCallback(GetOrderResult OrderResponse, string period)
        {
            var TimeExecuted = DateTime.UtcNow;
            
            //FIND + REMOVE FROM HOLDINGS:
            var holdingRows = Holdings.Tables[period].Select(string.Format("MarketDelta = '{0}'", OrderResponse.Exchange));

            //OUTPUT STOPLOSS EXECUTED:
            Console.Write("{0}{1} STOPLOSS-Sold {2} at {3:0.00000000}\r\n    =PROFIT: ",
                    VirtualOnOff ? "[VIRTUAL|" + TimeExecuted + "] ::: " : "[" + TimeExecuted + "] ::: ",
                    period.Remove(0, 6),
                    OrderResponse.Exchange.Split('-')[1],
                    OrderResponse.PricePerUnit);
            //CALC PROFIT WITH BOUGHT RATE AND FEES INCLUDED, OUTPUT:
            var profit = ((OrderResponse.PricePerUnit / Convert.ToDecimal(holdingRows[0]["BoughtRate"])) - 1M);
            RunningTotal += profit;
            if (profit < 0)
                Console.ForegroundColor = ConsoleColor.Red;
            else if (profit > 0)
                Console.ForegroundColor = ConsoleColor.Green;
            Console.Write("{0:+0.###%;-0.###%;0}", profit);
            Console.ForegroundColor = ConsoleColor.DarkCyan;
            Console.Write(" ..... =Time-Held: {0} ..... ",
                //CALC HELD TIME, OUTPUT:
                TimeExecuted - Convert.ToDateTime(holdingRows[0]["DateTimeBUY"]));
            if (RunningTotal < 0)
                Console.ForegroundColor = ConsoleColor.Red;
            else if (RunningTotal > 0)
                Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("=RunningTotal: {0:+0.###%;-0.###%;0}", RunningTotal);
            Console.ForegroundColor = ConsoleColor.DarkCyan;


            foreach (var row in holdingRows)
                Holdings.Tables[period].Rows.Remove(row);

            //CREATE & ENQUEUE SQLDatawrite obj:
            var update = new SaveDataUpdate(period, OrderResponse.Exchange, "SELL", TimeExecuted, OrderResponse.Quantity, OrderResponse.PricePerUnit, null, true);
            SQLDataWrites.Enqueue(update);
                        
        }

        public void ReCalcStoploss(string market, decimal oldRate, string period)
        {
            //RECALC NEW STOPLOSSRATE, THEN RAISE REGISTERED RATE IF HIGHER NOW:
            var stoplossRate = BtrexData.Markets[market].TradeHistory.RecentFills.Last().Rate - CalcStoplossMargin(market, period);

            if (Math.Round(stoplossRate, 8) > Math.Round(oldRate, 8))
            {
                var SLmovedTime = DateTime.UtcNow;
                //RAISE SL:
                StopLossController.RaiseStoploss(string.Format("{0}_{1}", period, market), stoplossRate);

                //CHANGE IN HOLDINGS:
                var row = Holdings.Tables[period].Select(string.Format("MarketDelta = '{0}'", market));
                row[0]["StopLossRate"] = stoplossRate;

                //CREATE & ENQUEUE SQLDataWrite:
                var update = new SaveDataUpdate(period, market, "SL_MOVE", SLmovedTime, 0, 0, stoplossRate);
                SQLDataWrites.Enqueue(update);

                //OUTPUT STOPLOSS MOVED:
                Console.WriteLine("{0}{1}_{2} STOPLOSS-RAISED from {3:0.00000000} to {4:0.00000000}",
                    "[" + SLmovedTime + "] ::: ",
                    period.Remove(0, 6),
                    market,
                    oldRate,
                    stoplossRate
                    );

            }

        }

        //LOGIC FOR CALCLULATING STOPLOSS MARGIN
        private decimal CalcStoplossMargin(string delta, string cPeriod)
        {
            int ATRparameter = 5;
            decimal ATRmultiple = 3M;
            decimal ATR = new decimal();

            switch (cPeriod)
            {
                case "period5m":
                    ATR = StratData.Candles5m[delta].Skip(Math.Max(0, StratData.Candles5m[delta].Count - 10)).Atr(ATRparameter).Last().Tick.Value;
                    break;
                case "period20m":
                    ATR = StratData.Candles20m[delta].Skip(Math.Max(0, StratData.Candles20m[delta].Count - 10)).Atr(ATRparameter).Last().Tick.Value;
                    break;
                case "period1h":
                    ATR = StratData.Candles1h[delta].Skip(Math.Max(0, StratData.Candles1h[delta].Count - 10)).Atr(ATRparameter).Last().Tick.Value;
                    break;
                case "period4h":
                    ATR = StratData.Candles4h[delta].Skip(Math.Max(0, StratData.Candles4h[delta].Count - 10)).Atr(ATRparameter).Last().Tick.Value;
                    break;
                case "period12h":
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
                            cmd.CommandText = string.Format("INSERT INTO {0}(MarketDelta, DateTimeBUY, Qty, BoughtRate, DateTimeSELL, SoldRate, StopLossRate, SL_Executed) VALUES ('{1}', '{2}', '{3}', '{4}', '{5}', '{6}', '{7}', '{8}')", update.PeriodName, update.MarketName, update.TimeStamp, update.Quantity, update.Rate, "OWNED", "OWNED", update.StopLossRate, "0");
                            cmd.ExecuteNonQuery();

                        }
                        else if (update.BUYorSELLorSL_MOVE.ToUpper() == "SELL")
                        {
                            cmd.CommandText = string.Format("UPDATE {0} SET DateTimeSELL = '{1}', SoldRate = '{2}', SL_Executed = '{3}' WHERE MarketDelta = '{4}'", update.PeriodName, update.TimeStamp, update.Rate, update.StopLossExecuted ? "1" : "0", update.MarketName);
                            cmd.ExecuteNonQuery();
                        } 
                        else if (update.BUYorSELLorSL_MOVE.ToUpper() == "SL_MOVE")
                        {
                            cmd.CommandText = string.Format("UPDATE {0} SET StopLossRate = '{1}' WHERE MarketDelta = '{2}'", update.PeriodName, update.StopLossRate, update.MarketName);
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
