﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using MySql.Data.MySqlClient;

namespace StockMarketServer {
    enum BidOffer { bid, offer }
    public enum Segments { RapidlyRising, Rising, Falling, RapidlyFalling }
    public enum Stance { ShortTermLong, ShortTermShort }
    class AlgorithmsTrader1 : AlgoTrader {
        DateTime LastTurn;
        string TargetStock;
        public List<StockTurn> StockTurns = new List<StockTurn>();
        List<MarketStance> stances = new List<MarketStance>();


        public double ShortRequirement { get; private set; }
        public double LongRequirement { get; private set; }
        public int MinAmount { get; private set; }
        public int MaxAmount { get; private set; }
        public double Aggression { get; private set; }
        ThreadDataBaseHandler threadDataBaseHandler { get { return AlgoTraderManager.threadDataBaseHandler; } }

		/// <summary>/
		///Takes a look at last two cycles
		///establishes trend
		///purchases or sell depending on position eg int or short
		/// </summary>
		/// <param name="ID"> Traders ID in manager </param>
		/// <param name="target"> Stock this trader is targeting </param>
		/// <param name="ClientID"> The ClientID who balance and invetory this trader will use </param>
		/// <param name="ShortTermShortRequirement"> The size of the trend need for the trader to take a stance on a short term short </param>
		/// <param name="ShortTermLongRequirement"> The size of the trend need for the trader to take a stance on a short term long </param>
		/// <param name="MinAmount"> Minimum amount of stocks per stance </param>
		/// <param name="MaxAmount"> Maximum amount of stocks per stance </param>
		/// <param name="Aggresion"> The aggresion the percentage of max stock the trader will assign per stance </param>
		public AlgorithmsTrader1(int ID, string target, int ClientID, double ShortTermShortRequirement, double ShortTermLongRequirement, int MinAmount, int MaxAmount, double Aggresion) {
            this.ID = ID;
            TargetStock = target;
            UserID = ClientID;
            this.ShortRequirement = ShortTermShortRequirement;
            this.LongRequirement = ShortTermLongRequirement;
            this.MinAmount = MinAmount;
            this.MaxAmount = MaxAmount;
            this.Aggression = Aggression;
        }

		//Called by AlgoTraderManager
        public override void RunTurn() {
			//Grab all the trades since last turn
            string s = "SELECT Price FROM Trades WHERE Time > '" + LastTurn.ToString("yyyy-MM-dd HH:mm:ss") + "' AND StockName = '" + TargetStock + "'";
            MySqlDataReader reader = threadDataBaseHandler.GetData(s);
            List<double> Trades = new List<double>();
            while (reader.Read()) {
                Trades.Add((double)reader["Price"]);
            }
            threadDataBaseHandler.CloseCon();
            if (Trades.Count == 0) {
                return;
            }
			//Turn the trades into a stock turn and it into the list
            StockTurns.Add(new StockTurn(Trades));
			//Create a short term stance
            CreateNewStance(true);
            if (StockTurns.Count >= 150) {
                StockTurns.RemoveAt(0);
            }
            foreach (MarketStance MS in stances) {
                MS.RunTurn();
            }
            stances.RemoveAll((MarketStance ms) => ms.isCompleted);
            LastTurn = DateTime.Now;
            threadDataBaseHandler.CloseCon();
        }

        void CreateNewStance(bool ShortTerm) {
			//If the price of the stock is too low then invest in the stock because at this price
			//level it will likely start increase soon
            if (StockTurns[StockTurns.Count - 1].AveragePrice < 1f) {
                new MarketStance(Stance.ShortTermLong, 1000, UserID, TargetStock, this, threadDataBaseHandler);
            }
			//At the moment this algo trader only does short term trade stances so this will always be true
            if (ShortTerm) {
				// Need atleast 4 turns in order to take a stance
                if (StockTurns.Count < 4) {
                    return;
                }
				//Sum the trend of the last three turns using enum cast as int
                int TotalLast3Turns = 0;
                for (int i = 1; i <= 3; i++) {
                    TotalLast3Turns += (int)StockTurns[StockTurns.Count - i].Trend + 1;
                }
                double AverageLast3Turns = (double)TotalLast3Turns / 3f;
				//If average is large then the stock price is falling
				//If it is small then it is rising
                if (AverageLast3Turns <= LongRequirement) {//1.6f
                    stances.Add(new MarketStance(Stance.ShortTermLong, MathsHelper.Lerp(MinAmount, MaxAmount, 1f - (AverageLast3Turns / LongRequirement) + Aggression), UserID, TargetStock, this, threadDataBaseHandler));
                } else if (AverageLast3Turns >= ShortRequirement) {//2.4f
                    stances.Add(new MarketStance(Stance.ShortTermShort, MathsHelper.Lerp(MinAmount, MaxAmount, (AverageLast3Turns / LongRequirement) + Aggression), UserID, TargetStock, this, threadDataBaseHandler));
                }
            }
        }

        class MarketStance {
            public Stance stance;
            int client;
            double SuccessPrice;
            double FailurePrice;
            int Quanity;
            AlgoTrader Owner;
            public bool isCompleted = false;
            bool OfferPlaced = false;
            string TargetStock;
            DateTime StartTime;
            int RequiredTime;
            ThreadDataBaseHandler threadDataBaseHandler;
			//This class now stores the information need for the stance and is called every turn to check it vs its conditions for success and failure
            public MarketStance(Stance s, int Quanity, int ClientID, string TargetStock, AlgoTrader owner, ThreadDataBaseHandler threadDataBaseHandler) {
                this.threadDataBaseHandler = threadDataBaseHandler;
                stance = s;
                Owner = owner;
                client = ClientID;
                this.TargetStock = TargetStock;
                StartTime = DateTime.Now;
                double CurrentPrice = threadDataBaseHandler.GetCountDouble("SELECT SUM(CurrentPrice) FROM Stock WHERE StockName = '" + this.TargetStock + "'");
                CurrentPrice = Math.Round(CurrentPrice, 2);
                switch (stance) {
                    case Stance.ShortTermLong:
						//For a long the trader believes the market will go up so success price if above current price
                        this.Quanity = Quanity;
                        SuccessPrice = CurrentPrice + 0.02f;
                        FailurePrice = CurrentPrice - 0.01f;
                        RequiredTime = 5;
                        ShortTermLong(Quanity, CurrentPrice);
                        break;
                    case Stance.ShortTermShort:
						//For a short the trader believes the market will go doen so success price if below current price
						this.Quanity = Quanity;
                        SuccessPrice = CurrentPrice - 0.02f;
                        FailurePrice = CurrentPrice + 0.01f;
                        RequiredTime = 5;
                        ShortTermShort(Quanity, CurrentPrice);
                        break;
                }
				//Close DB thread for other parts of the program to be free to connect to DB
                threadDataBaseHandler.CloseCon();
            }

			//Place offer into pool
            private void ShortTermShort(int Quanity, double Price) {
                threadDataBaseHandler.SetData(string.Format("INSERT INTO Pool (Type, Price, User, StockName, Quantity) VALUES ({0}, {1}, {2}, '{3}', {4})", (int)BidOffer.offer, Price, client, TargetStock, Quanity));
                Console.WriteLine("Places short with " + Quanity);
            }

			//Place bid into pool
            void ShortTermLong(int Quanity, double Price) {
                threadDataBaseHandler.SetData(string.Format("INSERT INTO Pool (Type, Price, User, StockName, Quantity) VALUES ({0}, {1}, {2}, '{3}', {4})", (int)BidOffer.bid, Price, client, TargetStock, Quanity));
                Console.WriteLine("Places Long with " + Quanity);
            }

            public void RunTurn() {
                double CurrentPrice = threadDataBaseHandler.GetCountDouble("SELECT SUM(CurrentPrice) FROM Stock WHERE StockName = '" + TargetStock + "'");
                TimeSpan TimeTaken = DateTime.Now - StartTime;
                CurrentPrice = Math.Round(CurrentPrice, 2);
                switch (stance) {
					//Check stance against conditions then excute the completion of the stance
                    case Stance.ShortTermLong:
                        if ((CurrentPrice >= SuccessPrice || CurrentPrice < FailurePrice) && !OfferPlaced && TimeTaken.TotalMinutes > RequiredTime) {
                            threadDataBaseHandler.SetData(string.Format("INSERT INTO Pool (Type, Price, User, StockName, Quantity) VALUES ({0}, {1}, {2}, '{3}', {4})", (int)BidOffer.offer, CurrentPrice, client, TargetStock, Quanity));
                            OfferPlaced = true;
                            isCompleted = true;
                            Console.WriteLine("Finished long with " + Quanity);
                        }
                        break;
                    case Stance.ShortTermShort:
                        if ((CurrentPrice <= SuccessPrice || CurrentPrice > FailurePrice) && !OfferPlaced && TimeTaken.TotalMinutes > RequiredTime) {
                            threadDataBaseHandler.SetData(string.Format("INSERT INTO Pool (Type, Price, User, StockName, Quantity) VALUES ({0}, {1}, {2}, '{3}', {4})", (int)BidOffer.bid, CurrentPrice, client, TargetStock, Quanity));
                            OfferPlaced = true;
                            isCompleted = true;
                            Console.WriteLine("Finished short with " + Quanity);
                        }
                        break;

                }
            }
        }
    }

    class StockTurn {
        public double OpeningPrice;
        public double LowPrice;
        public double HighPrice;
        public double ClosePrice;
        public double AveragePrice;
        public Segments Trend;
        public StockTurn(List<double> trades) {
			//assign a segment (trend) by taking the list of prices from the trades and doing basic trend analysis
            OpeningPrice = trades[0];
            LowPrice = trades.OrderBy((double t) => t).ToList()[0];
            HighPrice = trades.OrderByDescending((double t) => t).ToList()[0];
            ClosePrice = trades[trades.Count - 1];
            double TotalPrice = 0;
            foreach (double t in trades) {
                TotalPrice += t;
            }
            AveragePrice = TotalPrice / (double)trades.Count;
            Trend = AssignSegment();
        }
        Segments AssignSegment() {
            if (ClosePrice > OpeningPrice) {
                if (ClosePrice > AveragePrice) {
                    return Segments.RapidlyRising;
                } else {
                    return Segments.Rising;
                }
            } else {
                if (ClosePrice > AveragePrice) {
                    return Segments.Falling;
                } else {
                    return Segments.RapidlyFalling;
                }
            }
        }
    }
}


