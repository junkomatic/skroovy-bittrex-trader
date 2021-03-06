﻿using System;
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
        //SET OPTIONS HERE PRIOR TO USE: 
        internal static class OPTIONS
        {
            public const decimal BTCwagerAmt = 0.0016M;
            public const int MaxMarketsEnteredPerPeriod = 8;
            public const int MAXTOTALENTRANCES = 40;
            public const decimal ATRmultipleT1 = 2.5M;
            public const decimal ATRmultipleT2 = 2M;
            public const decimal ATRmultipleT3 = 1.5M;

            public static bool SAFE_MODE = true;
            public const bool VITRUAL_MODE = true;
            public const bool COMPOUND_WAGER = true;

            public const bool LogStoplossRaised = true;
            public const bool LogSignals = true;

            //EXLUDE MARKETS THAT WERE RELEASED LESS THAN A MONTH AGO,
            //(CANT PRELOAD ADEQUATE CANDLE DATA FOR CALCS -> SequenceContainsNoElements err)
            public static IReadOnlyList<string> ExcludeTheseDeltas = new List<string>()
            {
                "SWIFT", "GEO"
            };

            //ENTER MARKETS HERE FOR USE IN SubSpecificDeltas() METHOD:
            public static IReadOnlyList<string> SubSpecificDeltas = new List<string>()
            {
                "BTC-OMG", "BTC-XLM", "BTC-NEO", "BTC-BCC", "BTC-ADA"
            };
        }


        private StratData_MultiPeriods StratData = new StratData_MultiPeriods();
        private DataSet Holdings = new DataSet();
        private decimal TradingTotal = 0.0M;

        private const string TradesDataFile = "EMAofRSI1trades.data";
        private SQLiteConnection conn;

        private List<NewOrder> NewOrders = new List<NewOrder>();
        private ConcurrentQueue<SaveDataUpdate> SQLDataUpdateWrites = new ConcurrentQueue<SaveDataUpdate>();
        private ConcurrentQueue<OpenOrder> SQLOrderUpdateWrites = new ConcurrentQueue<OpenOrder>();
        private Thread EofR1ExeThread;
        private bool isStarted = false;

        

        public async Task Initialize()
        {
            OpenSQLiteConn();
            
            LoadHoldings();
            
            LoadOpenOrders();
            
            await SubTopMarketsByVol(60);

            //await SubSpecificMarkets();                   

            await StratData.PreloadCandleDicts(42);

            DisplayHoldings();
        }

        public void Start()
        {
            if (!isStarted)
            {
                EofR1ExeThread = new Thread(() => WatchMarkets());
                EofR1ExeThread.IsBackground = true;
                EofR1ExeThread.Name = "EMAofRSI1-ExecutionLoop-Thread";
                EofR1ExeThread.Start();
                isStarted = true;
            }

            StopLossController.StartWatching();
        }

        private void WatchMarkets()
        {
            using (var cmd = new SQLiteCommand(conn))
            {
                while (true) //!(Console.KeyAvailable && Console.ReadKey(true).Key == ConsoleKey.Backspace))
                {
                    //WRITE/SAVE SQL DATA CHANGES:
                    SaveSQLOrderData(cmd);
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
                            CheckStrategy(StratData.Candles5m[m.MarketDelta].Skip(Math.Max(0, StratData.Candles5m[m.MarketDelta].Count - 42)).ToList(), m.MarketDelta, "period5m");

                            //Build new 20m candles + Check strategy for buy/sell signals:
                            var CandleCurrentTime = m.TradeHistory.LastStoredCandle.AddMinutes(5);
                            if (CandleCurrentTime > StratData.Candles20m[m.MarketDelta].Last().DateTime.AddMinutes(40))
                            {
                                if (StratData.BuildNew20mCndls(m.MarketDelta))
                                    CheckStrategy(StratData.Candles20m[m.MarketDelta].Skip(Math.Max(0, StratData.Candles20m[m.MarketDelta].Count - 42)).ToList(), m.MarketDelta, "period20m");

                                //Build new 1h candles + Check strategy for buy/sell signals:
                                if (CandleCurrentTime > StratData.Candles1h[m.MarketDelta].Last().DateTime.AddHours(2))
                                {
                                    if (StratData.BuildNew1hCndls(m.MarketDelta))
                                        CheckStrategy(StratData.Candles1h[m.MarketDelta].Skip(Math.Max(0, StratData.Candles1h[m.MarketDelta].Count - 42)).ToList(), m.MarketDelta, "period1h");

                                    //Build new 4h candles + Check strategy for buy/sell signals:
                                    if (CandleCurrentTime > StratData.Candles4h[m.MarketDelta].Last().DateTime.AddHours(8) && StratData.BuildNew4hCndls(m.MarketDelta))
                                        CheckStrategy(StratData.Candles4h[m.MarketDelta].Skip(Math.Max(0, StratData.Candles4h[m.MarketDelta].Count - 42)).ToList(), m.MarketDelta, "period4h");

                                    //Build new 12h candles + Check strategy for buy/sell signals:
                                    if (CandleCurrentTime > StratData.Candles12h[m.MarketDelta].Last().DateTime.AddHours(24) && StratData.BuildNew12hCndls(m.MarketDelta))
                                        CheckStrategy(StratData.Candles12h[m.MarketDelta].Skip(Math.Max(0, StratData.Candles12h[m.MarketDelta].Count - 42)).ToList(), m.MarketDelta, "period12h");
                                }
                            }
                        }

                    }
                    

                    //EXECUTE ALL List<NewOrders>:
                    if (NewOrders.Count > 0)
                    {
                        //PendingOrders.AddRange(NewOrders);
                        var ords = new List<NewOrder>(NewOrders);
                        NewOrders = new List<NewOrder>();

                        //This is not awaited because NewOrder objects reference their own callback
                        BtrexREST.TradeController.ExecuteNewOrderList(ords, OPTIONS.VITRUAL_MODE);
                    }
                    

                    Thread.Sleep(TimeSpan.FromSeconds(3));

                }

                //CONTROL-LOOP ENDED:
                StopLossController.Stop();
            }
            
            conn.Close();
            isStarted = false;
            Trace.WriteLine("\r\n\r\n    @@@ EMAofRSI1 Strategy STOPPED @@@\r\n\r\n");
        }


        private bool? EMAofRSI1_STRATEGY(List<decimal> closes)
        {
            try
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
            }
            catch (Exception e)
            {

                Trace.WriteLine("   ERR>>> EMAofRSI1_STRATEGY => " + e.Message);
            }

            return null;
        }


        public void CheckStrategy(List<Candle> candles, string delta, string periodName)
        {
            //If not already in pending OpenOrders:
            if (Holdings.Tables["OpenOrders"].Select("Exchange = '" + delta + "' AND CandlePeriod = '" + periodName + "'").Count() == 0)
            {
                var closes = new List<decimal>(candles.Select(c => c.Close));
                bool? call = EMAofRSI1_STRATEGY(closes);

                //DEBUG OUTPUT LAST CANDLE:
                //var cndl = candles.Last();
                //Trace.WriteLine("[NEW CANDLE|{0}|{1}] ::: T:{2} ... O:{3} ... H:{4} ... L:{5} ... C:{6} ... V:{7}", 
                    //delta, periodName, cndl.DateTime, cndl.Open, cndl.High, cndl.Low, cndl.Close, cndl.Volume);
                
                if (call != null)
                {
                    //OUTPUT SIGNAL:
                    if (OPTIONS.LogSignals)
                    {
                        if (call == true)
                            Trace.WriteLine(string.Format("*BUY SIGNAL: {0} on {1} candles*", delta, periodName.Remove(0, 6)));
                        else
                            Trace.WriteLine(string.Format("*SELL SIGNAL: {0} on {1} candles*", delta, periodName.Remove(0, 6)));
                    }

                    var held = Holdings.Tables[periodName].Select("MarketDelta = '"+ delta +"'");
                    bool owned = false;
                    if (held.Length > 0)
                        owned = true;


                    if (call == true && !owned && (Holdings.Tables[periodName].Rows.Count + NewOrders.Where(o => o.CandlePeriod == periodName).Count() < OPTIONS.MaxMarketsEnteredPerPeriod))
                    {
                        //Add BUY order on period
                        var rate = BtrexData.Markets[delta].TradeHistory.RecentFills.Last().Rate;
                        if (rate >= 0.000001M)
                        {
                            var wagerMultiple = 1M;
                            if (OPTIONS.COMPOUND_WAGER)
                                wagerMultiple = (TradingTotal / OPTIONS.MAXTOTALENTRANCES) + 1;

                            var amt = (OPTIONS.BTCwagerAmt * wagerMultiple) / (rate * 1.0025M);
                            var ID = string.Format("{0}_{1}", periodName, delta);

                            NewOrders.Add(new NewOrder(ID, delta, "BUY", amt, rate, (a) => OrderDataCallback(a), (a) => OrderExecutedCallback(a), periodName));
                        }
                        
                    }
                    else if (call == false && owned)
                    {
                        //ADD SELL ORDER on period
                        var rate = BtrexData.Markets[delta].TradeHistory.RecentFills.Last().Rate;
                        //EXECUTE SELL ON SIGNAL IF RATE IS PROFITABLE (OTHERWISE BAG HOLD->STOPLOSS SELL)
                        if ((rate * 0.9975M) - Convert.ToDecimal(held[0]["BoughtRate"]) > 0)
                        {
                            var amt = Convert.ToDecimal(Holdings.Tables[periodName].AsEnumerable().Where(o => (string)o["MarketDelta"] == delta).First()["Qty"]);
                            var ID = string.Format("{0}_{1}", periodName, delta);

                            NewOrders.Add(new NewOrder(ID, delta, "SELL", amt, rate, (a) => OrderDataCallback(a), (a) => OrderExecutedCallback(a), periodName));
                        }                        
                    }
                }
            }

        }


        private async Task SubTopMarketsByVol(int n)
        {
            List<string> topMarkets = await BtrexREST.GetTopMarketsByBVbtcOnly(n);
            
            //ADD DELTAS FROM HOLDINGS TABLE
            foreach (DataTable dt in Holdings.Tables)
            {
                foreach (DataRow row in dt.Rows)
                {
                    if (!topMarkets.Exists(o => o == row["MarketDelta"].ToString().Split('-')[1]))
                    {
                        topMarkets.Add((string)row["MarketDelta"].ToString().Split('-')[1]);
                    }
                }
            }
            
            //SUBSCRIBE ALL MARKETS, RETRY FAILED AT END:
            List<MarketQueryResponse> timeGapMarkets = await BtrexWS.SubscribeMarketsList(topMarkets.Except(OPTIONS.ExcludeTheseDeltas).ToList());

            if (timeGapMarkets.Count > 0)
                await RetryTimeGappedMarkets(timeGapMarkets);
            
        }

        private async Task RetryTimeGappedMarkets(List<MarketQueryResponse> markets)
        {
            do
            {
                var mkQueries = new List<MarketQueryResponse>(markets);

                foreach (MarketQueryResponse mk in mkQueries)
                {
                    bool opened = await BtrexData.TryOpenMarket(mk);
                    if (opened)
                        markets.Remove(mk);
                }

                if (markets.Count == 0)
                    break;

                for (int i = 10; i > 0; i--)
                {
                    Trace.Write(string.Format("\r    ({0}) TimeGapped Markets (TradeFills->Candles), Retry in {1} seconds...\r", markets.Count, i));
                    Thread.Sleep(1000);

                }
                Trace.Write("\r                                                                                          \r");

            } while (markets.Count > 0);
            
        }


        private async Task SubSpecificMarkets()
        {
            foreach (string mk in OPTIONS.SubSpecificDeltas)
                await BtrexWS.SubscribeMarket(mk);
        }        
        
        
        
        private void LoadHoldings()
        {
            //Load held assets, stoploss amts, from SQLite for each period:            
            AddHoldingsTable("period5m");
            AddHoldingsTable("period20m");
            AddHoldingsTable("period1h");
            AddHoldingsTable("period4h");
            AddHoldingsTable("period12h");
            AddHoldingsTable("OpenOrders");
                       

            //REGISTER EXISTING STOPLOSS RATES FOR EACH HOLDING
            foreach (DataTable dt in Holdings.Tables)
            {                
                if (dt.TableName == "OpenOrders")
                    continue;

                foreach (DataRow row in dt.Rows)
                {
                    var stopLoss = new StopLoss((string)row["MarketDelta"], Convert.ToDecimal(row["StopLossRate"]), Convert.ToDecimal(row["Qty"]), (a, b, c) => ReCalcStoploss(a, b, c), (a, b) => StopLossExecutedCallback(a, b), dt.TableName, OPTIONS.VITRUAL_MODE);

                    if (OPTIONS.SAFE_MODE)
                    {
                        if (Convert.ToDecimal(row["BoughtRate"]) > stopLoss.StopRate)
                            stopLoss.StopRate = Convert.ToDecimal(row["BoughtRate"]) * 0.68M;
                    }
                    //Trace.WriteLine(string.Format("{0}_{1} ... {2} ... {3} ... {4}", stopLoss.CandlePeriod, stopLoss.MarketDelta, stopLoss.Quantity, stopLoss.StopRate, stopLoss.virtualSL));

                    StopLossController.RegisterStoploss(stopLoss, string.Format("{0}_{1}", stopLoss.CandlePeriod, stopLoss.MarketDelta));
                }
            }
                        

            //Load Total From SQLite data:
            LoadTradingTotal();

        }


        private void AddHoldingsTable(string tableName)
        {
            var dt = new DataTable();

            if (tableName == "OpenOrders")
            {
                using (var sqlAdapter = new SQLiteDataAdapter("SELECT * from " + tableName, conn))
                    sqlAdapter.Fill(dt);
            }
            else
            {
                using (var sqlAdapter = new SQLiteDataAdapter("SELECT * from " + tableName + " WHERE DateTimeSELL = 'OWNED'", conn))
                    sqlAdapter.Fill(dt);
            }
            

            dt.TableName = tableName;
            Holdings.Tables.Add(dt);
        }


        private void LoadTradingTotal()
        {
            using (var cmd = new SQLiteCommand(conn))
            {
                cmd.CommandText = "SELECT PercentageTotal FROM totals LIMIT 1";
                TradingTotal = Convert.ToDecimal(cmd.ExecuteScalar());
            }

        }


        private void LoadOpenOrders()
        {
            var dt = new DataTable();
            using (var sqlAdapter = new SQLiteDataAdapter("SELECT * from OpenOrders", conn))
                sqlAdapter.Fill(dt);

            foreach (DataRow row in Holdings.Tables["OpenOrders"].Rows)
            {
                //ADD OpenOrder obj TO OpenOrders ConcDICT in TradeControl 
                var openOrd = new OpenOrder((string)row["OrderUuid"], (string)row["Exchange"], (string)row["Type"], Convert.ToDecimal(row["TotalQuantity"]), Convert.ToDecimal(row["TotalReserved"]), Convert.ToDecimal(row["Quantity"]), Convert.ToDecimal(row["QuantityRemaining"]), Convert.ToDecimal(row["Limit"]), Convert.ToDecimal(row["Reserved"]), Convert.ToDecimal(row["CommissionReserved"]), Convert.ToDecimal(row["CommissionReservedRemaining"]), Convert.ToDecimal(row["CommissionPaid"]), Convert.ToDecimal(row["Price"]), Convert.ToDecimal(row["PricePerUnit"]), Convert.ToDateTime(row["Opened"]), (a) => OrderDataCallback(a), (a) => OrderExecutedCallback(a), (string)row["CandlePeriod"]);
                BtrexREST.TradeController.RegisterOpenOrder(openOrd, (string)row["UniqueID"]);
            }

        }


        private void DisplayHoldings()
        {
            if (!Holdings.Tables.Cast<DataTable>().Any(x => x.DefaultView.Count > 0))
            {
                Trace.WriteLine("\r\n>>>NO ASSETS HELD CURRENTLY");
                return;
            }
            
            Trace.WriteLine("\r\n>>>ASSETS HELD:");
            var netWorth = TradingTotal;
            
            foreach (DataTable dt in Holdings.Tables)
            {
                foreach (DataRow row in dt.Rows)
                {
                    decimal currentMargin;
                    if (!BtrexData.Markets.ContainsKey(row["MarketDelta"].ToString()))
                        currentMargin = 0.0M;
                    else
                        currentMargin = (BtrexData.Markets[row["MarketDelta"].ToString()].TradeHistory.RecentFills.Last().Rate / Convert.ToDecimal(row["BoughtRate"])) - 1;
                    netWorth += currentMargin;
                    Trace.WriteLine(string.Format("    {0,15}, SL_RATE: {1:0.00000000} .....{2,10:+0.###%;-0.###%;0%;}", dt.TableName.Remove(0, 6) + "_" + row["MarketDelta"], Convert.ToDecimal(row["StopLossRate"]), currentMargin));
                }
            }

            var TimeCreated = new DateTime();
            using (var cmd = new SQLiteCommand(conn))
            {
                cmd.CommandText = "SELECT TimeCreatedUTC FROM totals LIMIT 1";
                TimeCreated = Convert.ToDateTime(cmd.ExecuteScalar());
            }

            Trace.WriteLine(string.Format("CreationTimeUTC: {0}", TimeCreated));

            if (TradingTotal > 0)
                Console.ForegroundColor = ConsoleColor.DarkGreen;
            else if (TradingTotal < 0)
                Console.ForegroundColor = ConsoleColor.DarkRed;
            else
                Console.ForegroundColor = ConsoleColor.DarkCyan;            
            Trace.Write(string.Format("=TradingTotal: {0:+0.###%;-0.###%;0%} ... =GrossProfit: {1:+0.###%;-0.###%;0%}", TradingTotal, (TradingTotal / OPTIONS.MAXTOTALENTRANCES)));

            Console.ForegroundColor = ConsoleColor.DarkCyan;
            Trace.Write(" ... ");

            if (netWorth > 0)
                Console.ForegroundColor = ConsoleColor.Green;
            else if (netWorth < 0)
                Console.ForegroundColor = ConsoleColor.Red;
            else
                Console.ForegroundColor = ConsoleColor.DarkCyan;
            Trace.WriteLine(string.Format("=NET WORTH: {0:+0.###%;-0.###%;+0%}", netWorth / OPTIONS.MAXTOTALENTRANCES));
            Console.ForegroundColor = ConsoleColor.DarkCyan;

        }

        
        private decimal GetNetPercentage()
        {
            var netWorth = TradingTotal;
            
            foreach (DataTable dt in Holdings.Tables)
            {
                foreach (DataRow row in dt.Rows)
                {
                    if (!BtrexData.Markets.ContainsKey(row["MarketDelta"].ToString()))
                        continue;
                    var currentMargin = ((BtrexData.Markets[row["MarketDelta"].ToString()].TradeHistory.RecentFills.Last().Rate * 0.9975M) / Convert.ToDecimal(row["BoughtRate"])) - 1;
                    netWorth += currentMargin;
                 }
            }

            return netWorth / OPTIONS.MAXTOTALENTRANCES;
        }


        public void OrderDataCallback(OpenOrder OrderData)
        {
            //CREATE/UPDATE DATA ENTRIES IN OpenOrders AND ENQUEUE SQL SAVE-ORDER-UPDATE
            var UniqueID = string.Format("UniqueID = '{0}_{1}'", OrderData.CandlePeriod, OrderData.Exchange);
            var rows = Holdings.Tables["OpenOrders"].Select(string.Format("UniqueID = '{0}'", UniqueID));
            DataRow row;

            if (rows.Count() == 0)
            {
                row = Holdings.Tables["OpenOrders"].NewRow();
                Holdings.Tables["OpenOrders"].Rows.Add(row);
            }
            else
            {
                row = rows.First();                
            }

            row["UniqueID"] = UniqueID;
            row["OrderUuid"] = OrderData.OrderUuid;
            row["Exchange"] = OrderData.Exchange;
            row["Type"] = OrderData.Type;
            row["TotalQuantity"] = OrderData.TotalQuantity;
            row["TotalReserved"] = OrderData.TotalReserved;
            row["Quantity"] = OrderData.Quantity;
            row["QuantityRemaining"] = OrderData.QuantityRemaining;
            row["Limit"] = OrderData.Limit;
            row["Reserved"] = OrderData.Reserved;
            row["CommissionReserved"] = OrderData.CommissionReserved;
            row["CommissionReserveRemaining"] = OrderData.CommissionReserveRemaining;
            row["CommissionPaid"] = OrderData.CommissionPaid;
            row["Price"] = OrderData.Price;
            row["PricePerUnit"] = OrderData.PricePerUnit;
            row["Opened"] = OrderData.Opened;
            row["CandlePeriod"] = OrderData.CandlePeriod;
            
            SQLOrderUpdateWrites.Enqueue(OrderData);

        }


        public void OrderExecutedCallback(OpenOrder OrderData)
        {
            //Find + REMOVE FROM OpenOrders TABLE, 
            var OpenOrderRows = Holdings.Tables["OpenOrders"].Select(string.Format("UniqueID = '{0}_{1}'", OrderData.CandlePeriod, OrderData.Exchange));
            foreach (var row in OpenOrderRows)
                    Holdings.Tables["OpenOrders"].Rows.Remove(row);
            
            OrderData.PricePerUnit = Math.Round((OrderData.TotalReserved / OrderData.TotalQuantity), 8);

            if (OrderData.Type == "LIMIT_BUY")
            {
                //Calculate stoploss, within acceptable range per PeriodLength:
                decimal stoplossRate = OrderData.PricePerUnit - (CalcStoplossMargin(OrderData.Exchange, OrderData.CandlePeriod) * OPTIONS.ATRmultipleT1);

                switch (OrderData.CandlePeriod)
                {
                    case "period5m":
                        if (stoplossRate / OrderData.PricePerUnit < 0.98M)
                            stoplossRate = OrderData.PricePerUnit * 0.98M;
                        break;
                    case "period20m":
                        if (stoplossRate / OrderData.PricePerUnit < 0.95M)
                            stoplossRate = OrderData.PricePerUnit * 0.95M;
                        break;
                    case "period1h":
                        if (stoplossRate / OrderData.PricePerUnit < 0.93M)
                            stoplossRate = OrderData.PricePerUnit * 0.93M;
                        break;
                    case "period4h":
                        if (stoplossRate / OrderData.PricePerUnit < 0.90M)
                            stoplossRate = OrderData.PricePerUnit * 0.90M;
                        break;
                    case "period12h":
                        if (stoplossRate / OrderData.PricePerUnit < 0.88M)
                            stoplossRate = OrderData.PricePerUnit * 0.88M;
                        break;
                }

                //If 'SAFEMMODE' then Stoploss will be set to sell at minimum satoshis until profitable:
                if (OPTIONS.SAFE_MODE)
                    stoplossRate = 0.00105M / OrderData.TotalQuantity;

                //Register new StopLoss in controller:
                StopLossController.RegisterStoploss(new StopLoss(OrderData.Exchange, stoplossRate, OrderData.TotalQuantity, (a, b, c) => ReCalcStoploss(a, b, c), (a, b) => StopLossExecutedCallback(a, b), OrderData.CandlePeriod, OPTIONS.VITRUAL_MODE), string.Format("{0}_{1}", OrderData.CandlePeriod, OrderData.Exchange));

                //Enter into Holdings Table:
                var newHoldingsRow = Holdings.Tables[OrderData.CandlePeriod].NewRow();
                newHoldingsRow["MarketDelta"] = OrderData.Exchange;
                newHoldingsRow["DateTimeBUY"] = OrderData.Closed;
                newHoldingsRow["Qty"] = OrderData.TotalQuantity;
                newHoldingsRow["BoughtRate"] = OrderData.PricePerUnit;
                newHoldingsRow["DateTimeSELL"] = "OWNED";
                newHoldingsRow["SoldRate"] = "OWNED";
                newHoldingsRow["StopLossRate"] = stoplossRate;
                newHoldingsRow["SL_Executed"] = 0;
                Holdings.Tables[OrderData.CandlePeriod].Rows.Add(newHoldingsRow);

                //Create + Enqueue SaveDataUpdate + OrderUpdate
                var update = new SaveDataUpdate(OrderData.CandlePeriod, OrderData.Exchange, "BUY", (DateTime)OrderData.Closed, OrderData.TotalQuantity, OrderData.PricePerUnit, stoplossRate);
                SQLDataUpdateWrites.Enqueue(update);
                SQLOrderUpdateWrites.Enqueue(OrderData);

                //OUTPUT BUY
                Trace.WriteLine(string.Format("{0}{1} Bought {2} at {3:0.00000000}, SL_Rate: {4:0.00000000}",
                    OPTIONS.VITRUAL_MODE ? "[VIRTUAL|" + OrderData.Closed + "] ::: " : "[" + OrderData.Closed + "] ::: ",
                    OrderData.CandlePeriod.Remove(0, 6),
                    OrderData.Exchange.Split('-')[1],
                    OrderData.PricePerUnit,
                    stoplossRate));
                
            }
            else if (OrderData.Type == "LIMIT_SELL")
            {
                StopLossController.CancelStoploss(string.Format("{0}_{1}", OrderData.CandlePeriod, OrderData.Exchange));
                //Find row in Holdings:
                var holdingRows = Holdings.Tables[OrderData.CandlePeriod].Select(string.Format("MarketDelta = '{0}'", OrderData.Exchange));                
                
                //Calc profit with BoughtRate and include fees:
                var profit = ((OrderData.PricePerUnit / Convert.ToDecimal(holdingRows[0]["BoughtRate"])) - 1M);
                //Calc compound multiple
                var compoundMultiple = ((Convert.ToDecimal(holdingRows[0]["BoughtRate"]) * Convert.ToDecimal(holdingRows[0]["Qty"])) / OPTIONS.BTCwagerAmt);
                //Calc TradingTotal and NetWorth
                TradingTotal += (profit * compoundMultiple);
                var netWorth = GetNetPercentage();

                var timeHeld = OrderData.Closed - Convert.ToDateTime(holdingRows[0]["DateTimeBUY"]);

                //Remove from Holdings:
                foreach (var row in holdingRows)
                    Holdings.Tables[OrderData.CandlePeriod].Rows.Remove(row);

                //Create and add the SQL SaveDataUpdate + OrderUpdate
                var update = new SaveDataUpdate(OrderData.CandlePeriod, OrderData.Exchange, "SELL", (DateTime)OrderData.Closed, OrderData.TotalQuantity, OrderData.PricePerUnit, null, false, TradingTotal);
                SQLDataUpdateWrites.Enqueue(update);
                SQLOrderUpdateWrites.Enqueue(OrderData);
                
                
                //OUTPUT SELL-ON-SIGNAL
                Trace.Write(string.Format("{0}{1} Sold {2} at {3}\r\n    =TradeProfit: ",
                    OPTIONS.VITRUAL_MODE ? "[VIRTUAL|" + OrderData.Closed + "] ::: " : "[" + OrderData.Closed + "] ::: ",
                    OrderData.CandlePeriod.Remove(0, 6),
                    OrderData.Exchange.Split('-')[1],
                    OrderData.PricePerUnit));
                //OUTPUT PROFIT ON TRADE:
                if (profit < 0)
                    Console.ForegroundColor = ConsoleColor.Red;
                else if (profit > 0)
                    Console.ForegroundColor = ConsoleColor.Green;
                Trace.Write(string.Format("{0:+0.###%;-0.###%;0}", profit));
                //OUTPUT TIME HELD
                Console.ForegroundColor = ConsoleColor.DarkCyan;
                Trace.Write(string.Format(".....=Time-Held: {0:hh\\:mm\\:ss}.....", timeHeld));
                //OUTPUT GROSS TOTAL PROFIT PERCENTAGE:
                if (TradingTotal < 0)
                    Console.ForegroundColor = ConsoleColor.Red;
                else if (TradingTotal > 0)
                    Console.ForegroundColor = ConsoleColor.Green;
                else
                    Console.ForegroundColor = ConsoleColor.DarkCyan;
                Trace.Write(string.Format("=GrossProfit: {0:+0.###%;-0.###%;0}", TradingTotal / OPTIONS.MAXTOTALENTRANCES));

                Console.ForegroundColor = ConsoleColor.DarkCyan;
                Trace.Write(".....");
                //OUTPUT CURRENT NET WORTH PERCENTAGE INCLUDING HOLDINGS:
                if (netWorth > 0)
                    Console.ForegroundColor = ConsoleColor.DarkGreen;
                else if (netWorth < 0)
                    Console.ForegroundColor = ConsoleColor.DarkRed;
                else
                    Console.ForegroundColor = ConsoleColor.DarkCyan;
                Trace.WriteLine(string.Format("=CurrentNetWorth: {0:+0.###%;-0.###%;0}", netWorth));
                Console.ForegroundColor = ConsoleColor.DarkCyan;

            }

        }


        //CALLBACK FUNCTIONS FOR STOPLOSS EXE AND CALC-MOVE:
        public void StopLossExecutedCallback(GetOrderResult OrderResponse, string period)
        {
            var TimeExecuted = OrderResponse.Closed;
            
            //FIND + REMOVE FROM HOLDINGS:
            var holdingRows = Holdings.Tables[period].Select(string.Format("MarketDelta = '{0}'", OrderResponse.Exchange));            
            
            //CALC PROFIT WITH BOUGHT RATE AND FEES INCLUDED, OUTPUT:
            var profit = ((OrderResponse.PricePerUnit / Convert.ToDecimal(holdingRows[0]["BoughtRate"])) - 1M);
            var compoundMultiple = (Convert.ToDecimal(holdingRows[0]["BoughtRate"]) * Convert.ToDecimal(holdingRows[0]["Qty"])) / OPTIONS.BTCwagerAmt;

            var timeHeld = TimeExecuted - Convert.ToDateTime(holdingRows[0]["DateTimeBUY"]);

            TradingTotal += (profit * compoundMultiple);
            var netWorth = GetNetPercentage();

            //REMOVE
            foreach (var row in holdingRows)
                Holdings.Tables[period].Rows.Remove(row);
                        

            //OUTPUT STOPLOSS EXECUTED:
            Trace.Write(string.Format("{0}{1} STOPLOSS-Sold {2} at {3:0.00000000}\r\n    =TradeProfit: ",
                    OPTIONS.VITRUAL_MODE ? "[VIRTUAL|" + TimeExecuted + "] ::: " : "[" + TimeExecuted + "] ::: ",
                    period.Remove(0, 6),
                    OrderResponse.Exchange.Split('-')[1],
                    OrderResponse.PricePerUnit));            
            //OUTPUT PROFIT ON TRADE:
            if (profit < 0)
                Console.ForegroundColor = ConsoleColor.Red;
            else if (profit > 0)
                Console.ForegroundColor = ConsoleColor.Green;
            else
                Console.ForegroundColor = ConsoleColor.DarkCyan;
            Trace.Write(string.Format("{0:+0.###%;-0.###%;0}", profit));
            //OUTPUT TIME HELD
            Console.ForegroundColor = ConsoleColor.DarkCyan;
            Trace.Write(string.Format(".....=Time-Held: {0:hh\\:mm\\:ss}.....", timeHeld));
            //OUTPUT GROSS TOTAL PROFIT PERCENTAGE:
            if (TradingTotal < 0)
                Console.ForegroundColor = ConsoleColor.Red;
            else if (TradingTotal > 0)
                Console.ForegroundColor = ConsoleColor.Green;
            else
                Console.ForegroundColor = ConsoleColor.DarkCyan;
            Trace.Write(string.Format("=GrossProfit: {0:+0.###%;-0.###%;0}", TradingTotal / OPTIONS.MAXTOTALENTRANCES));

            Console.ForegroundColor = ConsoleColor.DarkCyan;
            Trace.Write(".....");
            //OUTPUT CURRENT NET WORTH PERCENTAGE INCLUDING HOLDINGS:
            if (netWorth > 0)
                Console.ForegroundColor = ConsoleColor.DarkGreen;
            else if (netWorth < 0)
                Console.ForegroundColor = ConsoleColor.DarkRed;
            else
                Console.ForegroundColor = ConsoleColor.DarkCyan;
            Trace.WriteLine(string.Format("=CurrentNetWorth: {0:+0.###%;-0.###%;0}", netWorth));            
            Console.ForegroundColor = ConsoleColor.DarkCyan;
            
            //CREATE & ENQUEUE SQLDatawrite obj:
            var update = new SaveDataUpdate(period, OrderResponse.Exchange, "SELL", (DateTime)TimeExecuted, OrderResponse.Quantity, OrderResponse.PricePerUnit, null, true, TradingTotal);
            SQLDataUpdateWrites.Enqueue(update);
        }

        public void ReCalcStoploss(string market, decimal oldRate, string period)
        {
            //RECALC NEW STOPLOSSRATE, THEN RAISE REGISTERED RATE IF HIGHER NOW:
            decimal boughtRate = 0;
            decimal ATR = 0;
            try
            {
                ATR = CalcStoplossMargin(market, period);
                boughtRate = Convert.ToDecimal(Holdings.Tables[period].Select(string.Format("MarketDelta = '{0}'", market))[0]["BoughtRate"]);
            }
            catch (Exception e)
            {
                Trace.WriteLine("    ****ERR RECALC-STOPLOSS>>> " + market + " | " + period + "... BOUGHT: " + boughtRate + " ... RATE: " + ATR);
                return;
            }

            //TIERED TRAILING STOPLOSS:
            //Teir 2 (calculate):
            var stoplossRate = BtrexData.Markets[market].TradeHistory.RecentFills.Last().Rate - (ATR * OPTIONS.ATRmultipleT2);
            var tier = "**";
            
            //Use Teir 1, if T2 is below profit line:
            if (stoplossRate < boughtRate * 1.0025M)
            {
                stoplossRate = BtrexData.Markets[market].TradeHistory.RecentFills.Last().Rate - (ATR * OPTIONS.ATRmultipleT1);
                if (OPTIONS.SAFE_MODE)
                    stoplossRate = 0.0M;

                tier = "*";
            }

            //Use Teir 3, if current rate is above 8% profit:
            if (BtrexData.Markets[market].TradeHistory.RecentFills.Last().Rate > boughtRate * 1.1M)
            {
                stoplossRate = BtrexData.Markets[market].TradeHistory.RecentFills.Last().Rate - (ATR * OPTIONS.ATRmultipleT3);
                tier = "***";
            }


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
                SQLDataUpdateWrites.Enqueue(update);

                //OUTPUT STOPLOSS MOVED:
                if (OPTIONS.LogStoplossRaised)
                {
                    Trace.WriteLine(string.Format("{0}{1}_{2} STOPLOSS-RAISED from {3:0.00000000} to {4:0.00000000}{5}",
                    "[" + SLmovedTime + "] ::: ",
                    period.Remove(0, 6),
                    market,
                    oldRate,
                    stoplossRate,
                    tier
                    ));
                }
                

            }

        }

        //LOGIC FOR CALCLULATING STOPLOSS MARGIN
        private decimal CalcStoplossMargin(string delta, string cPeriod)
        {
            int ATRparameter = 6;
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
            
            return ATR;
        }


        private void SaveSQLOrderData(SQLiteCommand cmd)
        {
            if (!SQLOrderUpdateWrites.IsEmpty)
            {
                using (var tx = conn.BeginTransaction())
                {
                    while (!SQLOrderUpdateWrites.IsEmpty)
                    {
                        OpenOrder update;
                        bool dqed;
                        do
                        {
                            dqed = SQLOrderUpdateWrites.TryDequeue(out update);
                        } while (!dqed);

                        var uniqueID = string.Format("{0}_{1}", update.CandlePeriod, update.Exchange);

                        if (update.IsOpen)
                        {
                            cmd.CommandText = string.Format("REPLACE INTO OpenOrders (UniqueID, OrderUuid, Exchange, Type, TotalQuantity, TotalReserved, Quantity, QuantityRemaining, Limit, Reserved, CommissionReserved, CommissionReserveRemaining, CommissionPaid, Price, PricePerUnit, Opened, CandlePeriod) VALUES ('{0}', '{1}', '{2}', '{3}', '{4}', '{5}', '{6}', '{7}', '{8}', '{9}', '{10}', '{11}', '{12}', '{13}', '{14}', '{15}', '{16}')", 
                                uniqueID, update.OrderUuid, update.Exchange, update.Type, update.TotalQuantity, update.TotalReserved, update.Quantity, update.QuantityRemaining, update.Limit, update.Reserved, update.CommissionReserved, update.CommissionReserveRemaining, update.CommissionPaid, update.Price, update.PricePerUnit, update.Opened, update.CandlePeriod);
                            cmd.ExecuteNonQuery();                            
                        }
                        else if (!update.IsOpen)
                        {
                            cmd.CommandText = string.Format("DELETE FROM OpenOrders WHERE UniqueID = '{0}'", uniqueID);
                            cmd.ExecuteNonQuery();
                        }                    

                    }

                    tx.Commit();
                }
            }


        }


        private void SaveSQLData(SQLiteCommand cmd)
        {
            if (!SQLDataUpdateWrites.IsEmpty)
            {
                using (var tx = conn.BeginTransaction())
                {
                    //SAVE DATA EVENT CHANGES
                    while (!SQLDataUpdateWrites.IsEmpty)
                    {
                        SaveDataUpdate update;
                        bool dqed;
                        do
                        {
                            dqed = SQLDataUpdateWrites.TryDequeue(out update);
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
                            cmd.CommandText = string.Format("UPDATE totals SET PercentageTotal = {0}", update.Percentage);
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
        
        
        private bool OpenSQLiteConn()
        {
            if (!File.Exists(TradesDataFile))
            {
                Trace.WriteLine(string.Format("CREATING NEW '{0}' FILE...", TradesDataFile));
                SQLiteConnection.CreateFile(TradesDataFile);
                conn = new SQLiteConnection("Data Source=" + TradesDataFile + ";Version=3;");
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
                        cmd.CommandText = "CREATE TABLE IF NOT EXISTS totals (TimeCreatedUTC TEXT, PercentageTotal TEXT)";
                        cmd.ExecuteNonQuery();
                        cmd.CommandText = string.Format("INSERT INTO totals(TimeCreatedUTC, PercentageTotal) VALUES ('{0}', '{1}')", DateTime.UtcNow, "0.0");
                        cmd.ExecuteNonQuery();
                        cmd.CommandText = @"CREATE TABLE IF NOT EXISTS OpenOrders (UniqueID TEXT, OrderUuid TEXT, Exchange TEXT, Type TEXT, TotalQuantity TEXT, TotalReserved TEXT, Quantity TEXT, QuantityRemaining TEXT, ""Limit"" TEXT, Reserved TEXT, CommissionReserved TEXT, CommissionReserveRemaining TEXT, CommissionPaid TEXT, Price TEXT, PricePerUnit TEXT, Opened TEXT, CandlePeriod TEXT)";
                        cmd.ExecuteNonQuery();
                        cmd.CommandText = "CREATE UNIQUE INDEX idx_OpenOrders_UniqueID ON OpenOrders (UniqueID)";
                        cmd.ExecuteNonQuery();

                        tx.Commit();
                    }
                }

                return true;
            }
            else
            {
                conn = new SQLiteConnection("Data Source=" + TradesDataFile + ";Version=3;");
                conn.Open();

                return false;
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
        public decimal? Percentage { get; set; }

        public SaveDataUpdate(string period, string market, string buyORsellORsl_move, DateTime time, decimal qty, decimal price, decimal? SL_rate = null, bool stoplossExe = false, decimal? perc = null)
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
            if (perc != null)
                Percentage = perc;
        }
    }


   

}
