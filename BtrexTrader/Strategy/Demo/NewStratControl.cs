using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using BtrexTrader.Interface;
using BtrexTrader.Data;

namespace BtrexTrader.Strategy.Demo
{
    class NewStratControl
    {        
        //POSSIBLY CONVERT BtrexData.Markets to ConcurrentDict, 
        //  replace all 'foreach (Market)' with indexed lookup 
        private IReadOnlyList<string> SpecificDeltas = new List<string>() { "BTC-ETH", "BTC-NEO", "BTC-XLM", "BTC-QTUM", "BTC-OMG"};
        List<NewStratData> mData { get; private set; }

        public async Task Initialize()
        {
            mData = new List<NewStratData>();
            await SubTopMarketsByVol(10);
            //await SubSpecificMarkets();
        }

        public async Task StartDisplayMarkets()
        {

        }


        private async Task SubTopMarketsByVol(int n)
        {
            List<string> topMarkets = await BtrexREST.TradeMethods.GetTopMarketsByBVbtcOnly(n);
            foreach (string mk in topMarkets)
            { 
                await BtrexWS.subscribeMarket("BTC-" + mk);
                
            }
        }

        private async Task SubSpecificMarkets()
        {
            foreach (string mk in SpecificDeltas)
            {
                await BtrexWS.subscribeMarket("BTC-" + mk);
                //Add data obj
            }
        }

    }
}
