Group ID: [e.g., G01]
Representative: Alaa Sabateen (Student ID: [Your ID])

Team Members & Roles:
1. Alaa Sabateen (ID: [Your ID]) - OrderService, Invariant Guards & Validation (Reviewer: Member 2)
2. [Student 2 Name] (ID: [ID]) - Catalogue & Domain Models (Reviewer: Member 3)
3. [Student 3 Name] (ID: [ID]) - Pricing Policies & OrderSummary Contract (Reviewer: Member 4)
4. [Student 4 Name] (ID: [ID]) - Receipt Formatter & Automated Test Harness (Reviewer: Member 1)

Compiler Option:
- .NET 8.0 SDK (C# 12) Console Application
- Also verified on https://dotnetfiddle.net/ using standard C# .NET 8 Console runtime.

How to Run:
1. Using .NET CLI:
   dotnet run
2. Using DotNetFiddle:
   - Navigate to https://dotnetfiddle.net/
   - Select Language: C#, Project Type: Console, Compiler: .NET 8
   - Paste Program.cs and click 'Run'.

Completed Requirements (Checkpoint 1):
- Fixed catalogue with P01-P04 loaded in InMemoryCatalogue.
- Core behavioral interfaces: IProductCatalogue, IPricingPolicy, IReceiptFormatter.
- OrderService implementing constructor-based dependency injection.
- NoDiscount pricing policy implementation.
- DetailedReceiptFormatter rendering itemized lines and InvariantCulture currency.
- Automated assertion test suite executing T01, T04, and T06.

Known Omissions:
- StudentDiscount and EmployeeDiscount policies (deferred to Checkpoint 2 and Checkpoint 3).
- CompactReceiptFormatter (deferred to Checkpoint 3).
- Test cases T02, T03, T05, T07–T20 (deferred to Checkpoint 2 and Checkpoint 3).