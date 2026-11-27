-- New approvals carry immutable phase IDs and impacts in the journal payload.
-- The migration runner advances the ledger and user_version so older strict JSON
-- readers refuse the newer store. Existing rows and their hashes stay untouched.
SELECT 1;
