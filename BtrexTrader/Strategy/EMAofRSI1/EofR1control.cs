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

        private IReadOnlyList<string> SubSpecificDeltas = new List<string>()
        {
            "BTC-OMG", "BTC-XLM", "BTC-NEO", "BTC-BCC", "BTC-ADA"
        };

        private IReadOnlyList<string> ExcludeTheseDeltas = new List<string>()
        {
            //EXLUDE MARKETS THAT WERE RELEASED LESS THAN A MONTH AGO,
            //(CANT PRELOAD ADEQUATE CANDLE DATA FOR CALCS -> SequenceContainsNoElements err)
            "1ST", "NAV", "XVG", "VIB"
        };

        private decimal WagerAmt = 0.001M;
        private decimal TradingTotal = 0.0M;
        private const int MaxMarketsEnteredPerPeriod = 8;
        private const int MAXTOTALENTRANCES = 40;
        private const decimal ATRmultipleT1 = 2.5M;
        private const decimal ATRmultipleT2 = 2M;
        private const decimal ATRmultipleT3 = 1.5M;
        private const bool SAFEMODE = true;

        private const bool LogStoplossRaised = true;
        private const bool LogSignals = true;

        private bool VirtualOnOff = true;
        

        public async Task Initialize()
        {
            OpenSQLiteConn();
            LoadHoldings();

            await SubTopMarketsByVol(60);
            //await SubSpecificMarkets();
                       

            await StratData.PreloadCandleDicts(39);                       
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
                        PendingOrders.AddRange(NewOrders);
                        var ords = new List<NewOrder>(NewOrders);
                        NewOrders = new List<NewOrder>();
                        
                        //This is not awaited because NewOrder objects reference their own callback
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

                //DEBUG OUTPUT LAST CANDLE:
                //var cndl = candles.Last();
                //Console.WriteLine("[NEW CANDLE|{0}|{1}] ::: T:{2} ... O:{3} ... H:{4} ... L:{5} ... C:{6} ... V:{7}", 
                    //delta, periodName, cndl.DateTime, cndl.Open, cndl.High, cndl.Low, cndl.Close, cndl.Volume);
                
                if (call != null)
                {
                    //OUTPUT SIGNAL:
                    if (LogSignals)
                    {
                        if (call == true)
                            Console.WriteLine("*BUY SIGNAL: {0} on {1} candles*", delta, periodName.Remove(0, 6));
                        else
                            Console.WriteLine("*SELL SIGNAL: {0} on {1} candles*", delta, periodName.Remove(0, 6));
                    }

                    var held = Holdings.Tables[periodName].Select("MarketDelta = '"+ delta +"'");
                    bool owned = false;
                    if (held.Length > 0)
                        owned = true;


                    if (call == true && !owned && (Holdings.Tables[periodName].Rows.Count + NewOrders.Where(o => o.CandlePeriod == periodName).Count() < MaxMarketsEnteredPerPeriod))
                    {
                        //Add BUY order on period
                        var rate = BtrexData.Markets[delta].TradeHistory.RecentFills.Last().Rate;
                        if (rate >= 0.000001M)
                        {
                            var wagerMultiple = (TradingTotal / MAXTOTALENTRANCES) + 1;
                            var amt = (WagerAmt * wagerMultiple) / (rate * 1.0025M);

                            NewOrders.Add(new NewOrder(delta, "BUY", amt, rate, (a) => OrderExecutedCallback(a), periodName));
                        }
                        
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

            //TODO: CHECK SQLITE FOR ADEQUATE HISTORY-TIME FOR CANDLE PRELOAD


            //SUBSCRIBE ALL MARKETS, RETRY FAILED AT END:
            List<MarketQueryResponse> timeGapMarkets = await BtrexWS.SubscribeMarketsList(topMarkets.Except(ExcludeTheseDeltas).ToList());

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
                    Console.Write("\r    ({0}) TimeGapped Markets (TradeFills->Candles), Retry in {1} seconds...\r", markets.Count, i);
                    Thread.Sleep(1000);

                }
                Console.Write("\r                                                                                          \r");

            } while (markets.Count > 0);
            
        }


        private async Task SubSpecificMarkets()
        {
            foreach (string mk in SubSpecificDeltas)
                await BtrexWS.SubscribeMarket(mk);
        }        
        
        
        private void AddHoldingsTable(string periodName)
        {
            var dt = new DataTable();
            using (var sqlAdapter = new SQLiteDataAdapter("SELECT * from " + periodName + " WHERE DateTimeSELL = 'OWNED'", conn))
                sqlAdapter.Fill(dt);

            dt.TableName = periodName;
            Holdings.Tables.Add(dt);
        }

        private void LoadTradingTotal()
        {
            DateTime TimeCreated = new DateTime();
            using (var cmd = new SQLiteCommand(conn))
            {
                cmd.CommandText = "SELECT PercentageTotal FROM totals LIMIT 1";
                TradingTotal = Convert.ToDecimal(cmd.ExecuteScalar());

                cmd.CommandText = "SELECT TimeCreatedUTC FROM totals LIMIT 1";
                TimeCreated = Convert.ToDateTime(cmd.ExecuteScalar());
            }

     
            if (TradingTotal > 0)
            {
                Console.ForegroundColor = ConsoleColor.Green;
                Console.Write("=TradingTotal: {0:+0.###%}", TradingTotal);
                Console.ForegroundColor = ConsoleColor.DarkGreen;
                Console.Write("({0:+0.###%} net)", (TradingTotal / MAXTOTALENTRANCES));
            }
            else if (TradingTotal < 0)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.Write("=TradingTotal: {0:0.###%}", TradingTotal);
                Console.ForegroundColor = ConsoleColor.DarkRed;
                Console.Write("({0:0.###%} net)", (TradingTotal / MAXTOTALENTRANCES));
            }
            else
            {
                Console.Write("TradingTotal = {0}%", TradingTotal);
            }
            Console.ForegroundColor = ConsoleColor.DarkCyan;
            Console.WriteLine(" ... StartTimeUTC: {0}\r\n", TimeCreated);
        }

        private void LoadHoldings()
        {
            //Load held assets, stoploss amts, from SQLite for each period:
            AddHoldingsTable("period5m");
            AddHoldingsTable("period20m");
            AddHoldingsTable("period1h");
            AddHoldingsTable("period4h");
            AddHoldingsTable("period12h");

            Console.WriteLine("\r\n>>>ASSETS HELD:");
;
            //REGISTER EXISTING STOPLOSS RATES FOR EACH HOLDING
            foreach (DataTable dt in Holdings.Tables)
            {
                foreach (DataRow row in dt.Rows)
                {
                    var stopLoss = new StopLoss((string)row["MarketDelta"], Convert.ToDecimal(row["StopLossRate"]), Convert.ToDecimal(row["Qty"]), (a, b, c) => ReCalcStoploss(a, b, c), (a, b) => StopLossExecutedCallback(a, b), dt.TableName, VirtualOnOff);
                    StopLossController.RegisterStoploss(stopLoss, string.Format("{0}_{1}", stopLoss.CandlePeriod, stopLoss.MarketDelta));
                    Console.WriteLine("    {0}_{1}, SL_RATE: {2:0.00000000}", dt.TableName.Remove(0, 6), row["MarketDelta"], Convert.ToDecimal(row["StopLossRate"]));
                }
            }

            //Load Total From SQLite data:
            LoadTradingTotal();

        }


        public void OrderExecutedCallback(NewOrder OrderData)
        { 
            var TimeCompleted = DateTime.UtcNow;

            //Pull from pending orders
            PendingOrders.RemoveAll(o => o.MarketDelta == OrderData.MarketDelta && o.CandlePeriod == OrderData.CandlePeriod);

            if (OrderData.BUYorSELL == "BUY")
            {
                //Create and register stoploss
                decimal stoplossRate = OrderData.Rate - (CalcStoplossMargin(OrderData.MarketDelta, OrderData.CandlePeriod) * ATRmultipleT1);

                switch (OrderData.CandlePeriod)
                {
                    case "period5m":
                        if (stoplossRate / OrderData.Rate < 0.98M)
                            stoplossRate = OrderData.Rate * 0.98M;
                        break;
                    case "period20m":
                        if (stoplossRate / OrderData.Rate < 0.95M)
                            stoplossRate = OrderData.Rate * 0.95M;
                        break;
                    case "period1h":
                        if (stoplossRate / OrderData.Rate < 0.93M)
                            stoplossRate = OrderData.Rate * 0.93M;
                        break;
                    case "period4h":
                        if (stoplossRate / OrderData.Rate < 0.90M)
                            stoplossRate = OrderData.Rate * 0.90M;
                        break;
                    case "period12h":
                        if (stoplossRate / OrderData.Rate < 0.88M)
                            stoplossRate = OrderData.Rate * 0.88M;
                        break;
                }

                if (SAFEMODE)
                    stoplossRate = 0.00056M / OrderData.Qty;
                
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
                var compoundMultiple = ((Convert.ToDecimal(holdingRows[0]["BoughtRate"]) * Convert.ToDecimal(holdingRows[0]["Qty"])) / WagerAmt);

                TradingTotal += (profit * compoundMultiple);

                if (profit < 0)
                    Console.ForegroundColor = ConsoleColor.Red;
                else if (profit > 0)
                    Console.ForegroundColor = ConsoleColor.Green;
                Console.Write("{0:+0.###%;-0.###%;0}", profit);
                Console.ForegroundColor = ConsoleColor.DarkCyan;
                Console.Write(" ..... =Time-Held: {0:hh\\:mm\\:ss} ..... ",                    
                    //CALC HELD TIME:
                    (TimeCompleted - Convert.ToDateTime(holdingRows[0]["DateTimeBUY"])));
                if (TradingTotal < 0)
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.Write("=TradingTotal: {0:+0.###%;-0.###%;0}", TradingTotal);
                    Console.ForegroundColor = ConsoleColor.DarkRed;
                    Console.WriteLine("({:+0.###%;-0.###%;0} net)", (TradingTotal / MAXTOTALENTRANCES));
                }
                else if (TradingTotal > 0)
                {
                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.Write("=TradingTotal: {0:+0.###%;-0.###%;0}", TradingTotal);
                    Console.ForegroundColor = ConsoleColor.DarkGreen;
                    Console.WriteLine("({0:+0.###%;-0.###%;0} net)", (TradingTotal / MAXTOTALENTRANCES));
                }
                    
                Console.ForegroundColor = ConsoleColor.DarkCyan;

                foreach (var row in holdingRows)
                    Holdings.Tables[OrderData.CandlePeriod].Rows.Remove(row);

                //CREATE/ADD SQL UPDATE:             
                var update = new SaveDataUpdate(OrderData.CandlePeriod, OrderData.MarketDelta, "SELL", TimeCompleted, OrderData.Qty, OrderData.Rate, null, false, TradingTotal);
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
            var compoundMultiple = (Convert.ToDecimal(holdingRows[0]["BoughtRate"]) * Convert.ToDecimal(holdingRows[0]["Qty"])) / WagerAmt;

            TradingTotal += (profit * compoundMultiple);
            if (profit < 0)
                Console.ForegroundColor = ConsoleColor.Red;
            else if (profit > 0)
                Console.ForegroundColor = ConsoleColor.Green;
            Console.Write("{0:+0.###%;-0.###%;0}", profit);
            Console.ForegroundColor = ConsoleColor.DarkCyan;
            Console.Write(" ..... =Time-Held: {0:hh\\:mm\\:ss} ..... ",
                //CALC HELD TIME, OUTPUT:
                (TimeExecuted - Convert.ToDateTime(holdingRows[0]["DateTimeBUY"])));
            if (TradingTotal < 0)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.Write("=TradingTotal: {0:+0.###%;-0.###%;0}", TradingTotal);
                Console.ForegroundColor = ConsoleColor.DarkRed;
                var net = 0 - (Math.Abs(TradingTotal) / MAXTOTALENTRANCES);
                Console.WriteLine("({:+0.###%;-0.###%;0} net)", (TradingTotal / MAXTOTALENTRANCES));
            }
            else if (TradingTotal > 0)
            {
                Console.ForegroundColor = ConsoleColor.Green;
                Console.Write("=TradingTotal: {0:+0.###%;-0.###%;0}", TradingTotal);
                Console.ForegroundColor = ConsoleColor.DarkGreen;
                Console.WriteLine("({0:+0.###%;-0.###%;0} net)", (TradingTotal / MAXTOTALENTRANCES));
            }

            
            Console.ForegroundColor = ConsoleColor.DarkCyan;

            foreach (var row in holdingRows)
                Holdings.Tables[period].Rows.Remove(row);

            //CREATE & ENQUEUE SQLDatawrite obj:
            var update = new SaveDataUpdate(period, OrderResponse.Exchange, "SELL", TimeExecuted, OrderResponse.Quantity, OrderResponse.PricePerUnit, null, true, TradingTotal);
            SQLDataWrites.Enqueue(update);
                        
        }

        public void ReCalcStoploss(string market, decimal oldRate, string period)
        {
            //RECALC NEW STOPLOSSRATE, THEN RAISE REGISTERED RATE IF HIGHER NOW:
            var ATR = CalcStoplossMargin(market, period);
            decimal boughtRate = Convert.ToDecimal(Holdings.Tables[period].Select(string.Format("MarketDelta = '{0}'", market))[0]["BoughtRate"]);

            //TIERED TRAILING STOPLOSS:
            //Teir 2 (calculate):
            var stoplossRate = BtrexData.Markets[market].TradeHistory.RecentFills.Last().Rate - (ATR * ATRmultipleT2);
            var tier = "**";
            
            //Use Teir 1, if T2 is below profit line:
            if (stoplossRate < boughtRate * 1.0025M)
            {
                stoplossRate = BtrexData.Markets[market].TradeHistory.RecentFills.Last().Rate - (ATR * ATRmultipleT1);
                if (SAFEMODE)
                    stoplossRate = 0.0M;

                tier = "*";
            }

            //Use Teir 3, if current rate is above 8% profit:
            if (BtrexData.Markets[market].TradeHistory.RecentFills.Last().Rate > boughtRate * 1.1M)
            {
                stoplossRate = BtrexData.Markets[market].TradeHistory.RecentFills.Last().Rate - (ATR * ATRmultipleT3);
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
                SQLDataWrites.Enqueue(update);

                //OUTPUT STOPLOSS MOVED:
                if (LogStoplossRaised)
                {
                    Console.WriteLine("{0}{1}_{2} STOPLOSS-RAISED from {3:0.00000000} to {4:0.00000000}{5}",
                    "[" + SLmovedTime + "] ::: ",
                    period.Remove(0, 6),
                    market,
                    oldRate,
                    stoplossRate,
                    tier
                    );
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
                        cmd.CommandText = "CREATE TABLE IF NOT EXISTS totals (TimeCreatedUTC TEXT, PercentageTotal TEXT)";
                        cmd.ExecuteNonQuery();
                        cmd.CommandText = string.Format("INSERT INTO totals(TimeCreatedUTC, PercentageTotal) VALUES ('{0}', '{1}')", DateTime.UtcNow, "0.0");
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
