// Repro: QueryContext<T>.Sorting silently drops sort fields that are defined via an expression,
// e.g. ISortInputTypeDescriptor<T>.Field(x => x.Price * x.Quantity).Name("total").
// The classic IQueryable sorting middleware handles the same field correctly.
using GreenDonut.Data;
using HotChocolate;
using HotChocolate.Data;
using HotChocolate.Data.Sorting;
using HotChocolate.Execution;
using HotChocolate.Types;
using Microsoft.Extensions.DependencyInjection;

Console.WriteLine($"HotChocolate.Data {typeof(SortingContext).Assembly.GetName().Version}");

var executor = await new ServiceCollection()
    .AddGraphQL()
    .AddQueryType<Query>()
    .AddSorting()
    .BuildRequestExecutorAsync();

string[] orders = [
    "[{ total: DESC }]",                // expression field only
    "[{ total: DESC }, { name: ASC }]", // expression field + property field
    "[{ name: DESC }]",                 // property field only (control)
];

foreach (var order in orders) {
    Console.WriteLine();
    Console.WriteLine($"=== order: {order}");

    var viaQueryable = await executor.ExecuteAsync($"{{ itemsQueryable(order: {order}) {{ name }} }}");
    Console.WriteLine($"IQueryable middleware: {viaQueryable.ToJson(false)}");

    var viaQueryContext = await executor.ExecuteAsync($"{{ itemsQueryContext(order: {order}) {{ name }} }}");
    Console.WriteLine($"QueryContext:          {viaQueryContext.ToJson(false)}");
    Console.WriteLine($"  ISortingContext.GetFields(): {Query.LastSortingFields}");
    Console.WriteLine($"  QueryContext.Sorting:        {Query.LastQueryContextSorting}");
}

public record Item(string Name, decimal Price, decimal Quantity);

public class ItemSortType : SortInputType<Item> {
    protected override void Configure(ISortInputTypeDescriptor<Item> descriptor) {
        descriptor.BindFieldsExplicitly();
        descriptor.Field(i => i.Name);
        // Not a direct member access -> Hot Chocolate creates an expression field (ExpressionSortMetadata)
        descriptor.Field(i => i.Price * i.Quantity).Name("total");
    }
}

public class Query {
    // Chosen so that "unsorted", "sorted by name" and "sorted by total" all differ
    private static readonly Item[] Items = [
        new("B", 1, 1),   // total 1
        new("C", 10, 5),  // total 50
        new("A", 2, 10),  // total 20
    ];

    public static string LastSortingFields = "";
    public static string LastQueryContextSorting = "";

    // Classic path: the sorting middleware applies the sorting to the IQueryable
    [UseSorting(typeof(ItemSortType))]
    public IQueryable<Item> GetItemsQueryable() => Items.AsQueryable();

    // GreenDonut path: the resolver applies the QueryContext itself
    [UseSorting(typeof(ItemSortType))]
    public IEnumerable<Item> GetItemsQueryContext(QueryContext<Item> query, ISortingContext sorting) {
        // Keep the middleware from sorting again, so the result shows only what QueryContext contains
        sorting.Handled(true);

        LastSortingFields = string.Join(", ", sorting.GetFields()
            .SelectMany(f => f)
            .Select(f => f.Field.Name));
        LastQueryContextSorting = query.Sorting is null
            ? "null"
            : "[" + string.Join(", ", query.Sorting.Operations.Select(o => $"{o.KeySelector} {(o.Ascending ? "ASC" : "DESC")}")) + "]";

        return Items.AsQueryable().With(query).ToList();
    }
}
