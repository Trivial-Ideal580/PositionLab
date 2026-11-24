using System;
using System.Collections.Generic;
using System.Linq;

namespace PositionLab {
sealed class Model {
    internal static readonly string[] Features={"ROE（百分点）","市盈率 PE","消息情绪","20 日动量","20 日年化波动"};
    internal double Probability, Accuracy, Baseline, Brier, Auc, Threshold;
    internal int Train, Test, Matched;
    internal DateTime Forecast, TestStart, TrainEnd;
    internal double[] Correlations, Coefficients;

    internal static Model Fit(Series s, Dictionary<DateTime,Context> context) {
        var x = new List<double[]>(); var y = new List<double>(); var forward = new List<double>();
        var dates = new List<DateTime>();
        double[] latest=null;
        for(int i=20;i<s.Bars.Length;i++) {
            Context c;
            if(!context.TryGetValue(s.Bars[i].Date,out c)) continue;
            var inputs=new[]{c.Roe,c.Pe,c.News,s.V["MOM20"][i],s.V["VOL20"][i]};
            if(i==s.Bars.Length-1) { latest=inputs; continue; }
            double ret=s.V["RET1"][i+1];
            x.Add(inputs); dates.Add(s.Bars[i].Date); forward.Add(ret); y.Add(ret>0 ? 1 : 0);
        }
        if(latest==null) throw new FormatException("最新行情日需有同日已知的基本面和消息情绪快照。");
        if(x.Count<100) throw new FormatException("模型至少需要 100 条具备次日结果的匹配快照，当前 " + x.Count + " 条。");
        int split=(int)(x.Count*.7), train=split-1, test=x.Count-split;
        int positives=(int)y.Take(train).Sum();
        if(positives<10||train-positives<10) throw new FormatException("训练集至少需要 10 个上涨和 10 个未上涨交易日。");
        var mean=new double[Features.Length]; var std=new double[Features.Length];
        var result=new Model {Train=train, Test=test, Matched=x.Count, Forecast=s.Bars.Last().Date, TrainEnd=dates[train-1],TestStart=dates[split],
            Threshold=(double)positives/train, Correlations=new double[Features.Length]};
        var trainReturns=forward.Take(train).ToArray();
        for(int j=0;j<Features.Length;j++) {
            var values=x.Take(train).Select(row=>row[j]).ToArray();
            mean[j]=values.Average(); std[j]=Stats.Std(values);
            result.Correlations[j]=Stats.Corr(values,trainReturns);
        }
        Func<double[],double[]> scale=row=>{
            var f=new double[Features.Length+1]; f[0]=1;
            for(int j=0;j<Features.Length;j++) f[j+1]=std[j]>0 ? Math.Max(-8,Math.Min(8,(row[j]-mean[j])/std[j])) : 0;
            return f;
        };
        var design=x.Take(train).Select(scale).ToArray();
        var weights=new double[Features.Length+1];
        for(int step=0;step<220;step++) {
            var gradient=new double[weights.Length];
            for(int i=0;i<train;i++) {
                var row=design[i]; double residual=Predict(weights,row)-y[i];
                for(int j=0;j<weights.Length;j++) gradient[j]+=residual*row[j];
            }
            for(int j=0;j<weights.Length;j++) weights[j]-=.2*(gradient[j]/train+(j==0?0:.03*weights[j]));
        }
        int correct=0, baseCorrect=0; double brier=0;
        var scored=new List<Tuple<double,double>>();
        for(int i=split;i<x.Count;i++) {
            double p=Predict(weights,scale(x[i]));
            scored.Add(Tuple.Create(p,y[i]));
            correct+=(p>=.5)==(y[i]==1)?1:0;
            baseCorrect+=(result.Threshold>=.5)==(y[i]==1)?1:0;
            brier+=(p-y[i])*(p-y[i]);
        }
        result.Probability=Predict(weights,scale(latest));
        result.Accuracy=(double)correct/test;
        result.Baseline=(double)baseCorrect/test;
        result.Brier=brier/test;
        result.Coefficients=weights.Skip(1).ToArray();
        result.Auc=AucOf(scored);
        return result;
    }
    static double Predict(double[] w,double[] x) {
        double z=0; for(int j=0;j<w.Length;j++) z+=w[j]*x[j];
        return 1/(1+Math.Exp(-z));
    }
    static double AucOf(List<Tuple<double,double>> scores) {
        var ordered=scores.OrderBy(x=>x.Item1).ToArray(); double rankSum=0, positive=0;
        for(int i=0;i<ordered.Length;) {
            int end=i+1;
            while(end<ordered.Length&&ordered[end].Item1==ordered[i].Item1) end++;
            for(int j=i;j<end;j++) if(ordered[j].Item2==1) { rankSum+=(i+1+end)/2.0; positive++; }
            i=end;
        }
        return positive==0||positive==ordered.Length ? double.NaN :
            (rankSum-positive*(positive+1)/2)/(positive*(ordered.Length-positive));
    }
}
sealed class PositionRow {
    internal Holding Lot;
    internal double Last, Value, Pnl, Weight, Days, Momentum, Ma20;
    internal bool? EnteredAboveMean;
}
sealed class Portfolio {
    internal PositionRow[] Positions;
    internal DateTime AsOf;
    internal double Cash, Total, Profit, Top, Hhi, Days, Trend, AboveMean, TrendCoverage, EntryTrend, EntryCoverage, Volatility, Drawdown;
    internal int RiskSamples;
    internal bool MissingRisk;
    internal string Style;
    internal static Portfolio Analyze(Dictionary<string,Series> market, List<Holding> holdings,double cash) {
        if(holdings.Count==0) throw new FormatException("请输入至少一笔持仓。");
        string[] symbols=holdings.Select(h=>h.Symbol).Distinct().ToArray();
        var missing=symbols.Where(s=>!market.ContainsKey(s)).Take(8).ToArray();
        if(missing.Length>0) throw new FormatException("缺少行情：" + string.Join("、",missing));
        DateTime asOf=symbols.Min(s=>market[s].Bars.Last().Date);
        var indexes=symbols.ToDictionary(s=>s,s=>Array.FindIndex(market[s].Bars,b=>b.Date==asOf));
        if(indexes.Values.Any(i=>i<0)) throw new FormatException("各持仓标的在共同估值日 " + Data.Day(asOf) + " 缺少行情。");
        var result=new Portfolio {Cash=cash, AsOf=asOf,Volatility=double.NaN,Drawdown=double.NaN,Trend=double.NaN,AboveMean=double.NaN,EntryTrend=double.NaN};
        result.Positions=holdings.Select(h=>{
            if(h.Bought>asOf) throw new FormatException(h.Symbol+" 买入日晚于估值日。");
            int i=indexes[h.Symbol]; var s=market[h.Symbol]; double last=s.Bars[i].Close;
            int session=FindSession(s,h.Bought);
            bool? entry=session>=20 ? (bool?)(s.Bars[session-1].Close>s.V["MA20"][session-1]) : null;
            return new PositionRow {Lot=h,Last=last,Value=last*h.Qty,Pnl=(last-h.Cost)*h.Qty,
                Days=(asOf-h.Bought).TotalDays,Momentum=s.V["MOM20"][i],Ma20=s.V["MA20"][i],EnteredAboveMean=entry};
        }).OrderByDescending(p=>p.Value).ToArray();
        double invested=result.Positions.Sum(p=>p.Value); result.Total=cash+invested;
        result.Profit=result.Positions.Sum(p=>p.Pnl);
        foreach(var p in result.Positions) p.Weight=p.Value/result.Total;
        var weights=result.Positions.GroupBy(p=>p.Lot.Symbol).ToDictionary(g=>g.Key,g=>g.Sum(p=>p.Weight));
        result.Top=weights.Values.Max();
        result.Hhi=weights.Values.Sum(w=>w*w)+Math.Pow(cash/result.Total,2);
        result.Days=result.Positions.Sum(p=>p.Value*p.Days)/invested;
        double coverage=result.Positions.Where(p=>!double.IsNaN(p.Momentum)).Sum(p=>p.Value);
        result.TrendCoverage=coverage/invested;
        if(coverage>0) {
            result.Trend=result.Positions.Where(p=>p.Momentum>0).Sum(p=>p.Value)/coverage;
            result.AboveMean=result.Positions.Where(p=>!double.IsNaN(p.Momentum)&&p.Last>p.Ma20).Sum(p=>p.Value)/coverage;
        }
        double entryCoverage=result.Positions.Where(p=>p.EnteredAboveMean.HasValue).Sum(p=>p.Value);
        result.EntryCoverage=entryCoverage/invested;
        if(entryCoverage>0) result.EntryTrend=result.Positions.Where(p=>p.EnteredAboveMean==true).Sum(p=>p.Value)/entryCoverage;
        result.Style=result.Days<=15 ? "短周期持仓" : result.Days<=90 ? "中周期持仓" : "长周期持仓";
        int depth=symbols.Min(s=>indexes[s])+1; depth=Math.Min(61,depth);
        var curve=new double[depth]; var returns=new double[Math.Max(0,depth-1)];
        for(int j=0;j<depth;j++) {
            DateTime date=market[symbols[0]].Bars[indexes[symbols[0]]-depth+1+j].Date;
            double ret=0;
            foreach(string symbol in symbols) {
                int i=indexes[symbol]-depth+1+j;
                if(market[symbol].Bars[i].Date!=date) { result.MissingRisk=true; return result; }
                if(j>0) ret+=weights[symbol]*market[symbol].V["RET1"][i];
            }
            curve[j]=j==0 ? 1 : curve[j-1]*(1+ret);
            if(j>0) returns[j-1]=ret;
        }
        result.RiskSamples=returns.Length;
        if(returns.Length>=20) { result.Volatility=Stats.Std(returns)*Math.Sqrt(252); result.Drawdown=Stats.Drawdown(curve); }
        return result;
    }
    static int FindSession(Series series,DateTime date) {
        int lo=0, hi=series.Bars.Length-1;
        while(lo<=hi) {
            int mid=lo+(hi-lo)/2;
            int order=series.Bars[mid].Date.CompareTo(date);
            if(order==0) return mid;
            if(order<0) lo=mid+1;
            else hi=mid-1;
        }
        return -1;
    }
}
sealed class Demo {
    internal Dictionary<string,Series> Market;
    internal Dictionary<DateTime,Context> Context;
    internal static Demo Create() {
        var random=new Random(571);
        var data=new Demo {Market=new Dictionary<string,Series>(), Context=new Dictionary<DateTime,Context>()};
        foreach(string symbol in new[]{"DEMO-A","DEMO-B","DEMO-C"}) {
            var list=new List<Bar>(); var day=new DateTime(2024,1,2);
            double price=50+25*data.Market.Count, priorNews=0;
            for(int i=0;i<516;i++) {
                while(day.DayOfWeek==DayOfWeek.Saturday||day.DayOfWeek==DayOfWeek.Sunday) day=day.AddDays(1);
                double open=price*Math.Exp((random.NextDouble()-.5)*.014);
                price=open*Math.Exp(.0004+Math.Sin(i/11.0)*.007+(random.NextDouble()-.5)*.042+.005*priorNews);
                priorNews=random.NextDouble()*2-1;
                list.Add(new Bar{Date=day,Open=open,Close=price,High=Math.Max(open,price)*1.008,Low=Math.Min(open,price)*.992,Volume=1e6*(.4+random.NextDouble()*1.2)});
                if(symbol=="DEMO-A") data.Context.Add(day,new Context{Roe=10+2*Math.Sin(i/70.0),Pe=19+5*Math.Cos(i/55.0),News=priorNews});
                day=day.AddDays(1);
            }
            data.Market.Add(symbol,new Series(symbol,list));
        }
        return data;
    }
}
}


