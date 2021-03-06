#region Using declarations
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Xml.Serialization;
using NinjaTrader.Cbi;
using NinjaTrader.Data;
using NinjaTrader.Indicator;
using NinjaTrader.Gui.Chart;
using NinjaTrader.Strategy;
#endregion


namespace NinjaTrader.Strategy
{		
    #region Enums
		public enum TradeState
		{
			InitialStop,
			Breakeven,
			Trailing
		}

		public enum ExitType
		{
			Custom,
			IntialToBreakevenToTrailing,
			InitialToBreakevenToTrailingATR,
			TrailingATR,
			Simple
		}

    public enum TrendDirection
    {
        Long,
        Neutral,
        Short
    }
		#endregion
	public abstract  class BaseForexStrategy : Strategy, ICustomTypeDescriptor
	{
		protected TradeState _tradeState = TradeState.InitialStop;
		protected List<string> PropertiesExposed = new List<string>();

		#region Account variables
		private int _counter = 1; // Default for Counter
		protected double _equity = 20000; // Default for Equity

		protected IOrder _entry = null, _exit = null;
		

		#endregion
	
		#region Account properties

		[Description("Initial Equity of the account")]
		[GridCategory("Account")]
		public double Equity
		{
			get { return _equity; }
			set { _equity = Math.Max(1, value); }
		}



		[Description("A counter used for optimization runs")]
		[GridCategory("Account")]
		public int RunCounter
		{
			get { return _counter; }
			set { _counter = Math.Max(1, value); }
		}
		#endregion

		



		#region Abstract Methods
	   // the inherited strategy initializes here
		protected abstract void MyInitialize();
		protected abstract void SetupIndicatorProperties();
	   // to keep unique strategy stuff 
		protected abstract void MyOnBarUpdate();
		// when we are flat, we are looking for new opportunities to entry
		protected abstract void LookForEntry();
		protected abstract void LookForExit();

		// once we have entprotected abstract void LookForEntry();ied, we have to manage the positions until exit, and here, 
		//protected abstract void ManagePositions();
	   
 

		protected abstract void MyManagePosition();

		// this is called by an exit event from the positions, so we can tell the child strategy that the position was closed, and it can reset the signals
		// protected abstract void OnExit(object sender, int positionID);
		// we have always to update signals, even when we have no positions, we are always calculating them, to entry or to exit
		//   protected abstract void UpdateSignals();

		#endregion


		public BaseForexStrategy()
		{
			SetupIndicatorProperties();
		}

		#region methods


		/// <summary>
		/// Called on each bar update event (incoming tick)
		/// </summary>
		protected override void OnBarUpdate()
		{
			if (CurrentBar <= BarsRequired) return;

		 
			MyOnBarUpdate();

			if (StillHaveMoney)
			{
				if (IsFlat)
				{
				   SetStopLoss(CalculationMode.Ticks, _mmInitialSL);
					LookForEntry();
				}
				else
				{
					LookForExit();
					if (!IsFlat)
					{
						ManagePositions();
					}
				}
			}
		  
			
		}

		protected override void Initialize()
		{
			
		   
			//    ClearOutputWindow();
			
			CalculateOnBarClose = true;
			//Unmanaged = false;
			MyInitialize();

		}

 

		protected override void OnExecution(IExecution execution)
		{
			if (_entry == null) return;
			if (execution == null) return;
			if (execution.Order == null) return;

			bool isEntry = (_entry.Token == execution.Order.Token);
			bool isExit = !isEntry;

			if (isExit)
			{
				double diff = 0;

				IOrder exit = execution.Order;
				if (_entry.OrderAction == OrderAction.Buy)
					diff = exit.AvgFillPrice - _entry.AvgFillPrice;
				else if (_entry.OrderAction == OrderAction.Sell)
					diff = _entry.AvgFillPrice - exit.AvgFillPrice;

				//   double profit = ((diff * this.PointValue)) * _entry.Quantity;
				   double profit = diff * _entry.Quantity;


				_equity += profit;

				//				P("Profit=" + profit.ToString("C2") + ", Equity=" + _equity.ToString("C2"));
			}
		}

		protected void ManagePositions()
		{
			switch (_exitType)
			{
				case ExitType.IntialToBreakevenToTrailing:
					ManageTradeByBreakevenTrail();
					break;

				case ExitType.InitialToBreakevenToTrailingATR:
					ManageTradeByTrailingATR();
					break;

				case ExitType.TrailingATR:
					ManageTradeByTrailingATR();
					break;

				case ExitType.Simple:
					ManageTradeBySimple();
					break;
					// if type is set to Custom
				case ExitType.Custom:
					MyManagePosition();
					break;
				default: 
					break;

			}
		}

		protected void GoFlat()
		{
			if (IsLong) _exit = ExitLong();
			if (IsShort) _exit = ExitShort();
		}

		protected int ComputeQty(double volatilityRisk)
		{
			double dollarRisk = _equity * (_percentRisk / 100.0);
			double tickRisk = Round2Tick(volatilityRisk / this.TickSize);
			double qty = (dollarRisk / (volatilityRisk * this.PointValue));

			int rounded;

			// round the shares into a lot-friendly number, applies only to stocks
			//			rounded = (int) (Math.Round(qty/100.0, 0) * 100.0);

			rounded = (int)Math.Round(qty, 0);

			//			P("vol risk=" + volatilityRisk.ToString("N2") 
			//				+ ", $ risk=" + dollarRisk.ToString("C2") 
			//				+ ", equity=" + _equity.ToString("C2")
			//				+ ", qty=" + qty.ToString("N0") 
			//				+ ", rounded=" + rounded.ToString("N0")
			//				+ ", price=" + Close[0].ToString());

			return rounded;
		}

		protected void DrawLossLevel()
		{
			if (IsFlat) return;

			Color color = Color.Black;

			if (IsLong)
				color = Color.Magenta;
			else if (IsShort)
				color = Color.Cyan;

			this.DrawDiamond("d" + CurrentBar, true, 0, _lossLevel, color);
		}

		protected bool StillHaveMoney { get { return _equity > 0; } }

		#endregion


		#region methods MM
		protected void ManageTradeBySimple()
		{

			SetStopLoss(CalculationMode.Ticks, InitialStoploss);
			SetProfitTarget(CalculationMode.Ticks, ProfitTicks);
		   
			 DrawLossLevel();
		}

		protected void ManageTradeByTrailingATR()
		{
			 
			double risk = EMA(ATR(MMAtrPeriod), MMAtrEMAPeriod)[0] * MMAtrMultiplier;

			if (IsLong)
			{
				switch (_tradeState)
				{
					case TradeState.InitialStop:
						// switch to breakeven if possible and start trailing
						if (Close[0] > Position.AvgPrice + (TickSize * ProfitTicksBeforeBreakeven))
						{
							_lossLevel = Position.AvgPrice + (TickSize*BreakevenTicks);
							SetStopLoss("long", CalculationMode.Price, _lossLevel, true);
							_tradeState = TradeState.Trailing;
						}
						break;

					case TradeState.Trailing:
						if (Low[0] - risk > _lossLevel)
						{
							_lossLevel = Low[0] - risk;
							SetStopLoss("long", CalculationMode.Price, _lossLevel, true);
						}
						break;
				}

			}
			else if (IsShort)
			{
				switch (_tradeState)
				{
					case TradeState.InitialStop:
						// switch to breakeven if possible and start trailing
						if (Close[0] < Position.AvgPrice - (TickSize * ProfitTicksBeforeBreakeven))
						{
							_lossLevel = Position.AvgPrice - (TickSize * BreakevenTicks);
							SetStopLoss("short", CalculationMode.Price, _lossLevel, true);
							_tradeState = TradeState.Trailing;
						}
						break;

					case TradeState.Trailing:
						if (High[0] + risk < _lossLevel)
						{
							_lossLevel = High[0] + risk;
							SetStopLoss("short", CalculationMode.Price, _lossLevel, true);
						}
						break;
				}
			}
			DrawLossLevel();
		}



					//        propertiesToUse.Add("MMAtrMultiplier");
					//propertiesToUse.Add("MMAtrPeriod");
					//propertiesToUse.Add("MMAtrEMA");
					//propertiesToUse.Add("BreakevenTicks");
					//propertiesToUse.Add("InitialStoploss");
					//propertiesToUse.Add("ProfitTicksBeforeBreakeven");


		protected void ManageTradeByBreakevenTrail()
		{

			if (IsLong)
			{
				switch (_tradeState)
				{
					case TradeState.InitialStop:
						// switch to breakeven if possible and start trailing
						if (Close[0] > Position.AvgPrice + (TickSize * _mmProfitTicksBeforeBreakeven))
						{
							_lossLevel = Position.AvgPrice + TickSize * _mmBreakevenTicks;
							SetStopLoss(CalculationMode.Price, _lossLevel);
							_tradeState = TradeState.Trailing;
						}
						break;

					case TradeState.Trailing:
						if (Close[0] - TickSize * _mmTrailTicks > _lossLevel)
						{
							_lossLevel = Close[0] - TickSize * _mmTrailTicks;
							SetStopLoss(CalculationMode.Price, _lossLevel);
						}
						break;
				}

			}
			else if (IsShort)
			{
				switch (_tradeState)
				{
					case TradeState.InitialStop:
						// switch to breakeven if possible and start trailing
						if (Close[0] < Position.AvgPrice - (TickSize * _mmProfitTicksBeforeBreakeven))
						{
							_lossLevel = Position.AvgPrice - TickSize * _mmBreakevenTicks;
							SetStopLoss(CalculationMode.Price, _lossLevel);
							_tradeState = TradeState.Trailing;
						}
						break;

					case TradeState.Trailing:
						if (Close[0] + TickSize * _mmTrailTicks < _lossLevel)
						{
							_lossLevel = Close[0] + TickSize * _mmTrailTicks;
							SetStopLoss(CalculationMode.Price, _lossLevel);
						}
						break;
				}
			}
			DrawLossLevel();
		}
		#endregion


		#region Helpers
		protected bool IsFlat { get { return Position.MarketPosition == MarketPosition.Flat; } }
		protected bool IsLong { get { return Position.MarketPosition == MarketPosition.Long; } }
		protected bool IsShort { get { return Position.MarketPosition == MarketPosition.Short; } }

		protected double Round2Tick(double val) { return Instrument.MasterInstrument.Round2TickSize(val); }
		protected double PointValue { get { return this.Instrument.MasterInstrument.PointValue; } }

		protected void P(string msg)
		{
			Print(Time[0].ToShortDateString() + " " + Time[0].ToShortTimeString() + "::" + msg);
		}
		#endregion


		#region Money management variables

		protected ExitType _exitType = ExitType.Custom;
		protected double _risk;
		protected double _percentRisk = 1.0; // Default for PercentRisk
		protected double _lossLevel;

		// breakeven trailstop
		protected int _mmProfitTicksBeforeBreakeven = 25;
		protected int _mmInitialSL = 100;
		protected int _mmBreakevenTicks = 2;
		protected int _mmTrailTicks = 25;

		protected int _mmTakeProfitTicks = 30;

		// atr multiplier trail stop
		protected double _mmAtrMultiplier = 3.0;
		protected int _mmAtrPeriod = 10;
		protected int _mmAtrEMAperiod = 5;
		#endregion

		#region Money management properties

		[Description("")]
		[GridCategory("Money management")]
		[RefreshProperties(RefreshProperties.All)]
		public ExitType ExitStrategy
		{
			get { return _exitType; }
			set { _exitType = value; }
		}

		[Description("Risk per trade in %")]
		[GridCategory("Money management")]
		public double PercentRisk
		{
			get { return _percentRisk; }
			set { _percentRisk = Math.Max(0.001, value); }
		}

		[Description("Ticks in profit before moving stoploss to breakeven(-ish)")]
		[GridCategory("Money management")]
		public int ProfitTicksBeforeBreakeven
		{
			get { return _mmProfitTicksBeforeBreakeven; }
			set { _mmProfitTicksBeforeBreakeven = Math.Max(1, value); }
		}

		[Description("Initial stoploss in ticks")]
		[GridCategory("Money management")]
		public int InitialStoploss
		{
			get { return _mmInitialSL; }
			set { _mmInitialSL = Math.Max(1, value); }
		}

		[Description("Take profit at ticks")]
		[GridCategory("Money management")]
		public int ProfitTicks
		{
			get { return _mmTakeProfitTicks; }
			set { _mmTakeProfitTicks = Math.Max(1, value); }
		}

		[Description("Ticksbeyond breakeven to move from initial stop")]
		
		[GridCategory("Money management")]
		public int BreakevenTicks
		{
			get { return _mmBreakevenTicks; }
			set { _mmBreakevenTicks = Math.Max(1, value); }
		}

		[Description("Trailing stop ticks, when starting to trail from breakeven")]
		[GridCategory("Money management")]
		public int TrailTicks
		{
			get { return _mmTrailTicks; }
			set { _mmTrailTicks = Math.Max(1, value); }
		}


		[Description("Period for trailing ATR")]
		[GridCategory("Money management")]
		public int MMAtrPeriod
		{
			get { return _mmAtrPeriod; }
			set { _mmAtrPeriod = Math.Max(1, value); }
		}

		[Description("Multiplier for trailing ATR")]
		[GridCategory("Money management")]
		public double MMAtrMultiplier
		{
			get { return _mmAtrMultiplier; }
			set { _mmAtrMultiplier = Math.Max(0.1 , value); }
		}

		[Description("Multiplier for trailing ATR")]
		[GridCategory("Money management")]
		public int MMAtrEMAPeriod
		{
			get { return _mmAtrEMAperiod; }
			set { _mmAtrEMAperiod = Math.Max(1, value); }
		}
		#endregion


		#region base Properties

		protected int _smaPeriod = 10; // Default setting for EMASlowPeriod
		[Description("Period for the SMA")]
		[GridCategory("ParametersBase")]
		public int SMAPeriod
		{
			get { return _smaPeriod; }
			set { _smaPeriod = Math.Max(1, value); }
		}

		protected int _smaSlowPeriod = 10; // Default setting for EMASlowPeriod
		[Description("Period for the slow SMA")]
		[GridCategory("ParametersBase")]
		public int SMASlowPeriod
		{
			get { return _emaSlowPeriod; }
			set { _emaSlowPeriod = Math.Max(1, value); }
		}

		protected int _smaFastPeriod = 5; // Default setting for EMAFastPeriod
		[Description("Period for the fast SMA")]
		[GridCategory("ParametersBase")]
		public int SMAFastPeriod
		{
			get { return _emaFastPeriod; }
			set { _emaFastPeriod = Math.Max(1, value); }
		}

		protected int _emaSlowPeriod = 10; // Default setting for EMASlowPeriod
		[Description("Period for the slow EMA")]
		[GridCategory("ParametersBase")]
		public int EMASlowPeriod
		{
			get { return _emaSlowPeriod; }
			set { _emaSlowPeriod = Math.Max(1, value); }
		}

		protected int _emaFastPeriod = 5; // Default setting for EMAFastPeriod
		[Description("Period for the fast EMA")]
		[GridCategory("ParametersBase")]
		public int EMAFastPeriod
		{
			get { return _emaFastPeriod; }
			set { _emaFastPeriod = Math.Max(1, value); }
		}

		protected int _bollingerPeriod = 14; // Default setting for RSIPeriod
		[Description("Period for Bollinger")]
		[GridCategory("ParametersBase")]
		public int BollingerPeriod
		{
			get { return _bollingerPeriod; }
			set { _bollingerPeriod = Math.Max(1, value); }
		}

		protected double _bollingerStdDev = 2; // Default setting for RSIPeriod
		[Description("Period for Bollinger Std dev")]
		[GridCategory("ParametersBase")]
		public double BollingerStdDev
		{
			get { return _bollingerStdDev; }
			set { _bollingerStdDev = Math.Max(1, value); }
		}

		protected int _rsiPeriod = 10; // Default setting for RSIPeriod
		[Description("Period for RSI")]
		[GridCategory("ParametersBase")]
		public int RSIPeriod
		{
			get { return _rsiPeriod; }
			set { _rsiPeriod = Math.Max(1, value); }
		}

		protected int _rsiSmooth = 3; // Default setting for RSIPeriod
		[Description("RSI smoothing")]
		[GridCategory("ParametersBase")]
		public int RSISmooth
		{
			get { return _rsiSmooth; }
			set { _rsiSmooth = Math.Max(1, value); }
		}


		protected int _rsiLower = 45;
		[Description("Period for RSI lower")]
		[GridCategory("ParametersBase")]
		public int RSILower
		{
			get { return _rsiLower; }
			set { _rsiLower = Math.Max(1, value); }
		}

		protected int _rsiUpper = 55;
		[Description("Period for RSI upper")]
		[GridCategory("ParametersBase")]
		public int RSIUpper
		{
			get { return _rsiUpper; }
			set { _rsiUpper = Math.Max(1, value); }
		}


		protected int _stochPeriodD = 14;
		[Description("Period for Stochastics PeriodD (slow)")]
		[GridCategory("ParametersBase")]
		public int StochPeriodD
		{
			get { return _stochPeriodD; }
			set { _stochPeriodD = Math.Max(1, value); }
		}
		protected int _stochPeriodK = 7;
		[Description("Period for Stochastics PeriodK (fast)")]
		[GridCategory("ParametersBase")]
		public int StochPeriodK
		{
			get { return _stochPeriodK; }
			set { _stochPeriodK = Math.Max(1, value); }
		}

		protected int _stochSmooth = 3;
		[Description("Period for Stochastics smoothing")]
		[GridCategory("ParametersBase")]
		public int StochSmooth
		{
			get { return _stochSmooth; }
			set { _stochSmooth = Math.Max(1, value); }
		}


		protected int _stochUpper = 80;
		[Description("Period for Stochastics upper")]
		[GridCategory("ParametersBase")]
		public int StochUpper
		{
			get { return _stochUpper; }
			set { _stochUpper = Math.Max(1, value); }
		}
		protected int _stochLower = 20;
		[Description("Period for Stochastics lower")]
		[GridCategory("ParametersBase")]
		public int StochLower
		{
			get { return _stochLower; }
			set { _stochLower = Math.Max(1, value); }
		}




		protected int _macdFast = 12;
		[Description("Period for MACD Fast")]
		[GridCategory("ParametersBase")]
		public int MACDFast
		{
			get { return _macdFast; }
			set { _macdFast = Math.Max(1, value); }
		}
		protected int _macdSlow = 26;
		[Description("Period for MACD Slow")]
		[GridCategory("ParametersBase")]
		public int MACDSlow
		{
			get { return _macdSlow; }
			set { _macdSlow = Math.Max(1, value); }
		}
		protected int _macdSmooth = 9;
		[Description("Period for MACD smoothing")]
		[GridCategory("ParametersBase")]
		public int MACDSmooth
		{
			get { return _macdSmooth; }
			set { _macdSmooth = Math.Max(1, value); }
		}






		protected int _adxPeriod = 10; // Default setting for ADXPeriod
		[Description("Period for ADX")]
		[GridCategory("ParametersBase")]
		public int ADXPeriod
		{
			get { return _adxPeriod; }
			set { _adxPeriod = Math.Max(1, value); }
		}

		protected int _adxMin = 20;
		[Description("Minimum for ADX")]
		[GridCategory("ParametersBase")]
		public int ADXMinimum
		{
			get { return _adxMin; }
			set { _adxMin = Math.Max(1, value); }
		}

		protected int _atrPeriod = 10;
		[Description("Period for ATR")]
		[GridCategory("ParametersBase")]
		public int ATRPeriod
		{
			get { return _atrPeriod; }
			set { _atrPeriod = Math.Max(1, value); }
		}

		protected double _atrExclusionMultiplier = 1;
		[Description("ATR multiplier for exluding trades")]
		[GridCategory("ParametersBase")]
		public double ATRExclusionMultiplier
		{
			get { return _atrExclusionMultiplier; }
			set { _atrExclusionMultiplier = Math.Max(1, value); }
		}

		protected int _crossoverLookbackPeriod = 1;
		[Description("Lookback period for crossover convergence")]
		[GridCategory("ParametersBase")]
		public int CrossoverLookbackPeriod
		{
			get { return _crossoverLookbackPeriod; }
			set { _crossoverLookbackPeriod = Math.Max(1, value); }
		}



		protected int _tradeableTimeStartHour = 0;
		[Description("Tradeable time start hour")]
		[GridCategory("ParametersBase")]
		public int TradeableTimeStartHour
		{
			get { return _tradeableTimeStartHour; }
			set { _tradeableTimeStartHour = Math.Max(0, value); }
		}
		protected int _tradeableTimeStartMinute = 0;
		[Description("Tradeable time start minute")]
		[GridCategory("ParametersBase")]
		public int TradeableTimeStartMinute
		{
			get { return _tradeableTimeStartMinute; }
			set { _tradeableTimeStartMinute = Math.Max(0, value); }
		}
		protected int _tradeableTimeEndHour = 23;
		[Description("Tradeable time end hour")]
		[GridCategory("ParametersBase")]
		public int TradeableTimeEndHour
		{
			get { return _tradeableTimeEndHour; }
			set { _tradeableTimeEndHour = Math.Max(0, value); }
		}
		protected int _tradeableTimeEndMinute = 59;
		[Description("Tradeable time end minute")]
		[GridCategory("ParametersBase")]
		public int TradeableTimeEndMinute
		{
			get { return _tradeableTimeEndMinute; }
			set { _tradeableTimeEndMinute = Math.Max(0, value); }
		}


		#endregion

		#region calculator related
		public enum LotSize
		{
			Standard = 100000,
			Mini = 10000,
			Micro = 1000,
			Single = 1
		}

		public class ForexCalculator
		{
			public static int GetPositionSize(double accountBalance, double risk, int stoplossTicks, double pipValue)
			{
				double res = (accountBalance * risk) / (stoplossTicks * pipValue);

				return Convert.ToInt32(Math.Floor(res));
			}

			public static double GetPipValue(string instrument, LotSize lot, double tickSize, double askPrice)
			{
				if (instrument.EndsWith("USD"))
					return tickSize * (double)lot;

				if (instrument.StartsWith("USD"))
					return tickSize * (double)lot / askPrice;


				RelatedPairs.RelatedPair relPair = RelatedPairs.GetRelatedPairs()[instrument];
				if (relPair.Instrument.StartsWith("USD"))
				{
					return tickSize * (double)lot / relPair.Rate;
				}


				throw new Exception("GetPipValue: The relative pair instrument or rate could not be determined (probably GBPUSD or other where USD is quote currency");

			}
		}

		public class RelatedPairs : Dictionary<string, RelatedPairs.RelatedPair>
		{
			public class RelatedPair
			{
				public string Instrument;
				public double Rate;
			}

			public static RelatedPairs GetRelatedPairs()
			{
				return new RelatedPairs
								   {
									   {"AUDJPY", new RelatedPair {Instrument = "USDJPY", Rate = 79.88}},
										 {"AUDCAD", new RelatedPair {Instrument = "USDCAD", Rate = 0.99559}},
							 //          {"EURGBP", new RelatedPair {Instrument = "GBPUSD", Rate = 0.8104}},
							 //          {"GBPCHF", new RelatedPair {Instrument = "EURUSD", Rate = 1.3085}}
								   };
			}
		}


		#endregion

		#region Custom Property Manipulation

		private void ModifyMoneyManagementProperties(PropertyDescriptorCollection col)
		{




			List<string> propertiesToUse = new List<string>();
			propertiesToUse.Add("ExitStrategy");

		   switch (ExitStrategy)
			{
				case ExitType.IntialToBreakevenToTrailing:
					propertiesToUse.Add("BreakevenTicks");
					propertiesToUse.Add("InitialStoploss");
					propertiesToUse.Add("ProfitTicksBeforeBreakeven");
					propertiesToUse.Add("TrailTicks");
					break;

			   case ExitType.InitialToBreakevenToTrailingATR:
					propertiesToUse.Add("MMAtrMultiplier");
					propertiesToUse.Add("MMAtrPeriod");
					propertiesToUse.Add("MMAtrEMAPeriod");
					propertiesToUse.Add("InitialStoploss");
					propertiesToUse.Add("BreakevenTicks");
					propertiesToUse.Add("ProfitTicksBeforeBreakeven");
					break;

				case ExitType.TrailingATR:
					propertiesToUse.Add("MMAtrMultiplier");
					propertiesToUse.Add("MMAtrPeriod");
					propertiesToUse.Add("MMAtrEMAPeriod");
					propertiesToUse.Add("BreakevenTicks");
					propertiesToUse.Add("ProfitTicksBeforeBreakeven");
					break;

			   case ExitType.Simple:
					propertiesToUse.Add("InitialStoploss");
					propertiesToUse.Add("ProfitTicks");
			   
					break;

			   case ExitType.Custom:
					foreach (PropertyDescriptor propDesc in col)
					{

						if (propDesc != null)
						{
							if (propDesc.Category != null)
							{
								if (propDesc.Category.Contains("management") && propDesc.Name != "ExitStrategy")
								{
									try
									{
										PropertyDescriptor tmp = col.Find(propDesc.Name, true);
										if (tmp != null)
										{
											col.Remove(tmp);
										}
										else { Print("was null"); }
									}
									catch (NullReferenceException ex)
									{
										Print(ex.ToString());
									}
								}
							}

						}
					}
					break;
				

			}

			foreach (PropertyDescriptor propDesc in col)
			{

				if (propDesc != null)
				{
					if (propDesc.Category != null)
					{
						if (propDesc.Category.Contains("management") &&
					   !propertiesToUse.Contains(propDesc.DisplayName))
						{
							try
							{
								PropertyDescriptor tmp = col.Find(propDesc.Name, true);
								if (tmp != null)
								{
									col.Remove(tmp);
								}
								else { Print("was null"); }
							}
							catch (NullReferenceException ex)
							{
								Print(ex.ToString());
							}
						}
					}
				   
				}
			}
		}

		protected void ModifyIndicatorProperties(PropertyDescriptorCollection col)
		{
			foreach (PropertyDescriptor propDesc in col)
			{
				// wtf propdesc can be null????????
				if (propDesc != null && propDesc.Category != null)
				{
					if ((propDesc.Category.Equals("ParametersBase")) && !PropertiesExposed.Contains(propDesc.Name))
					{
						try
						{
							PropertyDescriptor tmp = col.Find(propDesc.Name, true);
							if (tmp != null)
							{
								col.Remove(tmp);
							}
							else { Print("was null"); }

						}
						catch (NullReferenceException ex)
						{
							Print(ex.ToString());
						}
						
					}
				}
			}
		}

		#endregion

		#region ICustomTypeDescriptor Members

		public AttributeCollection GetAttributes()
		{
			return TypeDescriptor.GetAttributes(GetType());
		}

		public string GetClassName()
		{
			return TypeDescriptor.GetClassName(GetType());
		}

		public string GetComponentName()
		{
			return TypeDescriptor.GetComponentName(GetType());
		}

		public TypeConverter GetConverter()
		{
			return TypeDescriptor.GetConverter(GetType());
		}

		public EventDescriptor GetDefaultEvent()
		{
			return TypeDescriptor.GetDefaultEvent(GetType());
		}

		public PropertyDescriptor GetDefaultProperty()
		{
			return TypeDescriptor.GetDefaultProperty(GetType());
		}

		public object GetEditor(Type editorBaseType)
		{
			return TypeDescriptor.GetEditor(GetType(), editorBaseType);
		}

		public EventDescriptorCollection GetEvents(Attribute[] attributes)
		{
			return TypeDescriptor.GetEvents(GetType(), attributes);
		}

		public EventDescriptorCollection GetEvents()
		{
			return TypeDescriptor.GetEvents(GetType());
		}

		public PropertyDescriptorCollection GetProperties(Attribute[] attributes)
		{
			PropertyDescriptorCollection orig = TypeDescriptor.GetProperties(GetType(), attributes);
			PropertyDescriptor[] arr = new PropertyDescriptor[orig.Count];
			orig.CopyTo(arr, 0);
			PropertyDescriptorCollection col = new PropertyDescriptorCollection(arr);

			ModifyMoneyManagementProperties(col);
			ModifyIndicatorProperties(  col);

			return col;

		}

	 
 
		public PropertyDescriptorCollection GetProperties()
		{
			return TypeDescriptor.GetProperties(GetType());
		}

		public object GetPropertyOwner(PropertyDescriptor pd)
		{
			return this;
		}

		#endregion


	 
 
	}
}
