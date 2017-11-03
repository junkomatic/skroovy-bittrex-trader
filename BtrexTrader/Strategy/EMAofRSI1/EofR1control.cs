using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.IO;
using System.Data.SQLite;
using Trady.Core;
using Trady.Analysis;
using BtrexTrader.Interface;
using BtrexTrader.Data;
using BtrexTrader.Data.Market;

namespace BtrexTrader.Strategy.EMAofRSI1
{
    class EofR1control
    {
        private const string dataFile = "EMAofRSI1trades.data";
        private SQLiteConnection conn;

        private StratData_MultiPeriods StratData = new StratData_MultiPeriods();
        
        private IReadOnlyList<string> SpecificDeltas = new List<string>()
        {
            "BTC-XLM"//, "BTC-ADA", "BTC-ETH", "BTC-QTUM", "BTC-OMG"
        };
        


        public async Task Initialize()
        {
            //await SubTopMarketsByVol(50);
            await SubSpecificMarkets();

            checkNewTradesHistData();
            CheckHoldings();

            await StratData.PreloadCandleDicts(26);            
        }

        public async Task Start()
        {          
            using (var cmd = new SQLiteCommand(conn))
            {
                while (true)
                {
                    //TODO: CHECK CURRENTLY OWNED ASSETS and STOP LOSSES:









                    foreach (Market m in BtrexData.Markets.Values)
                    {
                        bool candles5mChanged = false,
                                candles20mChanged = false,
                                candles1hChanged = false,
                                candles4hChanged = false,
                                candles12hChanged = false;

                        //CHECK FOR NEW CANDLES:
                        if (m.TradeHistory.LastStoredCandle > StratData.Candles5m[m.MarketDelta].Last().DateTime)
                        {
                            //Get new 5m candles:
                            var Importer = new TradyCandleImporter();
                            var newCandles = await Importer.ImportAsync(m.MarketDelta, StratData.Candles5m[m.MarketDelta].Last().DateTime.AddMinutes(5));
                            StratData.Candles5m[m.MarketDelta].AddRange(newCandles);
                            candles5mChanged = true;

                            if (m.TradeHistory.LastStoredCandle > StratData.Candles20m[m.MarketDelta].Last().DateTime.AddMinutes(35))
                            {
                                //Build new 20m candles:
                                candles20mChanged = StratData.BuildNew20mCndls(m.MarketDelta);

                                if (m.TradeHistory.LastStoredCandle > StratData.Candles1h[m.MarketDelta].Last().DateTime.AddMinutes(115))
                                {
                                    //Build new 1h candles:
                                    candles1hChanged = StratData.BuildNew1hCndls(m.MarketDelta);

                                    if (m.TradeHistory.LastStoredCandle > StratData.Candles4h[m.MarketDelta].Last().DateTime.AddMinutes(475))
                                    {
                                        //Build new 4h candles
                                        candles4hChanged = StratData.BuildNew4hCndls(m.MarketDelta);
                                    }

                                    if (m.TradeHistory.LastStoredCandle.AddMinutes(5) > StratData.Candles12h[m.MarketDelta].Last().DateTime.AddHours(12))
                                    {
                                        //Build new 12h candles
                                        candles12hChanged = StratData.BuildNew12hCndls(m.MarketDelta);
                                    }
                                }
                            }
                        }


                        //TODO: CALC RSIs/Indicators for candlesChanged
                        if (candles5mChanged)
                        {

                        }

                        if (candles20mChanged)
                        {

                        }

                        if (candles1hChanged)
                        {

                        }

                        if (candles4hChanged)
                        {

                        }

                        if (candles12hChanged)
                        {

                        }

                    }

                    Thread.Sleep(1000);

                }
            }
            conn.Close();
            
        }
        


        private async Task SubTopMarketsByVol(int n)
        {
            List<string> topMarkets = await BtrexREST.TradeMethods.GetTopMarketsByBVbtcOnly(n);
            foreach (string mk in topMarkets)
                await BtrexWS.subscribeMarket("BTC-" + mk);
        }


        private async Task SubSpecificMarkets()
        {
            foreach (string mk in SpecificDeltas)
                await BtrexWS.subscribeMarket(mk);
        }


        private bool checkNewTradesHistData()
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
                        cmd.CommandText = "CREATE TABLE IF NOT EXISTS 5m (DateTimeBUY TEXT, Qty TEXT, BoughtRate TEXT, DateTimeSELL, SoldRate TEXT, StopLossRate TEXT, SL_Executed INTEGER)";
                        cmd.ExecuteNonQuery();

                        cmd.CommandText = "CREATE TABLE IF NOT EXISTS 20m (DateTimeBUY TEXT, Qty TEXT, BoughtRate TEXT, DateTimeSELL, SoldRate TEXT, StopLossRate TEXT, SL_Executed INTEGER)";
                        cmd.ExecuteNonQuery();

                        cmd.CommandText = "CREATE TABLE IF NOT EXISTS 1h (DateTimeBUY TEXT, Qty TEXT, BoughtRate TEXT, DateTimeSELL, SoldRate TEXT, StopLossRate TEXT, SL_Executed INTEGER)";
                        cmd.ExecuteNonQuery();

                        cmd.CommandText = "CREATE TABLE IF NOT EXISTS 4h (DateTimeBUY TEXT, Qty TEXT, BoughtRate TEXT, DateTimeSELL, SoldRate TEXT, StopLossRate TEXT, SL_Executed INTEGER)";
                        cmd.ExecuteNonQuery();

                        cmd.CommandText = "CREATE TABLE IF NOT EXISTS 12h (DateTimeBUY TEXT, Qty TEXT, BoughtRate TEXT, DateTimeSELL, SoldRate TEXT, StopLossRate TEXT, SL_Executed INTEGER)";
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


        private void CheckHoldings()
        {
            //TODO: Load held assets, stoploss amts, period, from SQLite




        }

    }


}
