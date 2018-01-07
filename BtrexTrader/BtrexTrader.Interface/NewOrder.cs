using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BtrexTrader.Interface
{
    public class NewOrder
    {
        public string UniqueID;
        public string MarketDelta { get; set; }
        public string BUYorSELL { get; set; }
        public decimal Qty { get; set; }
        public decimal Rate { get; set; }
        public string CandlePeriod { get; set; }
        public Action<OpenOrder> DataUpdateCallback { get; set; }
        public Action<OpenOrder> ExecutionCompleteCallback { get; set; }

        public NewOrder(string ID, string mDelta, string BUYSELL, decimal quantity, decimal price, Action<OpenOrder> DUpdateCback = null, Action<OpenOrder> ExeCback = null, string cPeriod = null)
        {
            UniqueID = ID;
            MarketDelta = mDelta;
            BUYorSELL = BUYSELL.ToUpper();
            Qty = quantity;
            Rate = price;

            if (DUpdateCback != null)
                DataUpdateCallback = DUpdateCback;

            if (ExeCback != null)
                ExecutionCompleteCallback = ExeCback;

            if (cPeriod != null)
                CandlePeriod = cPeriod;
        }

    }
}
