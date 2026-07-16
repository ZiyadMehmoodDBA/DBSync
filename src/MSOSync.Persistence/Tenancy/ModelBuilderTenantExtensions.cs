using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using MSOSync.Common.Tenancy;

namespace MSOSync.Persistence.Tenancy;

public static class ModelBuilderTenantExtensions
{
    public static void ApplyTenantFilters(this ModelBuilder modelBuilder, ICurrentTenantAccessor accessor)
    {
        foreach (var entityType in modelBuilder.Model.GetEntityTypes()
            .Where(t => typeof(ITenantScoped).IsAssignableFrom(t.ClrType)))
        {
            modelBuilder.Entity(entityType.ClrType)
                .HasQueryFilter(BuildFilter(entityType.ClrType, accessor));
        }
    }

    // Builds: e => accessor.TenantId == null || e.TenantId == accessor.TenantId.Value
    // EF Core evaluates accessor.TenantId at query time (singleton reads IHttpContextAccessor).
    private static LambdaExpression BuildFilter(Type clrType, ICurrentTenantAccessor accessor)
    {
        var param        = Expression.Parameter(clrType, "e");
        var tenantIdProp = Expression.Property(param, nameof(ITenantScoped.TenantId));

        var accessorExpr     = Expression.Constant(accessor, typeof(ICurrentTenantAccessor));
        var accessorTenantId = Expression.Property(accessorExpr, nameof(ICurrentTenantAccessor.TenantId));

        // accessor.TenantId == null  (platform context or no request)
        var isNull = Expression.Equal(accessorTenantId, Expression.Constant(null, typeof(Guid?)));

        // accessor.TenantId.Value  (unwrap Guid? → Guid)
        var accessorValue = Expression.Property(accessorTenantId, "Value");

        // e.TenantId == accessor.TenantId.Value
        var equals = Expression.Equal(tenantIdProp, accessorValue);

        // accessor.TenantId == null || e.TenantId == accessor.TenantId.Value
        var filter = Expression.OrElse(isNull, equals);

        return Expression.Lambda(filter, param);
    }
}
