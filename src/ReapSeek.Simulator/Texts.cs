namespace ReapSeek.Simulator;

public static class Names
{
    public static readonly string[] First =
    [
        "Ada", "Ayse", "Elif", "Zeynep", "Defne", "Ece", "Selin", "Deniz", "Merve", "Irem", "Nil", "Derya", "Ceren", "Asli", "Gizem", "Buse", "Melis", "Sena",
        "Can", "Emre", "Mert", "Burak", "Kerem", "Arda", "Ege", "Kaan", "Ozan", "Baris", "Onur", "Tolga", "Umut", "Cem", "Efe", "Alp", "Bora", "Sarp",
    ];

    public static readonly string[] Last =
    [
        "Yilmaz", "Kaya", "Demir", "Sahin", "Celik", "Yildiz", "Aydin", "Ozturk", "Arslan", "Dogan", "Kilic", "Aslan", "Cetin", "Kara", "Koc", "Kurt",
        "Ozkan", "Simsek", "Polat", "Korkmaz", "Erdem", "Aksoy", "Tekin", "Bulut", "Gunes", "Turan", "Keskin", "Uysal", "Acar", "Er",
    ];
}

public static class Texts
{
    public static readonly (string Title, string Body)[][] Reviews =
    [
        [("Disappointed", "The quality is far below what the photos suggest. I sent it back."), ("Not worth it", "Started falling apart after a week of use.")],
        [("Could be better", "It does the job, but the finish feels cheap for the price."), ("Meh", "Smaller than I expected and the color is a little off.")],
        [("Okay for the price", "Nothing special, but it arrived quickly and matches the description."), ("Average", "Decent quality. I would buy it again only on sale.")],
        [("Happy with it", "Good quality and it arrived well packed. Fits as described."), ("Nice purchase", "Looks great and feels sturdy. Delivery was quick.")],
        [("Love it", "Excellent quality, exactly as pictured. The seller shipped the next day."), ("Perfect", "Better than I expected. I already ordered a second one."),
         ("Highly recommend", "Great value for money and very well made.")],
    ];

    public static readonly (string Question, string Answer)[] Questions =
    [
        ("Is this true to size?", "Yes, it runs true to size. If you are between sizes, we suggest the larger one."),
        ("How long does delivery to Izmir take?", "We ship within one business day; delivery usually takes two to three days."),
        ("Is the color the same as in the photos?", "Yes, the photos are taken in daylight and match the product closely."),
        ("Does it come with a warranty?", "Yes, it has a two-year warranty from the manufacturer."),
        ("Can I return it if it does not fit?", "Of course, you can return it within 7 days of delivery from your order page."),
        ("Is it suitable as a gift?", "Yes, it arrives in its original box and makes a nice gift."),
    ];

    public static (string Title, string Body) Review(int rating, int variant)
    {
        var options = Reviews[rating - 1];
        return options[variant % options.Length];
    }
}
