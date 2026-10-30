export type Category = { slug: string; name: string; tagline: string; hue: number }

export const categories: Category[] = [
  { slug: 'women', name: 'Women', tagline: 'Dresses, shoes and bags', hue: 330 },
  { slug: 'men', name: 'Men', tagline: 'Shirts, sneakers and watches', hue: 215 },
  { slug: 'kids', name: 'Mother & Kids', tagline: 'Toys, strollers and clothing', hue: 45 },
  { slug: 'home', name: 'Home & Living', tagline: 'Furniture, decor and kitchen', hue: 25 },
  { slug: 'electronics', name: 'Electronics', tagline: 'Phones, laptops and audio', hue: 255 },
  { slug: 'beauty', name: 'Beauty', tagline: 'Skincare, makeup and fragrance', hue: 300 },
  { slug: 'sports', name: 'Sports & Outdoors', tagline: 'Fitness, camping and cycling', hue: 150 },
  { slug: 'books', name: 'Books & Hobbies', tagline: 'Books, music and crafts', hue: 190 },
  { slug: 'supermarket', name: 'Supermarket', tagline: 'Groceries and household', hue: 95 },
]

export function findCategory(slug: string): Category | undefined {
  return categories.find((c) => c.slug === slug)
}
