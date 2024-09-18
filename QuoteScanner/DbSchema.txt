
-- This is a table of key/value pairs.
CREATE TABLE IF NOT EXISTS state (
	key TEXT PRIMARY KEY,
	value TEXT);

-- Make sure that there is always a key present for the retirement date.
INSERT OR IGNORE INTO state (key, value)
	VALUES ('RETIREMENT_DATE', NULL);

CREATE TABLE IF NOT EXISTS clients (
	id TEXT PRIMARY KEY,
	description TEXT UNIQUE NOT NULL,
	gender_1 TEXT NOT NULL,
	ret_age_1 INTEGER NOT NULL,
	gender_2 TEXT,
	ret_age_2 INTEGER,
	escalation TEXT NOT NULL,
	guarantee_period INTEGER NOT NULL,
	fund_size INTEGER NOT NULL,
	CHECK (gender_1 IN ('M', 'F')),
	CHECK (gender_2 IS NULL OR gender_2 IN ('M', 'F')),
	-- Without parentheses, this wasn't behaving as expected.
	CHECK ((gender_2 IS NULL) = (ret_age_2 IS NULL))
);

CREATE TABLE IF NOT EXISTS updated (
	id TEXT PRIMARY KEY REFERENCES clients (id),
	user TEXT NOT NULL,
	timestamp TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS quotes (
	-- Make sure we can only get quotes for those clients which have been updated.
	id TEXT REFERENCES updated (id),
	user TEXT NOT NULL,
	timestamp TEXT NOT NULL,
	provider TEXT NOT NULL,
	quote REAL,
	error TEXT,
	-- At least one of quote or error must be populated.
	CHECK ((quote IS NULL) != (error IS NULL)) 
);
