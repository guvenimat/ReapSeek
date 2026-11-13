CREATE SCHEMA IF NOT EXISTS identity;
CREATE SCHEMA IF NOT EXISTS catalog;
CREATE SCHEMA IF NOT EXISTS sellers;
CREATE SCHEMA IF NOT EXISTS ordering;
CREATE SCHEMA IF NOT EXISTS finance;
CREATE SCHEMA IF NOT EXISTS engagement;
CREATE SCHEMA IF NOT EXISTS marketing;
CREATE SCHEMA IF NOT EXISTS platform;

ALTER TABLE public.users SET SCHEMA identity;
ALTER TABLE public.sessions SET SCHEMA identity;
ALTER TABLE public.account_tokens SET SCHEMA identity;
ALTER TABLE public.addresses SET SCHEMA identity;

ALTER TABLE public.categories SET SCHEMA catalog;
ALTER TABLE public.brands SET SCHEMA catalog;
ALTER TABLE public.products SET SCHEMA catalog;
ALTER TABLE public.product_images SET SCHEMA catalog;
ALTER TABLE public.product_variants SET SCHEMA catalog;
ALTER TABLE public.offers SET SCHEMA catalog;
ALTER VIEW public.buy_box SET SCHEMA catalog;

ALTER TABLE public.sellers SET SCHEMA sellers;

ALTER TABLE public.carts SET SCHEMA ordering;
ALTER TABLE public.cart_lines SET SCHEMA ordering;
ALTER TABLE public.orders SET SCHEMA ordering;
ALTER TABLE public.order_packages SET SCHEMA ordering;
ALTER TABLE public.order_lines SET SCHEMA ordering;
ALTER TABLE public.payments SET SCHEMA ordering;
ALTER TABLE public.payment_events SET SCHEMA ordering;
ALTER TABLE public.shipment_events SET SCHEMA ordering;
ALTER TABLE public.returns SET SCHEMA ordering;
ALTER TABLE public.return_lines SET SCHEMA ordering;
ALTER SEQUENCE public.order_number_seq SET SCHEMA ordering;
ALTER SEQUENCE public.return_number_seq SET SCHEMA ordering;

ALTER TABLE public.seller_ledger SET SCHEMA finance;
ALTER TABLE public.payouts SET SCHEMA finance;

ALTER TABLE public.reviews SET SCHEMA engagement;
ALTER TABLE public.questions SET SCHEMA engagement;
ALTER TABLE public.answers SET SCHEMA engagement;
ALTER TABLE public.wishlist_items SET SCHEMA engagement;

ALTER TABLE public.campaigns SET SCHEMA marketing;
ALTER TABLE public.coupons SET SCHEMA marketing;
ALTER TABLE public.reapdeep_alert_subscriptions SET SCHEMA marketing;
ALTER TABLE public.analytics_alerts SET SCHEMA marketing;

ALTER TABLE public.outbox SET SCHEMA platform;

DO $$
BEGIN
    EXECUTE format('ALTER DATABASE %I SET search_path = identity, catalog, sellers, ordering, finance, engagement, marketing, platform, public', current_database());
END
$$;

SET search_path = identity, catalog, sellers, ordering, finance, engagement, marketing, platform, public;
