using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BtrexTrader.Interface
{
    public class OpenOrder
    {
        public string OrderUuid { get; set; }
        public string Exchange { get; set; }
        public string Type { get; set; }
        public decimal Quantity { get; set; }
        public decimal QuantityRemaining { get; set; }
        public decimal Limit { get; set; }
        public decimal Reserved { get; set; }
        public decimal TotalQuantity { get; set; }
        public decimal TotalReserved { get; set; }
        public decimal CommissionReserved { get; set; }
        public decimal CommissionReserveRemaining { get; set; }
        public decimal CommissionPaid { get; set; }
        public decimal Price { get; set; }
        public decimal PricePerUnit { get; set; }
        public DateTime Opened { get; set; }
        public DateTime? Closed { get; set; }
        public bool IsOpen { get; set; }
        public string CandlePeriod { get; set; }
        public Action<OpenOrder> DataUpdateCallback { get; set; }
        public Action<OpenOrder> ExecutionCompleteCallback { get; set; }


        public OpenOrder(GetOrderResult ord, decimal totalQty, decimal totalRes, Action<OpenOrder> cBack_Data, Action<OpenOrder> cBack_Exe, string periodName = null)
        {
            OrderUuid = ord.OrderUuid;
            Exchange = ord.Exchange;
            Type = ord.Type;
            TotalQuantity = totalQty;
            TotalReserved = totalRes;
            Quantity = ord.Quantity;
            QuantityRemaining = ord.QuantityRemaining;
            Limit = ord.Limit;
            Reserved = ord.Reserved;
            CommissionReserved = ord.CommissionReserved;
            CommissionReserveRemaining = ord.CommissionReserveRemaining;
            CommissionPaid = ord.CommissionPaid;
            Price = ord.Price;
            PricePerUnit = 0M;
            Opened = ord.Opened;
            DataUpdateCallback = cBack_Data;
            ExecutionCompleteCallback = cBack_Exe;
            IsOpen = true;

            if (periodName != null)
                CandlePeriod = periodName;

        }


        public OpenOrder(string ID, string delta, string orderType, decimal totalQty, decimal totalRes, decimal qty, decimal qtyRemains, decimal limit_price, decimal BTCreserved, decimal feeReserved, decimal feeRemains, decimal feePaid, decimal BTCprice, decimal PPU, DateTime TimeOpened, Action<OpenOrder> cBack_Data, Action<OpenOrder> cBack_Exe, string periodName = null)
        {
            OrderUuid = ID;
            Exchange = delta;
            Type = orderType;
            TotalQuantity = totalQty;
            TotalReserved = totalRes;
            Quantity = qty;
            QuantityRemaining = qtyRemains;
            Limit = limit_price;
            Reserved = BTCreserved;
            CommissionReserved = feeReserved;
            CommissionReserveRemaining = feeRemains;
            CommissionPaid = feePaid;
            Price = BTCprice;
            PricePerUnit = PPU;
            Opened = TimeOpened;
            DataUpdateCallback = cBack_Data;
            ExecutionCompleteCallback = cBack_Exe;
            IsOpen = true;

            if (periodName != null)
                CandlePeriod = periodName;

        }


        public void UpdateOrder(GetOrderResult ord)
        {
            //UPDATE TOTALS:
            if (ord.Type.ToUpper() == "LIMIT_SELL")
                TotalReserved += ord.Price - Price;
            else if (ord.Type.ToUpper() == "LIMIT_BUY")
                TotalQuantity += QuantityRemaining - ord.QuantityRemaining;
            
            QuantityRemaining = ord.QuantityRemaining;
            Reserved = ord.Reserved;
            Price = ord.Price;
            CommissionReserved = ord.CommissionReserved;
            CommissionReserveRemaining = ord.CommissionReserveRemaining;
            CommissionPaid = ord.CommissionPaid;

            //CALL DataUpdateCallback:
            DataUpdateCallback(this);
        }


        public void CompleteOrder(GetOrderResult ord)
        {
            //UPDATE TOTALS:
            if (ord.Type.ToUpper() == "LIMIT_SELL")
                TotalReserved += ord.Price - Price;
            else if (ord.Type.ToUpper() == "LIMIT_BUY")
                TotalQuantity += QuantityRemaining - ord.QuantityRemaining;

            Limit = ord.Limit;
            Quantity = ord.Quantity;
            QuantityRemaining = ord.QuantityRemaining;
            Reserved = ord.Reserved;
            Price = ord.Price;
            CommissionReserved = ord.CommissionReserved;
            CommissionReserveRemaining = ord.CommissionReserveRemaining;
            CommissionPaid = ord.CommissionPaid;
            Closed = ord.Closed;
            IsOpen = false;

            //CALL ExecutionCallback:
            ExecutionCompleteCallback(this);
        }

        public void ReplaceOrder(GetOrderResult oldOrder, GetOrderResult newOrder)
        {
            OrderUuid = newOrder.OrderUuid;

            if (newOrder.Type.ToUpper() == "LIMIT_SELL")
                TotalReserved += (oldOrder.Price - Price) + newOrder.Price;
            else if (newOrder.Type.ToUpper() == "LIMIT_BUY")
                TotalQuantity += (QuantityRemaining - oldOrder.QuantityRemaining) + (newOrder.Quantity - newOrder.QuantityRemaining);

            Limit = newOrder.Limit;
            Quantity = newOrder.Quantity;
            QuantityRemaining = newOrder.QuantityRemaining;
            Reserved = newOrder.Reserved;
            Price = newOrder.Price;
            CommissionReserved = newOrder.CommissionReserved;
            CommissionReserveRemaining = newOrder.CommissionReserveRemaining;
            CommissionPaid = newOrder.CommissionPaid;

            //CALL DataUpdateCallback:
            DataUpdateCallback(this);

        }

    }
}
