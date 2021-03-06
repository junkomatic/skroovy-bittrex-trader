﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Diagnostics;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Data.SQLite;
using Trady.Core;
using BtrexTrader.Interface;

namespace BtrexTrader.Data.Market
{
    public class TradeHistory : ICloneable
    {
        public string MarketDelta { get; private set; }
        public List<mdFill> RecentFills { get; private set; }
        public List<HistDataLine> Candles5m { get; private set; }
        public DateTime LastStoredCandle { get; private set; }
        private bool CandlesResolved = false;

        public TradeHistory(MarketQueryResponse snap)
        {
            //Pull candles from SQLite data and rectify with snap data,
            //if cant rectify imediately, enter rectefication process/state
            MarketDelta = snap.MarketName;
            RecentFills = new List<mdFill>();
            Candles5m = new List<HistDataLine>();
            
            if (snap.Fills.Count() > 0)
            {
                snap.Fills.Reverse();
                foreach (Fill fill in snap.Fills)
                    RecentFills.Add(new mdFill(Convert.ToDateTime(fill.TimeStamp), fill.Price, fill.Quantity, fill.OrderType));
            }
            Trace.Write(string.Format("\rResolving Candle Data: [{0}]         ", MarketDelta));
            //Compare last-time from .data, and first-time from snap:
            using (SQLiteConnection conn = new SQLiteConnection("Data Source=" + HistoricalData.dbName + ";Version=3;"))
            {
                conn.Open();
                using (var cmd = new SQLiteCommand(conn))
                {
                    cmd.CommandText = string.Format("SELECT * FROM {0} ORDER BY datetime(DateTime) DESC Limit 1", MarketDelta.Replace('-', '_'));
                    LastStoredCandle = Convert.ToDateTime(cmd.ExecuteScalar());
                }
                conn.Close();
            }
            
            //Candle Time is the START time of the 5m period. This means it is current to +5min from that time.
            DateTime NextCandleTime = LastStoredCandle.AddMinutes(5);
            
            if (NextCandleTime > RecentFills.Last().TimeStamp)
            {
                //NO TRADES MADE IN THIS CANDLE PERIOD.
                CandlesResolved = true;
                return;
            }
            else if (NextCandleTime >= RecentFills.First().TimeStamp)
            {
                BuildCandleFromRecentFills(NextCandleTime);

                //Trace.WriteLine("@@@@@ NEW CANDLE = T:{0} O:{1} H:{2} L:{3} C:{4} V:{5}",
                    //nextCandle.DateTime, nextCandle.Open, nextCandle.High, nextCandle.Low, nextCandle.Close, nextCandle.Volume);
                
                CandlesResolved = true;
            }
            
        }


        public async Task<bool> Resolve5mCandles(bool retryOnFail = true)
        {
            DateTime NextCandleTime = LastStoredCandle.AddMinutes(5);
            DateTime NextCandleCurrTime = LastStoredCandle.AddMinutes(10);
            if (CandlesResolved)
            {
                Trace.WriteLine(string.Format("\r[{1}] CANDLES RESOLVED - LastCandleStart: {0}", LastStoredCandle, MarketDelta));
                return true;
            }
            
            while (!CandlesResolved)
            {
                HistDataResponse response = await BtrexREST.GetMarketHistoryV2(MarketDelta, "oneMin");
                if (!response.success)
                {
                    Trace.WriteLine("    !!!!ERR GET-1m-CANDLES: [{0}]", MarketDelta);
                    return false;
                }

                DateTime last1mCandleCurrTime = response.result.Last().T.AddMinutes(1);
                DateTime firstFillTime = RecentFills.First().TimeStamp;

                if (last1mCandleCurrTime >= firstFillTime)
                {
                    //Build latest 5m candle with 1m data and RecentFills:
                    List<HistDataLine> Candles1m = new List<HistDataLine>();
                    foreach (HistDataLine line in response.result)
                        if (line.T >= LastStoredCandle.AddMinutes(5))
                            Candles1m.Add(line);

                    //Grab O:H:L:V (noC) from 1mCandles
                    //simulate 2 mdFills (H&L:V) and add to beginning of RecentFills
                    Decimal O = Candles1m.First().O,
                            H = Candles1m.Max(x => x.H),
                            L = Candles1m.Min(x => x.L),
                            V = Candles1m.Sum(x => x.V),
                            C = Candles1m.Last().C;

                    List<mdFill> RevisedFills = new List<mdFill>();
                    RevisedFills.Add(new mdFill(LastStoredCandle.AddMinutes(5), O, (V / 4M), "BUY"));
                    RevisedFills.Add(new mdFill(LastStoredCandle.AddSeconds(300.5), H, (V / 4M), "SELL"));
                    RevisedFills.Add(new mdFill(LastStoredCandle.AddSeconds(300.5), L, (V / 4M), "BUY"));
                    RevisedFills.Add(new mdFill(LastStoredCandle.AddSeconds(301), C, (V / 4M), "SELL"));
                    
                    if (last1mCandleCurrTime >= NextCandleCurrTime)
                        RecentFills = new List<mdFill>(RevisedFills);
                    else
                    {
                        foreach (mdFill fill in RecentFills)
                            if (fill.TimeStamp >= last1mCandleCurrTime && last1mCandleCurrTime < NextCandleCurrTime)
                                RevisedFills.Add(fill);

                        RecentFills = new List<mdFill>(RevisedFills);
                    }

                    

                    BuildCandleFromRecentFills(NextCandleTime);
                    
                    CandlesResolved = true;
                    Trace.WriteLine(string.Format("\r[{1}] CANDLES RESOLVED - LastCandleStart: {0}", LastStoredCandle, MarketDelta));
                }
                else
                {
                    //Trace.WriteLine("    !!!!ERR RESOLVE_CANDLES>>Current: {0} < LastFill: {1} :: [{2}]", last1mCandleCurrTime, firstFillTime, MarketDelta);
                    if (!retryOnFail)
                        return false;

                    for (int s = 15; s > 0; s--)
                    {
                        Trace.Write(string.Format("\r    Resolving [{0}] TradeHist->Candles time gap. Retry in {1} seconds...", MarketDelta, s));
                        Thread.Sleep(1000);
                    }
                    Trace.Write("\r                                                                                  \r");
                }
            }

            return true;
        }


        public void BuildCandleFromRecentFills(DateTime NextCandleTime)
        {
            //check current, if not, rectify
            //If candle is current, Candles are Resolved
            if (NextCandleTime.AddMinutes(5) > DateTime.UtcNow)            
                return;            

            Decimal BV = 0;
            List<mdFill> candleFills = new List<mdFill>();
            foreach (mdFill fill in RecentFills)
            {
                if (fill.TimeStamp < NextCandleTime)
                    continue;
                else if (fill.TimeStamp >= NextCandleTime.AddMinutes(5))
                    break;
                else
                {
                    BV += (fill.Quantity * fill.Rate);
                    candleFills.Add(fill);
                }
                    
            }

            if (candleFills.Count == 0)
                return; 

            Decimal O = candleFills.First().Rate,
                    H = candleFills.Max(x => x.Rate),
                    L = candleFills.Min(x => x.Rate),
                    C = candleFills.Last().Rate,
                    V = candleFills.Sum(x => x.Quantity);
            
            HistDataLine candle = new HistDataLine(NextCandleTime, O, H, L, C, V, BV);
            Candles5m.Add(candle);
            LastStoredCandle = LastStoredCandle.AddMinutes(5);
        }

        public void SavePurgeCandlesSQLite(SQLiteCommand cmd)
        {
            cmd.CommandText = string.Format("SELECT * FROM {0} ORDER BY datetime(DateTime) DESC Limit 1", MarketDelta);
            DateTime dateTime = Convert.ToDateTime(cmd.ExecuteScalar());

            List<HistDataLine> removeList = new List<HistDataLine>();

            foreach (HistDataLine line in Candles5m)
            {

                if (line.T >= LastStoredCandle.Subtract(TimeSpan.FromHours(3)))
                    break;
                if (line.T <= dateTime)
                    continue;
                else
                {
                    cmd.CommandText = string.Format(
                        "INSERT INTO {0} (DateTime, Open, High, Low, Close, Volume, BaseVolume) "
                        + "VALUES ('{1}', '{2}', '{3}', '{4}', '{5}', '{6}', '{7}')",
                        MarketDelta,
                        line.T.ToString("yyyy-MM-dd HH:mm:ss"), line.O, line.H, line.L, line.C, line.V, line.BV);

                    cmd.ExecuteNonQuery();
                    removeList.Add(line);
                }
            }

            Candles5m = (List<HistDataLine>)Candles5m.Except(removeList);
        }

        public void CullRecentFills()
        {
            //CULL RecentFills to 20 minutes before LastCandleTime
            DateTime cullTime = LastStoredCandle.Subtract(TimeSpan.FromMinutes(20));
            List<mdFill> culledFills = new List<mdFill>();
            foreach (mdFill fill in RecentFills)
                if (fill.TimeStamp < cullTime)
                    culledFills.Add(fill);

            RecentFills = (List<mdFill>)RecentFills.Except(culledFills);
        }


        public object Clone()
        {
            return this.MemberwiseClone();
        }
    }
}
