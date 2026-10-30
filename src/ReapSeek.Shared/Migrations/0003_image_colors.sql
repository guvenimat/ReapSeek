ALTER TABLE product_images ADD COLUMN color text;

UPDATE product_images i
SET color = v.color
FROM (
    SELECT DISTINCT product_id, options->>'Color' AS color
    FROM product_variants
    WHERE options ? 'Color'
) v
WHERE v.product_id = i.product_id
  AND i.url LIKE '/media/catalog/%'
  AND lower(v.color) = split_part(i.url, '/', 5);
