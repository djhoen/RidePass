-- Dev seed: two active tenants. Run against ridepass_dev.
-- Safe to re-run: ON CONFLICT DO NOTHING on subdomain.

INSERT INTO tenant (subdomain, display_name, status) VALUES
    ('acme',      'Acme MX Park',        'active'),
    ('foothills', 'Foothills Bike Park', 'active')
ON CONFLICT (subdomain) DO NOTHING;

SELECT id, subdomain, display_name, status FROM tenant ORDER BY subdomain;
