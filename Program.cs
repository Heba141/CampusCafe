using System.Globalization;
using System.Text;
using System.Text.Json;

var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

// ==========================================
// 1. WEB API & FRONTEND ENDPOINTS
// ==========================================

// Serve the interactive POS Web Interface
app.MapGet("/", () => Results.Content(WebUi.HtmlContent, "text/html"));

// API: Process Order through C# Domain Logic
app.MapPost("/api/order", (OrderSubmissionRequest req) =>
{
    var catalogue = new InMemoryCatalogue();
    
    IPricingPolicy policy = req.Policy switch
    {
        "student" => new StudentDiscount(),
        "employee" => new EmployeeDiscount(),
        _ => new NoDiscount()
    };

    var orderService = new OrderService(catalogue, policy);

    try
    {
        var lines = req.Lines.Select(l => new OrderLineRequest(l.ProductId, l.Quantity)).ToList();
        var summary = orderService.ProcessOrder(lines);

        var detailedFormatter = new DetailedReceiptFormatter();
        var compactFormatter = new CompactReceiptFormatter();

        return Results.Ok(new
        {
            success = true,
            subtotal = summary.Subtotal.ToString("F2", CultureInfo.InvariantCulture),
            discount = summary.DiscountAmount.ToString("F2", CultureInfo.InvariantCulture),
            finalTotal = summary.FinalTotal.ToString("F2", CultureInfo.InvariantCulture),
            totalItems = summary.TotalItemQuantity,
            policyName = summary.PolicyName,
            detailedReceipt = detailedFormatter.Format(summary),
            compactReceipt = compactFormatter.Format(summary)
        });
    }
    catch (Exception ex)
    {
        return Results.BadRequest(new
        {
            success = false,
            errorType = ex.GetType().Name,
            message = ex.Message
        });
    }
});

// API: Run all C# Automated Tests (T01 - T22) from the browser
app.MapGet("/api/run-tests", () =>
{
    var results = TestSuiteRunner.ExecuteAll();
    return Results.Ok(results);
});

app.Run();

// ==========================================
// 2. DTO REQUEST RECORDS
// ==========================================

public record OrderLineDto(string ProductId, int Quantity);
public record OrderSubmissionRequest(string Policy, List<OrderLineDto> Lines);

// ==========================================
// 3. CORE DOMAIN LOGIC (C#)
// ==========================================

public sealed class Product
{
    public string Id { get; }
    public string Name { get; }
    public decimal UnitPrice { get; }

    public Product(string id, string name, decimal unitPrice)
    {
        if (string.IsNullOrWhiteSpace(id))
            throw new ArgumentException("Product ID cannot be null or whitespace.", nameof(id));
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Product name cannot be null or whitespace.", nameof(name));
        if (unitPrice < 0m)
            throw new ArgumentException("Unit price cannot be negative.", nameof(unitPrice));

        Id = id;
        Name = name;
        UnitPrice = unitPrice;
    }
}

public sealed class OrderLineRequest
{
    public string ProductId { get; }
    public int Quantity { get; }

    public OrderLineRequest(string productId, int quantity)
    {
        ProductId = productId;
        Quantity = quantity;
    }
}

public sealed class CalculatedOrderLine
{
    public string ProductId { get; }
    public string ProductName { get; }
    public int Quantity { get; }
    public decimal UnitPrice { get; }
    public decimal LineTotal => UnitPrice * Quantity;

    public CalculatedOrderLine(string productId, string productName, int quantity, decimal unitPrice)
    {
        ProductId = productId;
        ProductName = productName;
        Quantity = quantity;
        UnitPrice = unitPrice;
    }
}

public sealed class OrderSummary
{
    public IReadOnlyList<CalculatedOrderLine> Lines { get; }
    public string PolicyName { get; }
    public decimal Subtotal { get; }
    public decimal DiscountAmount { get; }
    public decimal FinalTotal { get; }
    public int TotalItemQuantity => Lines.Sum(l => l.Quantity);

    public OrderSummary(
        IReadOnlyList<CalculatedOrderLine> lines,
        string policyName,
        decimal subtotal,
        decimal discountAmount,
        decimal finalTotal)
    {
        Lines = lines ?? throw new ArgumentNullException(nameof(lines));
        PolicyName = policyName ?? throw new ArgumentNullException(nameof(policyName));
        Subtotal = subtotal;
        DiscountAmount = discountAmount;
        FinalTotal = finalTotal;
    }
}

public interface IProductCatalogue
{
    Product GetById(string productId);
}

public interface IPricingPolicy
{
    string Name { get; }
    decimal Apply(decimal subtotal);
}

public interface IReceiptFormatter
{
    string Format(OrderSummary summary);
}

public sealed class InMemoryCatalogue : IProductCatalogue
{
    private readonly Dictionary<string, Product> _products = new(StringComparer.Ordinal)
    {
        { "P01", new Product("P01", "Coffee", 2.50m) },
        { "P02", new Product("P02", "Sandwich", 5.00m) },
        { "P03", new Product("P03", "Juice", 3.00m) },
        { "P04", new Product("P04", "Cookie", 1.25m) }
    };

    public Product GetById(string productId)
    {
        if (string.IsNullOrWhiteSpace(productId))
            throw new ArgumentException("Product ID cannot be null, empty, or whitespace.", nameof(productId));

        if (!_products.TryGetValue(productId, out var product))
            throw new ArgumentException($"Product ID '{productId}' was not found in the catalogue.", nameof(productId));

        return product;
    }
}

public sealed class NoDiscount : IPricingPolicy
{
    public string Name => "No Discount";

    public decimal Apply(decimal subtotal)
    {
        if (subtotal < 0m)
            throw new ArgumentException("Subtotal cannot be negative.", nameof(subtotal));

        return subtotal;
    }
}

public sealed class StudentDiscount : IPricingPolicy
{
    public string Name => "Student Discount";

    public decimal Apply(decimal subtotal)
    {
        if (subtotal < 0m)
            throw new ArgumentException("Subtotal cannot be negative.", nameof(subtotal));

        return subtotal * 0.90m;
    }
}

public sealed class EmployeeDiscount : IPricingPolicy
{
    public string Name => "Employee Discount";

    public decimal Apply(decimal subtotal)
    {
        if (subtotal < 0m)
            throw new ArgumentException("Subtotal cannot be negative.", nameof(subtotal));

        return subtotal * 0.80m;
    }
}

public sealed class OrderService
{
    private readonly IProductCatalogue _catalogue;
    private readonly IPricingPolicy _policy;

    public OrderService(IProductCatalogue catalogue, IPricingPolicy policy)
    {
        _catalogue = catalogue ?? throw new ArgumentNullException(nameof(catalogue));
        _policy = policy ?? throw new ArgumentNullException(nameof(policy));
    }

    public OrderSummary ProcessOrder(IReadOnlyList<OrderLineRequest> lines)
    {
        if (lines == null)
            throw new ArgumentNullException(nameof(lines), "Order list cannot be null.");

        if (lines.Count == 0)
            throw new ArgumentException("Order must contain at least 1 line.", nameof(lines));

        if (lines.Count > 4)
            throw new ArgumentException("Order cannot exceed 4 lines.", nameof(lines));

        var calculatedLines = new List<CalculatedOrderLine>(lines.Count);
        var seenProductIds = new HashSet<string>(StringComparer.Ordinal);
        decimal subtotal = 0m;

        foreach (var line in lines)
        {
            if (line == null)
                throw new ArgumentException("Order line cannot be null.", nameof(lines));

            if (string.IsNullOrWhiteSpace(line.ProductId))
                throw new ArgumentException("Product ID cannot be blank.", nameof(lines));

            if (line.Quantity < 1 || line.Quantity > 10)
                throw new ArgumentException($"Quantity for product '{line.ProductId}' must be between 1 and 10.", nameof(lines));

            if (!seenProductIds.Add(line.ProductId))
                throw new ArgumentException($"Duplicate product ID '{line.ProductId}' detected in order.", nameof(lines));

            var product = _catalogue.GetById(line.ProductId);
            var calculatedLine = new CalculatedOrderLine(product.Id, product.Name, line.Quantity, product.UnitPrice);
            calculatedLines.Add(calculatedLine);
            subtotal += calculatedLine.LineTotal;
        }

        decimal unroundedPayable = _policy.Apply(subtotal);

        if (unroundedPayable < 0m || unroundedPayable > subtotal)
        {
            throw new InvalidOperationException(
                $"Policy returned an invalid payable amount: {unroundedPayable} for subtotal: {subtotal}.");
        }

        decimal roundedFinalTotal = Math.Round(unroundedPayable, 2, MidpointRounding.AwayFromZero);
        decimal discountAmount = subtotal - roundedFinalTotal;

        return new OrderSummary(calculatedLines, _policy.Name, subtotal, discountAmount, roundedFinalTotal);
    }
}

public sealed class DetailedReceiptFormatter : IReceiptFormatter
{
    public string Format(OrderSummary summary)
    {
        if (summary == null)
            throw new ArgumentNullException(nameof(summary));

        var culture = CultureInfo.InvariantCulture;
        var sb = new StringBuilder();

        sb.AppendLine("========================================");
        sb.AppendLine("           CAMPUS CAFE RECEIPT          ");
        sb.AppendLine("========================================");

        foreach (var line in summary.Lines)
        {
            sb.AppendLine($"{line.ProductId} {line.ProductName}");
            sb.AppendLine($"  {line.Quantity} x {line.UnitPrice.ToString("F2", culture)} = {line.LineTotal.ToString("F2", culture)}");
        }

        sb.AppendLine("----------------------------------------");
        sb.AppendLine($"Policy:          {summary.PolicyName}");
        sb.AppendLine($"Subtotal:        {summary.Subtotal.ToString("F2", culture)}");
        sb.AppendLine($"Discount:        {summary.DiscountAmount.ToString("F2", culture)}");
        sb.AppendLine($"Final Total:     {summary.FinalTotal.ToString("F2", culture)}");
        sb.AppendLine("========================================");

        return sb.ToString();
    }
}

public sealed class CompactReceiptFormatter : IReceiptFormatter
{
    public string Format(OrderSummary summary)
    {
        if (summary == null)
            throw new ArgumentNullException(nameof(summary));

        var culture = CultureInfo.InvariantCulture;
        var sb = new StringBuilder();

        sb.AppendLine("========================================");
        sb.AppendLine("      CAMPUS CAFE COMPACT RECEIPT       ");
        sb.AppendLine("========================================");
        sb.AppendLine($"Policy:          {summary.PolicyName}");
        sb.AppendLine($"Total Items:     {summary.TotalItemQuantity}");
        sb.AppendLine($"Subtotal:        {summary.Subtotal.ToString("F2", culture)}");
        sb.AppendLine($"Discount:        {summary.DiscountAmount.ToString("F2", culture)}");
        sb.AppendLine($"Final Total:     {summary.FinalTotal.ToString("F2", culture)}");
        sb.AppendLine("========================================");

        return sb.ToString();
    }
}

// ==========================================
// 4. TEST HARNESS EXECUTOR
// ==========================================

public static class TestSuiteRunner
{
    public record TestCaseResult(string Id, string Scenario, bool Passed, string Details);

    public static List<TestCaseResult> ExecuteAll()
    {
        var list = new List<TestCaseResult>();
        var catalogue = new InMemoryCatalogue();
        var regularService = new OrderService(catalogue, new NoDiscount());
        var studentService = new OrderService(catalogue, new StudentDiscount());
        var employeeService = new OrderService(catalogue, new EmployeeDiscount());
        var detailedFormatter = new DetailedReceiptFormatter();
        var compactFormatter = new CompactReceiptFormatter();

        var workedOrder = new List<OrderLineRequest> { new("P02", 2), new("P01", 1) };

        void AddTest(string id, string desc, Func<bool> test)
        {
            try
            {
                bool ok = test();
                list.Add(new TestCaseResult(id, desc, ok, ok ? "Condition verified." : "Assertion failed."));
            }
            catch (Exception ex)
            {
                list.Add(new TestCaseResult(id, desc, false, $"{ex.GetType().Name}: {ex.Message}"));
            }
        }

        void AddExceptionTest<T>(string id, string desc, Action act) where T : Exception
        {
            try
            {
                act();
                list.Add(new TestCaseResult(id, desc, false, $"Failed: Expected {typeof(T).Name} was not thrown."));
            }
            catch (T)
            {
                list.Add(new TestCaseResult(id, desc, true, $"Threw expected {typeof(T).Name}."));
            }
            catch (Exception ex)
            {
                list.Add(new TestCaseResult(id, desc, false, $"Failed: Threw {ex.GetType().Name} instead of {typeof(T).Name}."));
            }
        }

        AddTest("T01", "Two P02 and one P01; NoDiscount", () =>
        {
            var s = regularService.ProcessOrder(workedOrder);
            return s.Subtotal == 12.50m && s.DiscountAmount == 0.00m && s.FinalTotal == 12.50m;
        });

        AddTest("T02", "Same order; StudentDiscount", () =>
        {
            var s = studentService.ProcessOrder(workedOrder);
            return s.Subtotal == 12.50m && s.DiscountAmount == 1.25m && s.FinalTotal == 11.25m;
        });

        AddTest("T03", "One P04; StudentDiscount rounding", () =>
        {
            var s = studentService.ProcessOrder(new List<OrderLineRequest> { new("P04", 1) });
            return s.Subtotal == 1.25m && s.FinalTotal == 1.13m && s.DiscountAmount == 0.12m;
        });

        AddExceptionTest<ArgumentException>("T04", "Empty order", () => regularService.ProcessOrder(new List<OrderLineRequest>()));
        AddExceptionTest<ArgumentException>("T05", "Unknown ID P99", () => regularService.ProcessOrder(new List<OrderLineRequest> { new("P99", 1) }));
        AddExceptionTest<ArgumentException>("T06", "Quantity 0", () => regularService.ProcessOrder(new List<OrderLineRequest> { new("P01", 0) }));
        AddExceptionTest<ArgumentException>("T07", "Quantity -1", () => regularService.ProcessOrder(new List<OrderLineRequest> { new("P01", -1) }));
        AddExceptionTest<ArgumentException>("T08", "Quantity 11", () => regularService.ProcessOrder(new List<OrderLineRequest> { new("P01", 11) }));
        AddExceptionTest<ArgumentException>("T09", "Duplicate product lines", () => regularService.ProcessOrder(new List<OrderLineRequest> { new("P01", 1), new("P01", 2) }));
        AddExceptionTest<ArgumentException>("T10", "Blank product ID", () => regularService.ProcessOrder(new List<OrderLineRequest> { new("   ", 1) }));
        AddExceptionTest<ArgumentException>("T11", "Null order list", () => regularService.ProcessOrder(null!));
        AddExceptionTest<ArgumentException>("T12a", "Negative subtotal to NoDiscount", () => new NoDiscount().Apply(-1m));
        AddTest("T12b", "Zero subtotal to NoDiscount returns 0m", () => new NoDiscount().Apply(0m) == 0m);
        AddExceptionTest<ArgumentException>("T12c", "Negative subtotal to StudentDiscount", () => new StudentDiscount().Apply(-1m));
        AddTest("T12d", "Zero subtotal to StudentDiscount returns 0m", () => new StudentDiscount().Apply(0m) == 0m);
        AddExceptionTest<ArgumentException>("T12e", "Negative subtotal to EmployeeDiscount", () => new EmployeeDiscount().Apply(-1m));
        AddTest("T12f", "Zero subtotal to EmployeeDiscount returns 0m", () => new EmployeeDiscount().Apply(0m) == 0m);

        AddTest("T17", "Quantity 10 accepted", () =>
        {
            var s = regularService.ProcessOrder(new List<OrderLineRequest> { new("P01", 10) });
            return s.Subtotal == 25.00m && s.FinalTotal == 25.00m;
        });

        AddTest("T18", "Two P02 and one P01; EmployeeDiscount", () =>
        {
            var s = employeeService.ProcessOrder(workedOrder);
            return s.Subtotal == 12.50m && s.DiscountAmount == 2.50m && s.FinalTotal == 10.00m;
        });

        AddTest("T19", "Detailed receipt verification", () =>
        {
            var s = studentService.ProcessOrder(workedOrder);
            var text = detailedFormatter.Format(s);
            return text.Contains("P02 Sandwich") && text.Contains("Final Total:     11.25");
        });

        AddTest("T20", "Compact receipt verification", () =>
        {
            var s = employeeService.ProcessOrder(workedOrder);
            var text = compactFormatter.Format(s);
            return text.Contains("Total Items:     3") && text.Contains("Final Total:     10.00");
        });

        AddTest("T21", "Direct Employee policy check (20%)", () => new EmployeeDiscount().Apply(50m) == 40m);
        AddExceptionTest<ArgumentException>("T22", "Case sensitive catalogue lookup", () => catalogue.GetById("p01"));

        return list;
    }
}

// ==========================================
// 5. EMBEDDED WEB INTERFACE (HTML/JS)
// ==========================================

public static class WebUi
{
    public const string HtmlContent = """
    <!DOCTYPE html>
    <html lang="en">
    <head>
        <meta charset="UTF-8">
        <meta name="viewport" content="width=device-width, initial-scale=1.0">
        <title>Campus Café POS - C# Backend</title>
        <script src="https://cdn.tailwindcss.com"></script>
        <link href="https://fonts.googleapis.com/css2?family=JetBrains+Mono:wght@400;600&family=Plus+Jakarta+Sans:wght@500;600;700&display=swap" rel="stylesheet">
        <style>
            body { font-family: 'Plus Jakarta Sans', sans-serif; }
            .font-mono { font-family: 'JetBrains Mono', monospace; }
        </style>
    </head>
    <body class="bg-slate-900 text-slate-100 min-h-screen">
        <header class="border-b border-slate-800 bg-slate-950/80 backdrop-blur sticky top-0 z-50">
            <div class="max-w-7xl mx-auto px-6 h-16 flex items-center justify-between">
                <div class="flex items-center gap-3">
                    <span class="text-2xl">☕</span>
                    <div>
                        <h1 class="font-bold text-lg text-white">Campus Café POS</h1>
                        <p class="text-xs text-emerald-400 font-medium">● Powered directly by ASP.NET Core & C#</p>
                    </div>
                </div>
                <button onclick="runCSharpTests()" class="bg-indigo-600 hover:bg-indigo-500 text-white text-xs font-semibold px-4 py-2 rounded-lg transition shadow-lg shadow-indigo-600/30 flex items-center gap-2">
                    <span>⚡ Run C# Automated Tests</span>
                </button>
            </div>
        </header>

        <main class="max-w-7xl mx-auto px-6 py-8 grid grid-cols-1 lg:grid-cols-12 gap-8">
            <!-- Left: Menu & Order Builder -->
            <div class="lg:col-span-7 space-y-6">
                <!-- Products -->
                <div>
                    <h2 class="text-sm font-semibold uppercase tracking-wider text-slate-400 mb-3">Catalogue (P01 - P04)</h2>
                    <div class="grid grid-cols-2 gap-3">
                        <button onclick="addProduct('P01', 'Coffee', 2.50)" class="p-4 rounded-xl bg-slate-800/80 hover:bg-slate-700/80 border border-slate-700 text-left transition flex justify-between items-center group">
                            <div>
                                <div class="font-bold text-white group-hover:text-amber-400">P01 Coffee</div>
                                <div class="text-xs text-slate-400">$2.50</div>
                            </div>
                            <span class="text-xs bg-slate-700 group-hover:bg-amber-500 group-hover:text-slate-950 text-slate-300 px-2 py-1 rounded font-bold">+ Add</span>
                        </button>
                        <button onclick="addProduct('P02', 'Sandwich', 5.00)" class="p-4 rounded-xl bg-slate-800/80 hover:bg-slate-700/80 border border-slate-700 text-left transition flex justify-between items-center group">
                            <div>
                                <div class="font-bold text-white group-hover:text-amber-400">P02 Sandwich</div>
                                <div class="text-xs text-slate-400">$5.00</div>
                            </div>
                            <span class="text-xs bg-slate-700 group-hover:bg-amber-500 group-hover:text-slate-950 text-slate-300 px-2 py-1 rounded font-bold">+ Add</span>
                        </button>
                        <button onclick="addProduct('P03', 'Juice', 3.00)" class="p-4 rounded-xl bg-slate-800/80 hover:bg-slate-700/80 border border-slate-700 text-left transition flex justify-between items-center group">
                            <div>
                                <div class="font-bold text-white group-hover:text-amber-400">P03 Juice</div>
                                <div class="text-xs text-slate-400">$3.00</div>
                            </div>
                            <span class="text-xs bg-slate-700 group-hover:bg-amber-500 group-hover:text-slate-950 text-slate-300 px-2 py-1 rounded font-bold">+ Add</span>
                        </button>
                        <button onclick="addProduct('P04', 'Cookie', 1.25)" class="p-4 rounded-xl bg-slate-800/80 hover:bg-slate-700/80 border border-slate-700 text-left transition flex justify-between items-center group">
                            <div>
                                <div class="font-bold text-white group-hover:text-amber-400">P04 Cookie</div>
                                <div class="text-xs text-slate-400">$1.25</div>
                            </div>
                            <span class="text-xs bg-slate-700 group-hover:bg-amber-500 group-hover:text-slate-950 text-slate-300 px-2 py-1 rounded font-bold">+ Add</span>
                        </button>
                    </div>
                </div>

                <!-- Current Lines -->
                <div class="bg-slate-800/50 rounded-2xl p-5 border border-slate-700/80">
                    <div class="flex justify-between items-center mb-4">
                        <h2 class="text-sm font-semibold uppercase tracking-wider text-slate-400">Order Lines (<span id="lineCount">0</span>/4)</h2>
                        <button onclick="loadWorkedExample()" class="text-xs text-amber-400 hover:underline">Load Worked Example (2x P02, 1x P01)</button>
                    </div>

                    <div id="linesList" class="space-y-2 mb-4">
                        <div class="text-center py-8 text-slate-500 text-sm">Order is empty. Click a product above to add.</div>
                    </div>

                    <!-- Policy Selection -->
                    <div class="pt-4 border-t border-slate-700">
                        <label class="block text-xs uppercase tracking-wider font-semibold text-slate-400 mb-2">Discount Policy</label>
                        <div class="grid grid-cols-3 gap-2">
                            <label class="flex items-center gap-2 p-2.5 rounded-lg bg-slate-900 border border-slate-700 cursor-pointer text-xs">
                                <input type="radio" name="policy" value="none" checked class="text-amber-500">
                                <span>No Discount</span>
                            </label>
                            <label class="flex items-center gap-2 p-2.5 rounded-lg bg-slate-900 border border-slate-700 cursor-pointer text-xs">
                                <input type="radio" name="policy" value="student" class="text-amber-500">
                                <span>Student (10%)</span>
                            </label>
                            <label class="flex items-center gap-2 p-2.5 rounded-lg bg-slate-900 border border-slate-700 cursor-pointer text-xs">
                                <input type="radio" name="policy" value="employee" class="text-amber-500">
                                <span>Employee (20%)</span>
                            </label>
                        </div>
                    </div>

                    <!-- Submit Buttons -->
                    <div class="grid grid-cols-2 gap-3 mt-5">
                        <button onclick="submitOrder('detailed')" class="bg-amber-500 hover:bg-amber-400 text-slate-950 font-bold py-3 rounded-xl transition text-sm">
                            Generate Detailed Receipt
                        </button>
                        <button onclick="submitOrder('compact')" class="bg-slate-700 hover:bg-slate-600 text-white font-bold py-3 rounded-xl transition text-sm">
                            Generate Compact Receipt
                        </button>
                    </div>
                </div>
            </div>

            <!-- Right: Receipt Output & Live Results -->
            <div class="lg:col-span-5 space-y-6">
                <!-- Receipt Card -->
                <div class="bg-slate-950 rounded-2xl p-6 border border-slate-800 shadow-2xl relative">
                    <div class="flex justify-between items-center mb-3">
                        <h3 class="text-xs uppercase tracking-wider font-bold text-slate-400">Server C# Receipt Output</h3>
                        <span id="receiptBadge" class="text-xs font-mono bg-slate-800 text-slate-400 px-2 py-0.5 rounded">Ready</span>
                    </div>
                    <pre id="receiptBox" class="font-mono text-xs bg-slate-900 text-emerald-400 p-4 rounded-xl border border-slate-800 overflow-x-auto min-h-[300px] leading-relaxed">Ready to process orders.</pre>
                </div>

                <!-- Test Results Modal / Panel -->
                <div id="testPanel" class="bg-slate-950 rounded-2xl p-6 border border-slate-800 hidden">
                    <div class="flex justify-between items-center mb-4">
                        <h3 class="text-sm font-bold text-white flex items-center gap-2">
                            <span>🧪 C# Test Suite Results</span>
                            <span id="testSummaryBadge" class="text-xs bg-indigo-500/20 text-indigo-400 px-2 py-0.5 rounded-full font-mono"></span>
                        </h3>
                        <button onclick="document.getElementById('testPanel').classList.add('hidden')" class="text-slate-500 hover:text-white text-xs">✕ Close</button>
                    </div>
                    <div id="testResultsList" class="space-y-2 max-h-72 overflow-y-auto text-xs font-mono"></div>
                </div>
            </div>
        </main>

        <script>
            let orderLines = [];

            function addProduct(id, name, price) {
                const existing = orderLines.find(x => x.productId === id);
                if (existing) {
                    if (existing.quantity >= 10) {
                        alert('Maximum quantity per item is 10.');
                        return;
                    }
                    existing.quantity++;
                } else {
                    if (orderLines.length >= 4) {
                        alert('Order cannot exceed 4 distinct lines.');
                        return;
                    }
                    orderLines.push({ productId: id, name, price, quantity: 1 });
                }
                renderLines();
            }

            function updateQty(id, delta) {
                const line = orderLines.find(x => x.productId === id);
                if (!line) return;
                line.quantity += delta;
                if (line.quantity <= 0) {
                    orderLines = orderLines.filter(x => x.productId !== id);
                } else if (line.quantity > 10) {
                    line.quantity = 10;
                    alert('Maximum quantity is 10.');
                }
                renderLines();
            }

            function removeLine(id) {
                orderLines = orderLines.filter(x => x.productId !== id);
                renderLines();
            }

            function loadWorkedExample() {
                orderLines = [
                    { productId: 'P02', name: 'Sandwich', price: 5.00, quantity: 2 },
                    { productId: 'P01', name: 'Coffee', price: 2.50, quantity: 1 }
                ];
                renderLines();
            }

            function renderLines() {
                const list = document.getElementById('linesList');
                document.getElementById('lineCount').innerText = orderLines.length;

                if (orderLines.length === 0) {
                    list.innerHTML = '<div class="text-center py-8 text-slate-500 text-sm">Order is empty. Click a product above to add.</div>';
                    return;
                }

                list.innerHTML = orderLines.map(l => `
                    <div class="flex items-center justify-between p-3 rounded-xl bg-slate-900/90 border border-slate-700/60">
                        <div>
                            <span class="font-bold text-white text-sm">${l.productId} ${l.name}</span>
                            <span class="text-xs text-slate-400 ml-2">($${l.price.toFixed(2)} each)</span>
                        </div>
                        <div class="flex items-center gap-3">
                            <div class="flex items-center bg-slate-800 rounded-lg p-1 border border-slate-700">
                                <button onclick="updateQty('${l.productId}', -1)" class="w-6 h-6 flex items-center justify-center text-slate-400 hover:text-white font-bold">-</button>
                                <span class="w-8 text-center text-xs font-bold text-amber-400">${l.quantity}</span>
                                <button onclick="updateQty('${l.productId}', 1)" class="w-6 h-6 flex items-center justify-center text-slate-400 hover:text-white font-bold">+</button>
                            </div>
                            <span class="font-mono text-xs font-bold text-white w-14 text-right">$${(l.price * l.quantity).toFixed(2)}</span>
                            <button onclick="removeLine('${l.productId}')" class="text-rose-400 hover:text-rose-300 text-xs ml-1">✕</button>
                        </div>
                    </div>
                `).join('');
            }

            async function submitOrder(type) {
                if (orderLines.length === 0) {
                    alert('Please add at least 1 item to the order.');
                    return;
                }

                const policy = document.querySelector('input[name="policy"]:checked').value;
                const box = document.getElementById('receiptBox');
                const badge = document.getElementById('receiptBadge');

                try {
                    const res = await fetch('/api/order', {
                        method: 'POST',
                        headers: { 'Content-Type': 'application/json' },
                        body: JSON.stringify({
                            policy: policy,
                            lines: orderLines.map(l => ({ productId: l.productId, quantity: l.quantity }))
                        })
                    });

                    const data = await res.json();
                    if (data.success) {
                        box.innerText = type === 'detailed' ? data.detailedReceipt : data.compactReceipt;
                        badge.innerText = 'C# Response 200 OK';
                        badge.className = 'text-xs font-mono bg-emerald-500/20 text-emerald-400 px-2 py-0.5 rounded';
                    } else {
                        box.innerText = `[${data.errorType}] ${data.message}`;
                        badge.innerText = 'Server Rejected Order';
                        badge.className = 'text-xs font-mono bg-rose-500/20 text-rose-400 px-2 py-0.5 rounded';
                    }
                } catch (err) {
                    box.innerText = 'Error calling C# backend: ' + err.message;
                }
            }

            async function runCSharpTests() {
                const panel = document.getElementById('testPanel');
                const list = document.getElementById('testResultsList');
                const badge = document.getElementById('testSummaryBadge');

                panel.classList.remove('hidden');
                list.innerHTML = '<div class="text-slate-400">Executing C# test suite...</div>';

                try {
                    const res = await fetch('/api/run-tests');
                    const tests = await res.json();
                    const passed = tests.filter(t => t.passed).length;

                    badge.innerText = `${passed}/${tests.length} Passed`;
                    list.innerHTML = tests.map(t => `
                        <div class="flex items-center justify-between p-2 rounded ${t.passed ? 'bg-emerald-950/40 text-emerald-300 border border-emerald-900/40' : 'bg-rose-950/40 text-rose-300 border border-rose-900/40'}">
                            <span><strong>[${t.id}]</strong> ${t.scenario}</span>
                            <span class="font-bold">${t.passed ? 'PASS' : 'FAIL'}</span>
                        </div>
                    `).join('');
                } catch (err) {
                    list.innerHTML = '<div class="text-rose-400">Failed to run tests: ' + err.message + '</div>';
                }
            }
        </script>
    </body>
    </html>
    """;
}