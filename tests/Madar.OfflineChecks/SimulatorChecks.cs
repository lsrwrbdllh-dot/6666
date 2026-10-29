using System.Globalization;
using System.Text.Json;
using Madar.Core;

internal static class SimulatorChecks
{
    internal static void Run(string root, Action<bool,string> check)
    {
        var simulator=new TrainingSimulator(Path.Combine(root,"simulator-checks"));
        JsonElement Call(string action,object input,string owner="store:employee-a",TrainingSimulator? instance=null) => JsonSerializer.SerializeToElement((instance??simulator).Execute(owner,action,JsonSerializer.SerializeToElement(input)),Store.Json);
        void Reject(Action action,string name) { try{action();}catch(InvalidOperationException){check(true,name);return;}throw new Exception(name); }
        var catalog=Call("simulator.catalog",new{});
        check(catalog.GetProperty("scenarios").GetArrayLength()==6,"six independent financial practice scenarios available");
        var session=Call("simulator.start",new{kind="credit-sale",difficulty=3});
        var id=session.GetProperty("id").GetString()!;
        var givens=session.GetProperty("givens").EnumerateArray().ToDictionary(x=>x.GetProperty("label").GetString()!,x=>x.GetProperty("text").GetString()!);
        decimal Given(string label)=>decimal.Parse(givens[label].Split(' ')[0],CultureInfo.InvariantCulture);
        var qty=Given("الكمية");var price=Given("سعر بيع الحبة");var cost=Given("تكلفة الحبة");var stock=Given("الكمية قبل البيع");var cash=Given("الصندوق قبل البيع");var paid=Given("الدفعة المقبوضة");
        var wrong=Call("simulator.answer",new{id,step=0,answer="cash",requestId=Guid.NewGuid().ToString()});
        check(wrong.GetProperty("step").GetInt32()==0&&!wrong.GetProperty("lastCorrect").GetBoolean(),"wrong training answer keeps the question and gives a hint");
        var key=Guid.NewGuid().ToString();
        var first=Call("simulator.answer",new{id,step=0,answer="credit",requestId=key});
        var duplicate=Call("simulator.answer",new{id,step=0,answer="credit",requestId=key});
        check(duplicate.GetProperty("attempts").GetInt32()==first.GetProperty("attempts").GetInt32(),"duplicate training submission does not double-count or advance");
        Reject(()=>Call("simulator.answer",new{id,step=0,answer="credit",requestId=Guid.NewGuid().ToString()}),"stale training form cannot advance a later question");
        Reject(()=>Call("simulator.get",new{id},"store:employee-b"),"employee cannot read another employee's practice session");
        var reopened=new TrainingSimulator(Path.Combine(root,"simulator-checks"));
        check(Call("simulator.get",new{id},instance:reopened).GetProperty("step").GetInt32()==1,"practice state survives reopening the simulator");
        var invalidAttempts=first.GetProperty("attempts").GetInt32();
        Reject(()=>Call("simulator.answer",new{id,step=1,answer="not-a-number",requestId=Guid.NewGuid().ToString()}),"invalid training input is rejected without counting an attempt");
        check(Call("simulator.get",new{id}).GetProperty("attempts").GetInt32()==invalidAttempts,"invalid submission leaves progress unchanged");
        var expected=new[]{qty*price,qty*price-paid,cash+paid,stock-qty,qty*(price-cost)};
        for(var i=0;i<expected.Length;i++)
        {
            var text=expected[i].ToString("0.##",CultureInfo.InvariantCulture);
            if(i==0)text=string.Concat(text.Select(c=>c is >= '0' and <= '9'?(char)('٠'+c-'0'):c=='.'?'٫':c));
            session=Call("simulator.answer",new{id,step=i+1,answer=text,requestId=Guid.NewGuid().ToString()});
        }
        check(session.GetProperty("completed").GetBoolean()&&session.GetProperty("score").GetInt32()==83,"credit exercise completes with objective first-attempt scoring and Arabic numerals");
        check(Call("simulator.catalog",new{}).GetProperty("recommendedDifficulty").GetInt32()==2,"practice performance adapts the suggested next difficulty");
        var facts=JsonSerializer.SerializeToElement(simulator.Facts("store:employee-a",id),Store.Json);
        check(facts.GetProperty("simulation").GetBoolean()&&!facts.TryGetProperty("cash",out _)&&facts.GetProperty("summary").GetString()!.Contains("لا توجد بيانات أو أرصدة حقيقية"),"AI coaching context is explicitly simulated and has no live cash facts");
        using var financial=new Store(Path.Combine(root,"simulation-ledger-crosscheck"));
        JsonElement Finance(string action,object? input=null)=>JsonSerializer.SerializeToElement(financial.Execute(action,JsonSerializer.SerializeToElement(input??new{})),Store.Json);
        Finance("bootstrap",new{name="تدريب",pin="123456",currency="وحدة تدريب"});Finance("shift.open");
        Finance("cash.post",new{kind="Capital",amount=cash+stock*cost,note="تجربة تحقق فقط",requestId=Guid.NewGuid().ToString()});
        Finance("products.save",new{name="صنف",baseUnit="حبة",price,minimum=0});
        var unit=Finance("products").GetProperty("units")[0].GetProperty("id").GetInt64();
        Finance("invoice.post",new{kind="Purchase",warehouseId=1,full=true,requestId=Guid.NewGuid().ToString(),lines=new[]{new{unitId=unit,quantity=stock,price=cost}}});
        var party=Finance("parties.save",new{name="عميل تدريب",kind="Customer",dueDays=30}).GetInt64();
        Finance("invoice.post",new{kind="Sale",warehouseId=1,partyId=party,full=false,paid,requestId=Guid.NewGuid().ToString(),lines=new[]{new{unitId=unit,quantity=qty,price}}});
        var actual=Finance("assistant.facts",new{days=1});
        check(actual.GetProperty("cash").GetInt64()==(cash+paid)*100&&actual.GetProperty("receivable").GetInt64()==(qty*price-paid)*100&&actual.GetProperty("netProfit").GetInt64()==qty*(price-cost)*100,"simulated credit-sale outcomes agree with actual posted SQLite accounting");
        var documents=Finance("documents").GetArrayLength();
        Call("simulator.start",new{kind="cash-sale"});
        check(Finance("documents").GetArrayLength()==documents,"practice operations never post financial documents");
    }
}
