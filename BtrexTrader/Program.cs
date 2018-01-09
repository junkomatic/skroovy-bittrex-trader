using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using BtrexTrader.Control;
using BtrexTrader.Interface;
using BtrexTrader.Data;
using Trady.Core;

namespace BtrexTrader
{
    class Program
    {
        private static BtrexTradeController BtrexController = new BtrexTradeController();
        
        static void Main(string[] args)
        {
            Console.BufferHeight = 9999;
            
            PrintTitle();
            Console.ForegroundColor = ConsoleColor.DarkCyan;
            Console.BackgroundColor = ConsoleColor.Black;
                        
            RunAsync().Wait(); 
                        
        }


        static async Task RunAsync()
        {
            //UPDATE LOCALLY STORED 5m CANDLES, AND .CSV RECORDS:
            await HistoricalData.UpdateHistData();
            
            //INITIALIZE DATA, THEN CONNECT WEBSOCKET
            BtrexData.NewData();
            await BtrexWS.Connect();
            
            ConfigTraceLogging();

            //SUBSCRIBE TO DESIRED MARKETS, THEN START-DATA-UPDATES:
            await BtrexController.InitializeMarkets();

            //START DATA THREAD
            await BtrexData.StartDataUpdates();
            
            //START CALCS/STRATEGY WORK:
            BtrexController.StartWork();

            //START TRADING THREAD
            BtrexREST.TradeController.StartTrading();

            Console.WriteLine("\r\n\r\n-PRESS ENTER 3 TIMES TO EXIT-\r\n\r\n");
            Console.ReadLine();
            Console.ReadLine();
            Console.ReadLine();
            Environment.Exit(0);
        }



        private static void PrintTitle()
        {
            Console.SetWindowSize(120, 40);
            Console.WriteLine("\r\n");
            Console.ForegroundColor = ConsoleColor.White;
            Console.BackgroundColor = ConsoleColor.DarkCyan;
            Console.WriteLine(
@"-----------------------------------------------------------------------------------------------------------------------
  oooooooooo.      .                                  ooooooooooooo                          .o8                       
  `888'   `Y8b   .o8                                  8'   888   `8                         ""888                       
   888     888 .o888oo oooo d8b  .ooooo.  oooo    ooo      888      oooo d8b  .oooo.    .oooo888   .ooooo.  oooo d8b   
   888oooo888'   888   `888""""8P d88' `88b  `88b..8P'       888      `888""""8P `P  )88b  d88' `888  d88' `88b `888""""8P   
   888    `88b   888    888     888ooo888    Y888'         888       888      .oP""888  888   888  888ooo888  888       
   888    .88P   888 .  888     888    .o  .o8""'88b        888       888     d8(  888  888   888  888    .o  888       
  o888bood8P'    ""888"" d888b    `Y8bod8P' o88'   888o     o888o     d888b    `Y888""""8o `Y8bod88P"" `Y8bod8P' d888b      
          .          ,-_/         .                 .                                                                  
          |-. . .    '  | . . ,-. | , ,-. ,-,-. ,-. |- . ,-.                                                           
          | | | |       | | | | | |<  | | | | | ,-| |  | |                                                             
          ^-' `-|       | `-^ ' ' ' ` `-' ' ' ' `-^ `' ' `-'                                                           
               /|    /` |                                                                                              
              `-'    `--'                                                                                              
-----------------------------------------------------------------------------------------------------------------------
");

        }

        private static void ConfigTraceLogging()
        {
            TextWriterTraceListener tr1 = new TextWriterTraceListener(System.Console.Out);
            Trace.Listeners.Add(tr1);
            Directory.CreateDirectory("LogFiles");
            string path = @"LogFiles\" + DateTime.Now.ToString("dd-MM-yyyy HH.mm.ss") + ".log";
            TextWriterTraceListener tr2 = new TextWriterTraceListener(System.IO.File.CreateText(path));
            Trace.Listeners.Add(tr2);
            Trace.AutoFlush = true;
            
        }

    }

   


}
