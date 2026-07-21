using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using MSOSync.Common.Tenancy;

namespace MSOSync.Persistence.Tenancy;

public static class ModelBuilderTenantExtensions
{
    public static void ApplyTenantFilters(this ModelBuilder modelBuilder, AppDbContext context)
    {
        foreach (var entityType in modelBuilder.Model.GetEntityTypes()
            .Where(t => typeof(ITenantScoped).IsAssignableFrom(t.ClrType)))
        {
            modelBuilder.Entity(entityType.ClrType)
                .HasQueryFilter(BuildFilter(entityType.ClrType, context));
        }
    }

    // Builds: e => context.CurrentTenantId == null || (Guid?)e.TenantId == context.CurrentTenantId
    // The context reference is rewritten by EF per query to the executing context instance,
    // so the model cache (keyed on context type) stays correct across instances (2A-015).
    // Lifted nullable comparison — no .Value: EF's parameter extraction evaluates
    // client subexpressions eagerly and .Value throws when CurrentTenantId is null.
    private static LambdaExpression BuildFilter(Type clrType, AppDbContext context)
    {
        var param        = Expression.Parameter(clrType, "e");
        var tenantIdProp = Expression.Property(param, nameof(ITenantScoped.TenantId));

        var contextExpr     = Expression.Constant(context, typeof(AppDbContext));
        var currentTenantId = Expression.Property(contextExpr, nameof(AppDbContext.CurrentTenantId));

        // context.CurrentTenantId == null  (platform context or no request)
        var isNull = Expression.Equal(currentTenantId, Expression.Constant(null, typeof(Guid?)));

        // (Guid?)e.TenantId == context.CurrentTenantId
        var equals = Expression.Equal(
            Expression.Convert(tenantIdProp, typeof(Guid?)), currentTenantId);

        var filter = Expression.OrElse(isNull, equals);

        return Expression.Lambda(filter, param);
    }
}
