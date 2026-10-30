namespace ReapSeek.Api.Seed;

public enum VariantStyle { Apparel, Shoes, Phone, Laptop, ColorOnly, Single }

public sealed record LeafSpec(
    string Slug, string Name, string Kind, decimal MinPrice, decimal MaxPrice, VariantStyle Style,
    string[] Nouns, string[] Adjectives, string[] Materials);

public sealed record DepartmentSpec(string Slug, string Name, string Kind, string[] Brands, string[] Colors, LeafSpec[] Leaves);

public sealed record SellerSpec(string Name, string City, decimal Rating, string[] Departments);

public static class SeedData
{
    static readonly string[] ApparelColors = ["Black", "White", "Navy", "Beige", "Grey", "Red", "Green", "Pink", "Brown", "Blue"];
    static readonly string[] TechColors = ["Black", "Silver", "White", "Blue", "Gold", "Purple"];
    static readonly string[] HomeColors = ["White", "Grey", "Beige", "Navy", "Green", "Brown", "Black"];
    static readonly string[] BrightColors = ["Red", "Blue", "Yellow", "Green", "Pink", "Orange", "Purple"];

    public static readonly SellerSpec[] Sellers =
    [
        new("Aegean Threads", "Izmir", 4.7m, ["women", "men", "kids"]),
        new("Bosphorus Tech", "Istanbul", 4.6m, ["electronics"]),
        new("Taurus Outdoor", "Antalya", 4.4m, ["sports", "men"]),
        new("Lykia Home", "Mugla", 4.5m, ["home"]),
        new("Anka Books", "Ankara", 4.8m, ["books", "kids"]),
        new("Cedar & Co", "Bursa", 4.2m, ["home", "women"]),
        new("Nova Beauty Lab", "Istanbul", 4.6m, ["beauty"]),
        new("Pebble Kids", "Eskisehir", 4.3m, ["kids", "books"]),
        new("Urban Stride", "Istanbul", 4.1m, ["men", "women", "sports"]),
        new("Golden Horn Market", "Istanbul", 4.5m, ["supermarket", "home"]),
        new("Kestrel Sports", "Izmir", 3.9m, ["sports"]),
        new("Marmara Electronics", "Kocaeli", 4.0m, ["electronics", "home"]),
        new("Saffron Kitchen", "Gaziantep", 4.7m, ["supermarket", "home"]),
        new("Ivory Lane", "Ankara", 3.7m, ["women", "beauty", "men"]),
    ];

    public static readonly DepartmentSpec[] Departments =
    [
        new("women", "Women", "dress", ["Velora", "Mira Lane", "Solenne", "Atelier Nord", "Coralie", "Wren & Sage"], ApparelColors,
        [
            new("dresses", "Dresses", "dress", 349, 1899, VariantStyle.Apparel, ["Midi Dress", "Maxi Dress", "Wrap Dress", "Shirt Dress", "Slip Dress", "Knit Dress"], ["Floral", "Pleated", "Belted", "Ruffled", "Tiered", "Classic"], ["Linen", "Cotton", "Satin", "Viscose", "Crepe"]),
            new("womens-tops", "Tops & Blouses", "shirt", 199, 899, VariantStyle.Apparel, ["Blouse", "Crop Top", "Tunic", "Tank Top", "Bodysuit"], ["Relaxed", "Puff Sleeve", "Square Neck", "Embroidered", "Basic"], ["Cotton", "Linen", "Modal", "Chiffon"]),
            new("womens-jeans", "Jeans & Trousers", "pants", 399, 1499, VariantStyle.Apparel, ["Mom Jeans", "Wide Leg Jeans", "Straight Trousers", "Cargo Pants", "Palazzo Trousers"], ["High Rise", "Washed", "Tailored", "Stretch", "Vintage"], ["Denim", "Twill", "Linen Blend"]),
            new("womens-shoes", "Women's Shoes", "shoe", 499, 2499, VariantStyle.Shoes, ["Ankle Boots", "Loafers", "Ballet Flats", "Block Heels", "Platform Sneakers"], ["Suede", "Patent", "Comfort", "Pointed", "Everyday"], ["Leather", "Faux Leather", "Canvas"]),
            new("handbags", "Bags", "bag", 399, 3499, VariantStyle.ColorOnly, ["Tote Bag", "Crossbody Bag", "Shoulder Bag", "Backpack", "Clutch"], ["Quilted", "Structured", "Mini", "Woven", "Classic"], ["Leather", "Faux Leather", "Canvas", "Nylon"]),
        ]),
        new("men", "Men", "shirt", ["Graymark", "Northpeak", "Oakridge", "Ferro", "Calder", "Holt & Finch"], ApparelColors,
        [
            new("mens-tshirts", "T-Shirts", "shirt", 149, 599, VariantStyle.Apparel, ["Crew Neck T-Shirt", "V-Neck T-Shirt", "Oversized T-Shirt", "Polo Shirt", "Henley"], ["Essential", "Heavyweight", "Slim Fit", "Graphic", "Pocket"], ["Cotton", "Organic Cotton", "Pique"]),
            new("mens-shirts", "Shirts", "shirt", 299, 1199, VariantStyle.Apparel, ["Oxford Shirt", "Linen Shirt", "Flannel Shirt", "Denim Shirt", "Overshirt"], ["Regular Fit", "Slim Fit", "Button-Down", "Striped", "Washed"], ["Cotton", "Linen", "Flannel", "Denim"]),
            new("mens-jeans", "Jeans & Chinos", "pants", 449, 1599, VariantStyle.Apparel, ["Slim Jeans", "Straight Jeans", "Chino Trousers", "Jogger Pants", "Cargo Trousers"], ["Stretch", "Raw", "Tapered", "Relaxed", "Classic"], ["Denim", "Twill", "Cotton"]),
            new("sneakers", "Sneakers", "shoe", 799, 3999, VariantStyle.Shoes, ["Low-Top Sneakers", "Running Sneakers", "High-Top Sneakers", "Slip-On Sneakers", "Court Sneakers"], ["Retro", "Lightweight", "Cushioned", "Minimal", "Everyday"], ["Leather", "Mesh", "Suede", "Canvas"]),
            new("watches", "Watches", "watch", 899, 12999, VariantStyle.ColorOnly, ["Chronograph Watch", "Field Watch", "Dress Watch", "Diver Watch", "Automatic Watch"], ["Steel", "Minimal", "Sport", "Classic", "Sapphire"], ["Stainless Steel", "Leather Strap", "Titanium"]),
        ]),
        new("kids", "Mother & Kids", "toy", ["Tiny Tide", "Pipsqueak", "Little Lark", "Bramble", "Puddle & Co"], BrightColors,
        [
            new("baby-clothing", "Baby Clothing", "shirt", 99, 499, VariantStyle.Apparel, ["Bodysuit Set", "Romper", "Pajama Set", "Hooded Jacket", "Cardigan"], ["Soft", "Organic", "Printed", "Cosy", "Snap"], ["Cotton", "Organic Cotton", "Muslin"]),
            new("toys", "Toys", "toy", 149, 1999, VariantStyle.Single, ["Building Blocks", "Puzzle", "Stacking Rings", "Wooden Train", "Plush Bear", "Shape Sorter"], ["Colourful", "Educational", "Classic", "Magnetic", "Giant", "Mini"], [""]),
            new("kids-shoes", "Kids' Shoes", "shoe", 299, 999, VariantStyle.Shoes, ["Light-Up Sneakers", "First Walkers", "Rain Boots", "Sandals", "School Shoes"], ["Velcro", "Waterproof", "Flexible", "Playful", "Everyday"], ["Canvas", "Leather", "Rubber"]),
        ]),
        new("home", "Home & Living", "sofa", ["Hearthly", "Linden & Co", "Casa Mora", "Nordwood", "Lumo", "Ashby Home"], HomeColors,
        [
            new("furniture", "Furniture", "sofa", 1999, 24999, VariantStyle.ColorOnly, ["Three-Seater Sofa", "Armchair", "Corner Sofa", "Loveseat", "Ottoman"], ["Modular", "Scandinavian", "Velvet", "Compact", "Deep Seat"], ["Velvet", "Linen", "Boucle", "Leather"]),
            new("lighting", "Lighting", "lamp", 299, 3499, VariantStyle.ColorOnly, ["Table Lamp", "Floor Lamp", "Pendant Light", "Desk Lamp", "Wall Light"], ["Dimmable", "Rattan", "Industrial", "Mid-Century", "Arc"], ["Metal", "Rattan", "Ceramic", "Glass"]),
            new("kitchen", "Kitchen & Dining", "mug", 99, 1999, VariantStyle.ColorOnly, ["Mug Set", "Dinner Plate Set", "Salad Bowl", "Teapot", "Serving Bowl"], ["Speckled", "Handmade", "Matte", "Glazed", "Everyday"], ["Ceramic", "Porcelain", "Stoneware"]),
            new("bedding", "Bedding", "box", 299, 2499, VariantStyle.ColorOnly, ["Duvet Cover Set", "Pillow Pair", "Weighted Blanket", "Fitted Sheet", "Throw Blanket"], ["Soft", "Stonewashed", "Cooling", "Waffle", "Classic"], ["Cotton Percale", "Linen", "Bamboo", "Sateen"]),
        ]),
        new("electronics", "Electronics", "phone", ["Voltix", "Nexora", "Auralis", "Pixelon", "Zentra", "Kairo"], TechColors,
        [
            new("smartphones", "Smartphones", "phone", 6999, 64999, VariantStyle.Phone, ["Smartphone", "5G Smartphone", "Pro Smartphone", "Lite Smartphone"], ["X5", "S12", "Nova 8", "Edge 3", "Prime 2", "A40"], [""]),
            new("laptops", "Laptops", "laptop", 14999, 89999, VariantStyle.Laptop, ["Ultrabook", "Gaming Laptop", "2-in-1 Laptop", "Business Laptop"], ["Air 14", "Pro 16", "Flex 13", "Blade 15", "Book 14"], [""]),
            new("headphones", "Headphones", "headphones", 499, 11999, VariantStyle.ColorOnly, ["Wireless Headphones", "Noise Cancelling Headphones", "Earbuds", "Gaming Headset", "Studio Headphones"], ["Over-Ear", "True Wireless", "Foldable", "Pro", "Bass"], [""]),
            new("smartwatches", "Smartwatches", "watch", 1999, 19999, VariantStyle.ColorOnly, ["Smartwatch", "Fitness Tracker", "GPS Watch", "Kids Smartwatch"], ["Active 2", "Fit 5", "Sport", "Classic", "Ultra"], [""]),
        ]),
        new("beauty", "Beauty", "bottle", ["Elysia", "Purelle", "Rosemere", "Dewbloom", "Velvet Moss"], ["Pink", "White", "Beige", "Gold", "Purple"],
        [
            new("skincare", "Skincare", "bottle", 149, 1499, VariantStyle.Single, ["Hydrating Serum", "Daily Moisturizer", "Gentle Cleanser", "Vitamin C Serum", "Sunscreen SPF 50"], ["Brightening", "Fragrance-Free", "Overnight", "Barrier", "Light"], ["30 ml", "50 ml", "100 ml"]),
            new("makeup", "Makeup", "bottle", 99, 999, VariantStyle.ColorOnly, ["Liquid Foundation", "Matte Lipstick", "Mascara", "Blush Palette", "Concealer"], ["Long-Wear", "Velvet", "Volumising", "Dewy", "Natural"], [""]),
            new("fragrance", "Fragrance", "bottle", 399, 4999, VariantStyle.Single, ["Eau de Parfum", "Eau de Toilette", "Body Mist", "Perfume Oil"], ["Amber", "Citrus", "Floral", "Woody", "Musk"], ["50 ml", "100 ml"]),
        ]),
        new("sports", "Sports & Outdoors", "dumbbell", ["Strideon", "Peakform", "Trailmark", "Vigor", "Summitry"], ["Black", "Blue", "Green", "Orange", "Grey", "Red"],
        [
            new("fitness", "Fitness Equipment", "dumbbell", 199, 7999, VariantStyle.Single, ["Adjustable Dumbbells", "Kettlebell", "Yoga Mat", "Resistance Bands", "Pull-Up Bar", "Foam Roller"], ["Home Gym", "Non-Slip", "Compact", "Pro", "Essential", "Studio"], [""]),
            new("camping", "Camping & Hiking", "bag", 299, 5999, VariantStyle.ColorOnly, ["Hiking Backpack", "Two-Person Tent", "Sleeping Bag", "Trekking Poles", "Camping Stove", "Headlamp"], ["Lightweight", "Waterproof", "Ultralight", "All-Season", "Packable", "Trail"], [""]),
            new("sportswear", "Sportswear", "shirt", 199, 1299, VariantStyle.Apparel, ["Training T-Shirt", "Running Shorts", "Track Jacket", "Leggings", "Hoodie"], ["Breathable", "Quick-Dry", "Seamless", "Thermal", "Performance"], ["Polyester", "Recycled Polyester", "Cotton Blend"]),
            new("running-shoes", "Running Shoes", "shoe", 999, 4999, VariantStyle.Shoes, ["Road Running Shoes", "Trail Running Shoes", "Racing Flats", "Stability Shoes"], ["Responsive", "Cushioned", "Carbon Plate", "Grip", "Daily"], ["Mesh", "Knit"]),
        ]),
        new("books", "Books & Hobbies", "book", ["Inkwell Press", "Foxglove Books", "Paperkite", "Meeple Works", "Quill & Lamp"], ["Blue", "Red", "Green", "Navy", "Orange", "Purple"],
        [
            new("novels", "Novels", "book", 89, 399, VariantStyle.Single, ["The Quiet Harbour", "Salt and Silver", "A House of Winters", "The Last Cartographer", "Northern Lights Café", "The Glass Orchard", "Echoes of Anatolia", "Paper Moons", "The Lighthouse Keeper", "Ashes in Spring", "A Map of Small Things", "The Tea Merchant", "Under the Plane Trees", "The Silk Road Letters", "Winter in Kars", "The Clockmaker", "Blue Hour", "The Orchard Thief", "Letters from Bodrum", "The River Between Us", "Saltwater Children", "A Season of Figs", "The Copper Bell", "Night Ferry"], ["Paperback", "Hardcover", "Special Edition"], [""]),
            new("childrens-books", "Children's Books", "book", 59, 299, VariantStyle.Single, ["The Brave Little Fox", "Moon Bear Goes Home", "Counting Stars", "The Busy Garden", "Tiny Dragon", "Rainy Day Friends", "Where the Kites Go", "Ollie the Octopus", "Goodnight, Little Owl", "The Very Tall Giraffe", "Max and the Moon", "Pip Plants a Tree", "The Sleepy Train", "Hello, Snow!", "A Hat for Hedgehog", "Lulu Learns to Swim", "The Noisy Farm", "Big Truck, Little Truck"], ["Board Book", "Picture Book", "Sticker Book"], [""]),
            new("board-games", "Board Games", "toy", 249, 1999, VariantStyle.Single, ["Strategy Game", "Family Card Game", "Cooperative Game", "Word Game", "Party Game"], ["Classic", "Deluxe", "Travel", "Two-Player", "Expansion"], [""]),
        ]),
        new("supermarket", "Supermarket", "jar", ["Harvest Table", "Golden Mill", "Brightday", "Roastery Lane", "Olive Grove"], ["Brown", "Green", "Yellow", "Red", "Orange"],
        [
            new("coffee-tea", "Coffee & Tea", "jar", 89, 899, VariantStyle.Single, ["Whole Bean Coffee", "Ground Coffee", "Black Tea", "Green Tea", "Turkish Coffee"], ["Single Origin", "Dark Roast", "Organic", "Breakfast", "Medium Roast"], ["250 g", "500 g", "1 kg"]),
            new("snacks", "Snacks", "jar", 39, 399, VariantStyle.Single, ["Mixed Nuts", "Dried Apricots", "Dark Chocolate", "Granola", "Olive Oil Crackers"], ["Roasted", "Organic", "Sea Salt", "Honey", "Classic"], ["200 g", "500 g", "1 kg"]),
            new("household", "Household", "bottle", 49, 599, VariantStyle.Single, ["Laundry Detergent", "Dishwasher Tablets", "Surface Cleaner", "Fabric Softener", "Hand Soap"], ["Eco", "Fresh Scent", "Sensitive", "Concentrated", "Lavender"], ["750 ml", "1.5 l", "3 l"]),
        ]),
    ];
}
