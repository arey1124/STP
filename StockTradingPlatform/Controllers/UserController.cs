﻿using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.Dynamic;
using System.Linq;
using System.Net;
using System.Web;
using System.Web.Mvc;
using StockTradingPlatform.Models;
using StockTradingPlatform.Utils;
using System.Web.Helpers;
using System.Collections;


namespace StockTradingPlatform.Controllers
{
    public class UserController : Controller
    {
        private StpDBEntities db = new StpDBEntities();
        // GET: User
        public ActionResult Index()
        {
            return View();
        }

        //Arihant's Code
        public ActionResult Dashboard()
        {
            if (Session["user"] == null || Session["userName"] == null)
                return Redirect("~/Login.aspx");
            var user = Session["user"] as tblUser;
            int currentUser = user.uid;
            ViewBag.stocks = db.tblStocks.ToList();
            ViewBag.allUserReq = db.tblTradeRequests.Where(x => (x.uid != currentUser) && ( x.requestStatus != "D" && x.requestStatus != "C")).Take(5).ToList();
            ViewBag.ongoingReq = db.tblTradeRequests.Where(x => (x.uid == currentUser) && (x.requestStatus == "O" || x.requestStatus == "P")).Take(5).ToList();
            ViewBag.stockDetails = db.GetStocksData().ToList();
            return View();
        }
        

        public ActionResult Profile()
        {
            if (Session["user"] == null || Session["userName"] == null)
                return Redirect("~/Login.aspx");
            ViewBag.user = Session["user"] as tblUser;
            ViewBag.userName = Session["userName"].ToString();
            return View();
        }

        public ActionResult GetData(int id)
        {
            JsonResult result = new JsonResult();
            try
            {
                // Loading.  
                db.Configuration.ProxyCreationEnabled = false;
                var data = (from x in db.tblStocksPrices where x.stockId == id select x).ToList();
                data =  data.Skip(Math.Max(0, data.Count() - 10)).ToList();
                
                result = this.Json(data, JsonRequestBehavior.AllowGet);
            }
            catch (Exception ex)
            {
                // Info  
                Console.Write(ex);
            }
            return result;
        }

        public ActionResult GetUpdatedData()
        {
            JsonResult result = new JsonResult();
            result = this.Json(db.GetStocksData().ToList(), JsonRequestBehavior.AllowGet);
            return result;
        }

        public ActionResult Graph(int id, string stockName)
        {
            ArrayList xValues = new ArrayList();
            ArrayList yValues = new ArrayList();

            var results = (from x in db.tblStocksPrices where x.stockId == id select x);
            results.ToList().ForEach(rs => xValues.Add(rs.timeOfDay.ToString()));
            results.ToList().ForEach(rs => yValues.Add(rs.currentPrice));

            new Chart(width: 1000, height: 400, theme: ChartTheme.Vanilla)
                .AddTitle(stockName)
                .AddSeries("Default", chartType: "Area", xValue: xValues, yValues: yValues)
                .Write("bmp");
            return null;
        }

        public PartialViewResult GraphData(int id, string stockName)
        {
            List<string> data = new List<string>();
            data.Add(stockName);
            data.Add(Convert.ToString(id));
            return PartialView("Graph", data);
        }

        public ActionResult Logout()
        {
            Session["user"] = null;
            Session["userName"] = null;
            return Redirect("~/Login.aspx");
        }

        public ActionResult CancelRequest(int id)
        {
            var result = db.tblTradeRequests.SingleOrDefault(x => x.requestId == id);

            if (result != null)
            {
                if ( result.requestType == "B" )
                {
                    int reqQty = result.remainingQty ?? 0;
                    decimal reqPrice = result.requestPrice ?? 0;
                    var wallet = db.tblWallets.SingleOrDefault(x => x.uid == result.uid);
                    wallet.balance = wallet.balance + (reqQty * reqPrice);
                    db.SaveChanges();
                }
                else if( result.requestType == "S" )
                {
                    int reqQty = result.remainingQty ?? 0;
                    var holdings = db.tblHoldings.FirstOrDefault(x=>x.uid == result.uid && x.stockId == result.stockId);
                    holdings.remQty = holdings.remQty + reqQty;
                }
                result.requestStatus = "C";
                db.SaveChanges();
            }
            return Redirect(Request.UrlReferrer.PathAndQuery);
        }

        public ActionResult AllRequests()
        {
            if (Session["user"] == null || Session["userName"] == null)
                return Redirect("~/Login.aspx");
            var user = Session["user"] as tblUser;
            int currentUser = user.uid;
            ViewBag.allUserReq = db.tblTradeRequests.Where(x => (x.uid != currentUser) && (x.requestStatus != "D" && x.requestStatus != "C")).ToList();
            ViewBag.stocks = db.tblStocks.ToList();
            return View();
        }

        public ActionResult TradeRequest(int a, int? id)
        {
            if (Session["user"] == null || Session["userName"] == null)
                return Redirect("~/Login.aspx");
            if (a == 1)
            {
                ViewBag.stocks = db.tblStocks.ToList();
                ViewBag.type = "Add";
                ViewBag.Title = "Put a trade request";
                ViewBag.reqPrice = "";
                ViewBag.requestQty = "";
            }
            else if (a == 0)
            {
                var request = db.tblTradeRequests.SingleOrDefault(x => x.requestId == id);
                ViewBag.type = "Update";
                ViewBag.Title = "Update trade request";
                ViewBag.stocks = db.tblStocks.ToList();
                ViewBag.stockId = request.stockId;
                if (request.requestType == "B")
                {
                    ViewBag.requestType = "Buy";
                }
                else if (request.requestType == "S")
                {
                    ViewBag.requestType = "Sell";
                }
                ViewBag.reqPrice = request.requestPrice;
                ViewBag.requestQty = request.requestQty;
                ViewBag.requestId = request.requestId;
            }
            return View();
        }
        [HttpPost]
        public ActionResult TradeRequest(int? stockId, string requestType, decimal? reqPrice, int? reqQty, int? requestId, string Operation)
        {
            if (Session["user"] == null || Session["userName"] == null)
                return Redirect("~/Login.aspx");
            var user = Session["user"] as tblUser;
            TradeMatchingAlgo algo = new TradeMatchingAlgo();
            if (Operation == "Add")
            {
                if (requestType == "B")
                {
                    var balance = db.tblWallets.SingleOrDefault(x => x.uid == user.uid);
                    decimal reqp = reqPrice ?? 0;
                    int reqq = reqQty ?? 0;
                    decimal reqTotalPrice = reqp * reqq;
                    if (reqTotalPrice > balance.balance)
                    {
                        ViewBag.result = "Insufficient Funds . Unable to place a request";
                        ViewBag.stocks = db.tblStocks.ToList();
                        ViewBag.type = "Add";
                        ViewBag.Title = "Put a trade request";
                        ViewBag.reqPrice = "";
                        ViewBag.requestQty = "";
                        return View();
                    }
                    var accountBalance = db.tblWallets.SingleOrDefault(x => x.uid == user.uid);
                    if (accountBalance != null)
                    {
                        accountBalance.balance = (accountBalance.balance - reqTotalPrice);
                        this.db.SaveChanges();
                    }
                }
                else if(requestType == "S")
                {
                    var holdings = db.tblHoldings.SingleOrDefault(x => x.uid == user.uid && x.stockId == stockId);
                    int qty;
                    if (holdings == null)
                    {
                        qty = 0;
                    }
                    else
                    {
                        qty = holdings.remQty ?? 0;
                    }
                    
                    if (reqQty > qty)
                    {
                        ViewBag.result = "You do not have stocks to sell . Unable to place a request";
                        ViewBag.stocks = db.tblStocks.ToList();
                        ViewBag.type = "Add";
                        ViewBag.Title = "Put a trade request";
                        ViewBag.reqPrice = "";
                        ViewBag.requestQty = "";
                        return View();
                    }

                    holdings.remQty = holdings.remQty - reqQty;
                    db.SaveChanges();
                }

                tblTradeRequest tradeRequest = new tblTradeRequest();
                tradeRequest.uid = user.uid;
                tradeRequest.requestType = requestType;
                tradeRequest.stockId = stockId;
                tradeRequest.requestQty = reqQty;
                tradeRequest.remainingQty = reqQty;
                tradeRequest.requestPrice = reqPrice;
                tradeRequest.requestTime = DateTime.Now;
                tradeRequest.requestStatus = "O";
                this.db.tblTradeRequests.Add(tradeRequest);
                this.db.SaveChanges();
                algo.MatchingAlgo(tradeRequest);
                
                return Redirect("/User/Dashboard");
            }
            else if (Operation == "Update")
            {
                var result = db.tblTradeRequests.Single(x => x.requestId == requestId);
                if (result != null)
                {
                    result.requestQty = reqQty.Value;
                    result.remainingQty = reqQty.Value;
                    result.requestPrice = reqPrice.Value;
                    db.SaveChanges();
                    algo.MatchingAlgo(result);
                    return Redirect("/User/Dashboard");
                }
            }
            return View();
        }

        ////////////////////////////////////////////////////////////

        /// <summary>
        /// Prajjwal's Code
        /// </summary>
        //Prajjwal's Code

        public ActionResult UserTradeRequests()
        {
            if(Session["user"] == null || Session["userName"] == null)
                return Redirect("~/Login.aspx");
            var user = Session["user"] as tblUser;
            int id = user.uid;
            if (id == null)
            {
                return new HttpStatusCodeResult(HttpStatusCode.BadRequest);
            }
            ViewBag.id = id;
            var TradeRequestsByUid = db.tblTradeRequests.Include(t => t.tblStock).Include(t => t.tblUser).Where(t => t.uid == id);
            if (TradeRequestsByUid == null)
            {
                return HttpNotFound();
            }
            return View(TradeRequestsByUid.ToList());
        }

        public PartialViewResult UserTradeRequestsBuy(int? id)
        {
            return PartialView("TradeRequestsByUid", db.tblTradeRequests.Include(t => t.tblStock).Include(t => t.tblUser).Where(t => t.uid == id && t.requestType == "B"));
        }

        public PartialViewResult UserTradeRequestsSell(int? id)
        {
            return PartialView("TradeRequestsByUid", db.tblTradeRequests.Include(t => t.tblStock).Include(t => t.tblUser).Where(t => t.uid == id && t.requestType == "S"));
        }

        public ActionResult UserTransactions()
        {
            if (Session["user"] == null || Session["userName"] == null)
                return Redirect("~/Login.aspx");
            var user = Session["user"] as tblUser;
            int id = user.uid;
            ViewBag.id = id;
            return View(db.UserTransactionsDetailByUid(id).ToList());
        }

        public PartialViewResult UserTransactionsBuy(int? id)
        {
            return PartialView("TransactionsByUid", db.UserTransactionsDetailByUid(id).Where(x => x.reqType == "B"));
        }

        public PartialViewResult UserTransactionsSell(int? id)
        {
            return PartialView("TransactionsByUid", db.UserTransactionsDetailByUid(id).Where(x => x.reqType == "S"));
        }


        ////////////////////////////////////////////////////////////

        /// <summary>
        ///  Chayank's code starts
        /// </summary>

        // GET:
        public ActionResult UserWallet()
        {
            ///give session uid here for now given uid= 4
            ///
            if (Session["user"] == null || Session["userName"] == null)
                return Redirect("~/Login.aspx");

            var user = Session["user"] as tblUser;
            int uid = user.uid;
            UserManager manager = new UserManager();

            manager.tblHoldings = manager.StpDBEntities.tblHoldings.Include(t => t.tblStocks).Where(t => t.uid == uid && t.Qty > 0);
            manager.tblWallet = manager.StpDBEntities.tblWallets.SingleOrDefault(t => t.uid == uid);
            return View(manager);
        }

        public ActionResult AboutUs()
        {
            return View();
        }

            /// <summary>
            ///  Chayank's code ends
            /// </summary>

        }
}