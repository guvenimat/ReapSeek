using ReapSeek.Domain.Common;

namespace ReapSeek.Domain.Carts;

public sealed record CartLine(long OfferId, int Quantity, Money AddedPrice, DateTime AddedAt);

public sealed class Cart
{
    public const int MaxQuantityPerLine = 10;
    public const int MaxLines = 50;

    readonly List<CartLine> _lines;

    Cart(Guid id, long? customerId, string? guestToken, IEnumerable<CartLine> lines)
    {
        Id = id;
        CustomerId = customerId;
        GuestToken = guestToken;
        _lines = lines.ToList();
    }

    public Guid Id { get; }
    public long? CustomerId { get; }
    public string? GuestToken { get; }
    public IReadOnlyList<CartLine> Lines => _lines;
    public int ItemCount => _lines.Sum(l => l.Quantity);

    public static Cart ForCustomer(Guid id, long customerId) => new(id, customerId, null, []);

    public static Cart ForGuest(Guid id, string token)
    {
        if (string.IsNullOrWhiteSpace(token)) throw new DomainException("cart.token", "A guest cart needs a token");
        return new Cart(id, null, token, []);
    }

    public static Cart Restore(Guid id, long? customerId, string? guestToken, IEnumerable<CartLine> lines) => new(id, customerId, guestToken, lines);

    public CartLine? Find(long offerId) => _lines.FirstOrDefault(l => l.OfferId == offerId);

    public void Add(long offerId, int quantity, Money currentPrice, DateTime now)
    {
        if (quantity < 1) throw new DomainException("cart.quantity", "Quantity must be at least 1");
        var existing = Find(offerId);
        var total = (existing?.Quantity ?? 0) + quantity;
        if (total > MaxQuantityPerLine)
            throw new DomainException("cart.max_quantity", $"You can buy at most {MaxQuantityPerLine} of the same item");
        if (existing is null)
        {
            if (_lines.Count >= MaxLines) throw new DomainException("cart.max_lines", $"A cart can hold at most {MaxLines} different items");
            _lines.Add(new CartLine(offerId, quantity, currentPrice, now));
        }
        else
        {
            Replace(existing, existing with { Quantity = total });
        }
    }

    public void SetQuantity(long offerId, int quantity)
    {
        var existing = Find(offerId) ?? throw new DomainException("cart.line_not_found", "This item is not in the cart");
        if (quantity == 0)
        {
            _lines.Remove(existing);
            return;
        }
        if (quantity is < 0 or > MaxQuantityPerLine)
            throw new DomainException("cart.quantity", $"Quantity must be between 1 and {MaxQuantityPerLine}");
        Replace(existing, existing with { Quantity = quantity });
    }

    public bool Remove(long offerId) => _lines.RemoveAll(l => l.OfferId == offerId) > 0;

    public void MergeFrom(Cart other)
    {
        foreach (var line in other.Lines)
        {
            var existing = Find(line.OfferId);
            if (existing is null)
            {
                if (_lines.Count < MaxLines) _lines.Add(line);
            }
            else
            {
                Replace(existing, existing with { Quantity = Math.Min(MaxQuantityPerLine, existing.Quantity + line.Quantity) });
            }
        }
    }

    public void RemovePurchased(IEnumerable<(long OfferId, int Quantity)> purchased)
    {
        foreach (var (offerId, quantity) in purchased)
        {
            var existing = Find(offerId);
            if (existing is null) continue;
            if (existing.Quantity <= quantity) _lines.Remove(existing);
            else Replace(existing, existing with { Quantity = existing.Quantity - quantity });
        }
    }

    void Replace(CartLine existing, CartLine updated) => _lines[_lines.IndexOf(existing)] = updated;
}
