-- --------------------------------------------------------------------------
-- Convoy logistics — the non-sensitive half
--
-- Schema-level grants, so every table added to dbo is covered without a grant of its own.
-- --------------------------------------------------------------------------

GRANT SELECT, INSERT, UPDATE, DELETE ON SCHEMA::[dbo] TO [freedom_app];
GO
GRANT SELECT, INSERT, UPDATE ON SCHEMA::[dbo] TO [freedom_worker];
GO
GRANT SELECT ON SCHEMA::[dbo] TO [administrator], [dispatcher], [loader], [purchaser], [ground_officer];
GO

-- --------------------------------------------------------------------------
-- Delivery detail — Ground Officer only
--
-- The DENY is the load-bearing line. Without it, membership of two roles would silently
-- combine to grant access; DENY overrides GRANT in SQL Server, so the application identity
-- cannot read receiver addresses even if someone later adds a broad grant elsewhere.
-- Removing it is the thing to review for.
--
-- tests/UA.Action.Freedom.Tests.Integration/Receivers/ReceiverSegregationTests.cs asserts it
-- against the real database, connected as freedom_app.
--
-- The audit log the Ground Officer path writes on every address it resolves needs no grant
-- of its own: INSERT on the schema covers sensitive.ReceiverDetailAccessLog.
-- --------------------------------------------------------------------------

GRANT SELECT, INSERT, UPDATE, DELETE ON SCHEMA::[sensitive] TO [ground_officer];
GO
DENY SELECT ON SCHEMA::[sensitive] TO [freedom_app], [freedom_worker], [administrator], [dispatcher], [loader], [purchaser];
GO
