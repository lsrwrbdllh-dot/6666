using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace Madar.Core;

/// <summary>Practice data and objective scoring live outside the financial store.</summary>
public sealed class TrainingSimulator
{
    private readonly string database;
    private readonly object gate = new();
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    public sealed record Choice(string Id, string Text);
    private sealed record Question(string Id, string Text, string Kind, string Expected, string Hint, string Explanation, Choice[]? Choices = null);
    private sealed record Scenario(string Id, string Title, string Description);
    private sealed record Parameters(int Quantity, int Price, int Cost, int Cash, int Stock, int Paid, int Factor);
    private sealed record Progress(string Kind, int Difficulty, Parameters Values, int Step = 0, int Attempts = 0,
        int FirstTryCorrect = 0, int StepAttempts = 0, string Feedback = "", bool? LastCorrect = null, string LastAnswer = "", string RequestId = "");
    private static readonly Scenario[] Scenarios =
    [
        new("cash-sale", "بيع نقدي", "أنشئ فهمك لإجمالي البيع ورصيد الصندوق والمخزون والربح."),
        new("credit-sale", "بيع آجل بدفعة", "ميّز بين قيمة الفاتورة، والمبلغ المقبوض، ومديونية العميل."),
        new("purchase-units", "شراء بوحدات مختلفة", "حوّل الكرتون إلى الوحدة الأساسية واحسب تكلفة الحبة وأثر الشراء."),
        new("debt-receipt", "تحصيل مديونية", "فرّق بين تحصيل فاتورة قديمة وتسجيل بيع جديد."),
        new("cash-count", "مراجعة وردية الصندوق", "احسب فرق الجرد وحدد مسار المراجعة والأثر المحاسبي."),
        new("stock-count", "تسوية المخزون", "افهم فرق الكمية وقيمة النقص وأهمية توثيق السبب.")
    ];

    public TrainingSimulator(string directory)
    {
        Directory.CreateDirectory(directory);
        database = Path.Combine(Path.GetFullPath(directory), "simulator.db");
        using var db = Open();
        using var cmd = db.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS Sessions(Id TEXT PRIMARY KEY,Owner TEXT NOT NULL,Kind TEXT NOT NULL,State TEXT NOT NULL,Completed INTEGER NOT NULL,Score INTEGER NOT NULL,UpdatedAt TEXT NOT NULL);
            CREATE INDEX IF NOT EXISTS SessionOwner ON Sessions(Owner,UpdatedAt);
            """;
        cmd.ExecuteNonQuery();
    }

    private SqliteConnection Open()
    {
        var db = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = database, Pooling = false, DefaultTimeout = 10 }.ToString());
        db.Open();
        using var command = db.CreateCommand(); command.CommandText = "PRAGMA journal_mode=WAL; PRAGMA busy_timeout=10000;"; command.ExecuteNonQuery();
        return db;
    }

    public object Execute(string owner, string action, JsonElement args)
    {
        if (string.IsNullOrWhiteSpace(owner) || owner.Length > 120) throw new InvalidOperationException("سجّل الدخول لبدء التدريب.");
        lock (gate)
        {
            using var db = Open();
            using var write = action is "simulator.start" or "simulator.answer" ? db.BeginTransaction(deferred: false) : null;
            if (action == "simulator.catalog") return Catalog(db, owner);
            if (action == "simulator.start")
            {
                var kind = args.GetProperty("kind").GetString();
                if (!Scenarios.Any(s => s.Id == kind)) throw new InvalidOperationException("اختر سيناريو تدريب صحيحاً.");
                var difficulty = args.TryGetProperty("difficulty", out var level) && level.TryGetInt32(out var selected) && selected is >= 1 and <= 3 ? selected : Recommended(db, owner, write);
                var seed = RandomNumberGenerator.GetInt32(0, 9);
                var p = new Parameters(2 + seed % (difficulty + 2), (10 + seed) * 100 + (difficulty > 1 ? 50 : 0),
                    (5 + seed % 4) * 100, (100 + seed * 20) * 100, 15 + seed, (5 + seed) * 100, 12);
                var state = new Progress(kind!, difficulty, p);
                var id = Guid.NewGuid().ToString("N"); Save(db, owner, id, state, insert: true, transaction: write); write!.Commit();
                return View(id, state);
            }
            var sessionId = args.GetProperty("id").GetString() ?? "";
            var current = Read(db, owner, sessionId, write);
            if (action == "simulator.get") return View(sessionId, current);
            if (action == "simulator.answer")
            {
                var request = args.GetProperty("requestId").GetString() ?? "";
                if (!Guid.TryParse(request, out _)) throw new InvalidOperationException("أعد إرسال الإجابة من شاشة التدريب.");
                if (request == current.RequestId) return View(sessionId, current);
                var questions = Questions(current);
                if (current.Step >= questions.Length) throw new InvalidOperationException("اكتمل التدريب؛ ابدأ سيناريو جديداً.");
                if (args.GetProperty("step").GetInt32() != current.Step) throw new InvalidOperationException("تغيرت خطوة التدريب؛ حدّث الشاشة قبل الإجابة.");
                if (current.Attempts >= 200) throw new InvalidOperationException("ابدأ جلسة تدريب جديدة لمتابعة هذا السيناريو.");
                var question = questions[current.Step];
                var raw = args.GetProperty("answer");
                var answer = raw.ValueKind == JsonValueKind.String ? raw.GetString()!.Trim() : raw.GetRawText();
                if (string.IsNullOrWhiteSpace(answer) || answer.Length > 120) throw new InvalidOperationException("اكتب إجابة قصيرة وصحيحة.");
                bool correct;
                if (question.Kind == "choice")
                    correct = answer == question.Expected;
                else
                {
                    var normalized = Normalize(answer);
                    if (!decimal.TryParse(normalized, NumberStyles.AllowDecimalPoint | NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var number)
                        || number < 0 || number > 100000000 || decimal.Round(number, 2) != number)
                        throw new InvalidOperationException("أدخل رقماً موجباً بدقة منزلتين كحد أقصى.");
                    correct = number == decimal.Parse(question.Expected, CultureInfo.InvariantCulture);
                }
                current = current with
                {
                    Step = current.Step + (correct ? 1 : 0), Attempts = current.Attempts + 1,
                    FirstTryCorrect = current.FirstTryCorrect + (correct && current.StepAttempts == 0 ? 1 : 0),
                    StepAttempts = correct ? 0 : current.StepAttempts + 1, LastCorrect = correct,
                    LastAnswer = answer, RequestId = request,
                    Feedback = correct ? question.Explanation : "راجع الخطوة: " + question.Hint
                };
                Save(db, owner, sessionId, current, transaction: write); write!.Commit();
                return View(sessionId, current);
            }
            throw new InvalidOperationException("عملية التدريب غير معروفة.");
        }
    }

    public object Facts(string owner, string id)
    {
        lock (gate)
        {
            using var db = Open();
            var state = Read(db, owner, id);
            var scenario = Scenarios.Single(s => s.Id == state.Kind);
            var questions = Questions(state);
            var q = state.Step < questions.Length ? questions[state.Step] : null;
            var givens = Givens(state);
            var summary = "هذه محاكاة تدريب فقط؛ لا توجد بيانات أو أرصدة حقيقية للمتجر.\n" + scenario.Title + ": " + string.Join("، ", givens.Select(p => p.Label + ": " + p.Text))
                + (q is null ? "\nاكتملت الجلسة. راجع المبادئ التي تعلمها الموظف." : "\nالخطوة الحالية: " + q.Text + "\nالإجابة المرجعية: " + q.Expected + ". " + q.Explanation)
                + "\nآخر ملاحظة تدريبية: " + state.Feedback + "\nاشرح السبب المحاسبي ووجّه المتدرّب للخطوة؛ لا تنفذ عمليات حقيقية.";
            return new { simulation = true, simulationId = id, title = scenario.Title, step = state.Step, summary, values = givens };
        }
    }

    private static string Normalize(string value)
    {
        return string.Concat(value.Select(c => c is >= '٠' and <= '٩' ? (char)('0' + c - '٠') : c is >= '۰' and <= '۹' ? (char)('0' + c - '۰') : c == '٫' ? '.' : c));
    }
    private static string N(decimal value) => value.ToString("0.##", CultureInfo.InvariantCulture);
    private static string M(int cents) => N(cents / 100m);
    public sealed record Given(string Label, string Text);
    private static Given[] Givens(Progress s)
    {
        var p = s.Values;
        return s.Kind switch
        {
            "cash-sale" or "credit-sale" => [new("العملية", s.Kind == "cash-sale" ? "بيع نقدي كامل" : "بيع آجل بدفعة جزئية"), new("الصنف", "منتج تدريب"),
                new("الكمية", N(p.Quantity) + " حبات"), new("سعر بيع الحبة", M(p.Price) + " وحدة تدريب"), new("تكلفة الحبة", M(p.Cost) + " وحدة تدريب"),
                new("الكمية قبل البيع", N(p.Stock)), new("الصندوق قبل البيع", M(p.Cash)), new("الدفعة المقبوضة", M(s.Kind == "cash-sale" ? p.Quantity * p.Price : p.Paid))],
            "purchase-units" => [new("العملية", "شراء نقدي"),new("عدد الكراتين", N(p.Quantity)),new("حبات في الكرتون", N(p.Factor)),
                new("سعر شراء الكرتون", M(p.Factor * p.Cost)),new("الكمية الحالية بالحبات", N(p.Stock)),new("الصندوق قبل الشراء", M(p.Cash + 50000))],
            "debt-receipt" => [new("العملية", "تحصيل جزء من دين عميل قديم"),new("مديونية العميل", M(p.Cash)),new("المقبوض الآن", M(p.Paid)),new("رصيد الصندوق قبل التحصيل", "100")],
            "cash-count" => [new("الرصيد المتوقع", M(p.Cash)),new("النقد المعدود", M(p.Cash - p.Paid)),new("الحالة", "فرق جرد يحتاج سبباً ومراجعة")],
            _ => [new("كمية النظام", N(p.Stock)),new("الكمية المعدودة", N(p.Stock - p.Quantity)),new("تكلفة الحبة", M(p.Cost)),new("الحالة", "نقص جرد موثّق؛ ليست فاتورة شراء")]
        };
    }

    private static Question[] Questions(Progress s)
    {
        var p = s.Values;
        Question Money(string id, string text, int cents, string hint, string explanation) => new(id, text, "money", M(cents), hint, explanation);
        Question Count(string id, string text, int value, string hint, string explanation) => new(id, text, "quantity", N(value), hint, explanation);
        Question Choice(string id, string text, string expected, string hint, string explanation, params Choice[] choices) => new(id, text, "choice", expected, hint, explanation, choices);
        var total = p.Quantity * p.Price;
        var paid = s.Kind == "cash-sale" ? total : p.Paid;
        var sale = new[]
        {
            Choice("kind", "ما العملية المناسبة؟", s.Kind == "cash-sale" ? "cash" : "credit", "قارن المبلغ المقبوض بإجمالي الفاتورة.", "الدفعة الجزئية تبقي مديونية على العميل، بينما البيع النقدي الكامل يسدد الفاتورة كلها.", new("cash","بيع نقدي كامل"), new("credit","بيع آجل بدفعة"), new("purchase","شراء")),
            Money("total", "كم إجمالي الفاتورة؟", total,"الإجمالي = الكمية × سعر البيع.", $"الإجمالي {N(p.Quantity)} × {M(p.Price)} = {M(total)} وحدة تدريب."),
            Money("cash", "كم يصبح رصيد الصندوق بعد البيع؟", p.Cash + paid,"أضف المبلغ المقبوض فقط إلى الرصيد السابق.", $"الصندوق = {M(p.Cash)} + {M(paid)} = {M(p.Cash + paid)}؛ الجزء الآجل ليس نقداً."),
            Count("stock", "كم حبة تبقى في المخزون؟",p.Stock-p.Quantity,"اطرح الكمية المباعة من كمية البداية.",$"المخزون = {p.Stock} − {p.Quantity} = {p.Stock-p.Quantity} حبة."),
            Money("profit", "كم الربح الإجمالي لهذه العملية؟", p.Quantity*(p.Price-p.Cost),"ربح العملية = الكمية × (سعر البيع − تكلفة الحبة).",$"الربح {M(p.Quantity*(p.Price-p.Cost))}، ويختلف عن المبيعات والنقد المقبوض.")
        };
        return s.Kind switch
        {
            "cash-sale" => sale,
            "credit-sale" => [sale[0],sale[1],Money("debt","كم يتبقى على العميل؟",total-p.Paid,"اطرح الدفعة المقبوضة من إجمالي الفاتورة.",$"المديونية = {M(total)} − {M(p.Paid)} = {M(total-p.Paid)}."),..sale.Skip(2)],
            "purchase-units" => [
                Choice("kind","ما المستند المناسب لإدخال هذه البضاعة؟","purchase","البضاعة مشتراة وليست فرق جرد.","فاتورة الشراء تثبت المخزون وقيمة الدفع؛ التسوية لا تحل محل الشراء.",new("purchase","فاتورة شراء"),new("adjust","تسوية مخزون"),new("sale","فاتورة بيع")),
                Count("received","كم حبة تصل إلى المخزون؟",p.Quantity*p.Factor,"عدد الكراتين × حبات الكرتون.",$"تدخل {p.Quantity*p.Factor} حبة بالوحدة الأساسية."),
                Money("total","كم إجمالي الشراء؟",p.Quantity*p.Factor*p.Cost,"عدد الكراتين × سعر الكرتون.",$"إجمالي الشراء {M(p.Quantity*p.Factor*p.Cost)} وحدة تدريب."),
                Money("unit-cost","كم تكلفة شراء الحبة؟",p.Cost,"اقسم سعر الكرتون على عدد الحبات فيه.",$"تكلفة الحبة {M(p.Cost)} وحدة تدريب؛ سعر الكرتون ليس سعر الحبة."),
                Count("stock","كم يصبح المخزون بالحبات؟",p.Stock+p.Quantity*p.Factor,"أضف الحبات المستلمة إلى كمية البداية.",$"المخزون يصبح {p.Stock+p.Quantity*p.Factor} حبة.")],
            "debt-receipt" => [
                Choice("kind","أي مستند تستخدم للتحصيل؟","receipt","الفاتورة سُجلت سابقاً؛ المطلوب تحصيلها.","سند القبض يزيد النقد ويقلل دين العميل دون بيع جديد.",new("receipt","سند قبض من العميل"),new("sale","فاتورة بيع جديدة"),new("capital","تمويل الصندوق")),
                Money("debt","كم يتبقى من دين العميل؟",p.Cash-p.Paid,"الدين السابق − المبلغ المقبوض.",$"يتبقى {M(p.Cash-p.Paid)} وحدة تدريب على العميل."),
                Money("cash","كم يصبح الصندوق؟",10000+p.Paid,"أضف التحصيل إلى رصيد الصندوق السابق.",$"الصندوق يصبح {M(10000+p.Paid)} وحدة تدريب."),
                Money("sales","كم مبيعات جديدة تنشأ من هذا التحصيل؟",0,"البيع قد سُجل وقت الفاتورة؛ القبض ليس بيعاً ثانياً.","التحصيل لا ينشئ إيراد بيع جديداً.")],
            "cash-count" => [
                Money("difference","كم مقدار العجز؟",p.Paid,"الرصيد المتوقع − النقد المعدود.",$"العجز {M(p.Paid)} وحدة تدريب."),
                Choice("effect","ما أثر هذا الفرق؟","expense","النقد أقل من المتوقع، وليس ربحاً أو رأس مال.","العجز الموثّق يسجل مصروف فرق صندوق عند اعتماد المراجعة.",new("expense","عجز يحتاج مصروف فرق صندوق"),new("capital","تمويل رأس مال"),new("sale","بيع جديد")),
                Choice("review","ما الإجراء قبل الاعتماد؟","review","الفرق يحتاج توثيقاً ومراجعة مدير.","تُغلق الوردية للمراجعة مع السبب، ثم يراجعها صاحب الصلاحية ويعتمدها.",new("review","توثيق السبب وإرسال الوردية للمراجعة"),new("ignore","تجاهل الفرق"),new("change","تغيير الفواتير حتى تتطابق الأرقام")),
                Money("cash","ما النقد الفعلي بعد الجرد؟",p.Cash-p.Paid,"استخدم المبلغ المعدود فعلياً.",$"النقد المعدود {M(p.Cash-p.Paid)} وحدة تدريب.")],
            _ => [
                Choice("kind","ما العملية المناسبة لهذا الفرق؟","adjust","لا توجد عملية شراء أو بيع في هذا الجرد.","تسوية المخزون الموثّقة تعالج فرق الجرد بعد المراجعة.",new("adjust","تسوية نقص مخزون مع السبب"),new("purchase","فاتورة شراء وهمية"),new("sale","بيع وهمي")),
                Count("missing","كم حبة ناقصة؟",p.Quantity,"كمية النظام − الكمية المعدودة.",$"نقص الجرد {p.Quantity} حبات."),
                Money("cost","كم قيمة النقص بالتكلفة؟",p.Quantity*p.Cost,"الكمية الناقصة × تكلفة الحبة.",$"قيمة النقص {M(p.Quantity*p.Cost)} وحدة تدريب؛ لا تستخدم سعر البيع."),
                Count("stock","ما الكمية التي تعتمد بعد المراجعة؟",p.Stock-p.Quantity,"اعتمد الكمية المعدودة فعلياً مع توثيق الفرق.",$"الكمية الفعلية {p.Stock-p.Quantity} حبة.")]
        };
    }

    private static Progress Read(SqliteConnection db, string owner, string id, SqliteTransaction? transaction = null)
    {
        using var command=db.CreateCommand();command.CommandText="SELECT State FROM Sessions WHERE Id=$id AND Owner=$owner";
        command.Transaction=transaction;
        command.Parameters.AddWithValue("$id",id);command.Parameters.AddWithValue("$owner",owner);
        var value=command.ExecuteScalar() as string ?? throw new InvalidOperationException("جلسة التدريب غير موجودة لهذا المستخدم.");
        return JsonSerializer.Deserialize<Progress>(value,Json)!;
    }
    private static void Save(SqliteConnection db, string owner, string id, Progress state, bool insert=false, SqliteTransaction? transaction=null)
    {
        var count=Questions(state).Length;var complete=state.Step>=count;
        using var command=db.CreateCommand();
        command.Transaction=transaction;
        command.CommandText=insert?"INSERT INTO Sessions(Id,Owner,Kind,State,Completed,Score,UpdatedAt) VALUES($id,$owner,$kind,$state,$completed,$score,$time)":"UPDATE Sessions SET State=$state,Completed=$completed,Score=$score,UpdatedAt=$time WHERE Id=$id AND Owner=$owner";
        command.Parameters.AddWithValue("$id",id);command.Parameters.AddWithValue("$owner",owner);command.Parameters.AddWithValue("$kind",state.Kind);
        command.Parameters.AddWithValue("$state",JsonSerializer.Serialize(state,Json));command.Parameters.AddWithValue("$completed",complete?1:0);
        command.Parameters.AddWithValue("$score",(int)Math.Round(100m*state.FirstTryCorrect/count));command.Parameters.AddWithValue("$time",DateTimeOffset.UtcNow.ToString("O",CultureInfo.InvariantCulture));
        command.ExecuteNonQuery();
    }
    private static object View(string id, Progress state)
    {
        var scenario=Scenarios.Single(s=>s.Id==state.Kind);var questions=Questions(state);
        var current=state.Step<questions.Length?questions[state.Step]:null;
        return new {id,kind=state.Kind,title=scenario.Title,state.Difficulty,step=state.Step,totalSteps=questions.Length,completed=current is null,
            state.Attempts,state.FirstTryCorrect,score=(int)Math.Round(100m*state.FirstTryCorrect/questions.Length),state.Feedback,state.LastCorrect,
            givens=Givens(state),question=current is null?null:new {current.Id,current.Text,current.Kind,current.Choices,current.Hint}};
    }
    private static int Recommended(SqliteConnection db,string owner,SqliteTransaction? transaction=null)
    {
        using var command=db.CreateCommand();command.CommandText="SELECT COUNT(*),COALESCE(AVG(Score),0) FROM (SELECT Score FROM Sessions WHERE Owner=$owner AND Completed=1 ORDER BY UpdatedAt DESC LIMIT 3)";
        command.Transaction=transaction;
        command.Parameters.AddWithValue("$owner",owner);using var reader=command.ExecuteReader();reader.Read();
        var count=reader.GetInt32(0);var average=reader.GetDouble(1);
        return count>=3 && average>=80?3:count>=1 && average>=80?2:1;
    }
    private static object Catalog(SqliteConnection db,string owner)
    {
        using var command=db.CreateCommand();command.CommandText="SELECT Id,Kind,Completed,Score,UpdatedAt FROM Sessions WHERE Owner=$owner ORDER BY UpdatedAt DESC LIMIT 20";
        command.Parameters.AddWithValue("$owner",owner);using var reader=command.ExecuteReader();var recent=new List<object>();
        while(reader.Read())recent.Add(new {id=reader.GetString(0),kind=reader.GetString(1),completed=reader.GetInt32(2)!=0,score=reader.GetInt32(3),updatedAt=reader.GetString(4)});
        reader.Close();return new {scenarios=Scenarios,recommendedDifficulty=Recommended(db,owner),recent};
    }
}
