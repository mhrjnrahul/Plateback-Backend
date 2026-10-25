-- Load-test data for profiling (Week 5). Adds 400 verified businesses around
-- the Kathmandu valley and 30,000 listings on top of the demo data: about 20%
-- with a pickup window still to come, the rest ended. The demo seed
-- (`dotnet run --project src/PlateBack.Api -- seed`) removes it all again.
--
--   psql -h localhost -U plateback -d plateback -f perf/load-data.sql
--
-- setseed makes every run produce the same rows, so measurements compare.
SELECT setseed(0.42);

INSERT INTO organizations (id, name, type, address, latitude, longitude, time_zone, phone, status, created_at, verified_at)
SELECT gen_random_uuid(),
       'Load Test Business ' || n,
       (ARRAY['Bakery', 'Cafe', 'Restaurant', 'Grocer'])[1 + n % 4],
       'Ward ' || (n % 32 + 1) || ', Kathmandu valley',
       27.62 + random() * 0.14,
       85.25 + random() * 0.20,
       'Asia/Kathmandu', NULL, 'Verified', now() - interval '60 days', now() - interval '59 days'
FROM generate_series(1, 400) AS n;

WITH orgs AS (
    SELECT id, latitude, longitude, row_number() OVER (ORDER BY name) AS rn
    FROM organizations WHERE name LIKE 'Load Test Business %'
), rows AS (
    SELECT o.id AS org_id, o.latitude, o.longitude, g AS n,
           -- 20% upcoming or open now, 80% in the past 30 days
           CASE WHEN random() < 0.2
                THEN now() + (random() * interval '20 hours') - interval '2 hours'
                ELSE now() - (random() * interval '30 days') - interval '1 day' END AS starts,
           1 + floor(random() * 30)::int AS qty
    FROM orgs o CROSS JOIN generate_series(1, 75) AS g
)
INSERT INTO listings (id, organization_id, title, category, description, allergen_notes, price, original_price,
                      quantity_total, quantity_available, pickup_start, pickup_end, address, latitude, longitude,
                      status, image_url, created_at)
SELECT gen_random_uuid(), org_id,
       (ARRAY['Bread basket', 'Veg thali', 'Momo box', 'Fruit bag', 'Cake slices', 'Coffee and pastry'])[1 + n % 6] || ' #' || n,
       (ARRAY['Bakery', 'Meals', 'Meals', 'Groceries', 'Bakery', 'Beverages'])[1 + n % 6],
       repeat('Fresh food from today, packed at closing time. ', 6),
       'Contains gluten and milk.',
       50 + (n % 10) * 20, 200 + (n % 10) * 50,
       qty, CASE WHEN starts > now() - interval '2 hours' THEN floor(random() * (qty + 1))::int ELSE 0 END,
       starts, starts + interval '2 hours',
       'Ward ' || (n % 32 + 1) || ', Kathmandu valley',
       latitude + (random() - 0.5) * 0.002, longitude + (random() - 0.5) * 0.002,
       CASE WHEN starts + interval '2 hours' < now() THEN 'Expired' ELSE 'Active' END,
       NULL, starts - interval '3 hours'
FROM rows;

ANALYZE organizations;
ANALYZE listings;
SELECT count(*) AS listings, count(*) FILTER (WHERE status = 'Active') AS active FROM listings;
