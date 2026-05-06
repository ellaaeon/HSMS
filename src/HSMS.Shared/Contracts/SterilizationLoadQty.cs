namespace HSMS.Shared.Contracts;

/// <summary>Derives <c>load_qty</c> from register-load line items (same total as item Qty on the form).</summary>
public static class SterilizationLoadQty
{
    public static int? FromItems(IEnumerable<SterilizationItemDto>? items)
    {
        if (items is null)
        {
            return null;
        }

        var sum = 0;
        var any = false;
        foreach (var i in items)
        {
            any = true;
            sum += Math.Max(1, i.Qty);
        }

        return any ? sum : null;
    }
}
