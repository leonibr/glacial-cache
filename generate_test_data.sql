-- GlacialCache Test Data Generation Script
-- Generates 1 million records with realistic data distribution
-- Run this after creating the table schema

-- Set work_mem for better performance during bulk insert
SET work_mem = '256MB';

-- Create a temporary function to generate random bytea data
CREATE OR REPLACE FUNCTION generate_random_bytea(min_size INTEGER, max_size INTEGER) 
RETURNS BYTEA AS $$
BEGIN
    RETURN decode(
        string_agg(
            lpad(to_hex(floor(random() * 256)::integer), 2, '0'), 
            ''
        ), 
        'hex'
    ) FROM generate_series(1, floor(random() * (max_size - min_size + 1) + min_size));
END;
$$ LANGUAGE plpgsql;

-- Create a temporary function to generate random intervals
CREATE OR REPLACE FUNCTION generate_random_interval() 
RETURNS INTERVAL AS $$
BEGIN
    RETURN (floor(random() * 30) + 1) * INTERVAL '1 hour' + 
           (floor(random() * 60)) * INTERVAL '1 minute';
END;
$$ LANGUAGE plpgsql;

-- Insert 1 million records in batches for better performance
DO $$
DECLARE
    batch_size INTEGER := 10000;
    total_records INTEGER := 1000000;
    current_batch INTEGER := 0;
    value_types TEXT[] := ARRAY['string', 'json', 'xml', 'binary', 'serialized'];
    start_time TIMESTAMP;
BEGIN
    start_time := clock_timestamp();
    RAISE NOTICE 'Starting data generation for % records...', total_records;
    
    -- Generate records in batches
    FOR current_batch IN 0..(total_records / batch_size - 1) LOOP
        -- Insert batch
        INSERT INTO glacial.cache (
            key,
            value,
            absolute_expiration,
            sliding_interval,
            next_expiration,
            value_type
        )
        SELECT 
            -- Generate unique keys with pattern: prefix_batch_row
            'key_' || 
            lpad((current_batch * batch_size + row_number() OVER ())::TEXT, 8, '0') ||
            '_' || 
            lpad(floor(random() * 1000000)::TEXT, 6, '0'),
            
            -- Generate random bytea data (100 bytes to 10KB)
            generate_random_bytea(100, 10240),
            
            -- 70% of records have absolute expiration (1 hour to 30 days from now)
            CASE 
                WHEN random() < 0.7 THEN 
                    NOW() + (floor(random() * 720) + 1) * INTERVAL '1 hour'
                ELSE NULL
            END,
            
            -- 60% of records have sliding expiration (15 minutes to 2 hours)
            CASE 
                WHEN random() < 0.6 THEN 
                    generate_random_interval()
                ELSE NULL
            END,
            
            -- next_expiration: if sliding, set to now + sliding, otherwise now
            CASE 
                WHEN random() < 0.6 THEN 
                    NOW() + generate_random_interval()
                ELSE NOW()
            END,
            
            -- Random value type from predefined array
            value_types[floor(random() * array_length(value_types, 1) + 1)]
            
        FROM generate_series(1, batch_size);
        
        -- Progress indicator
        IF current_batch % 10 = 0 THEN
            RAISE NOTICE 'Processed batch % of % (%.1f%%)', 
                current_batch, 
                total_records / batch_size, 
                (current_batch * batch_size * 100.0 / total_records);
        END IF;
    END LOOP;
    
    RAISE NOTICE 'Data generation completed in %', clock_timestamp() - start_time;
END $$;

-- Clean up temporary functions
DROP FUNCTION IF EXISTS generate_random_bytea(INTEGER, INTEGER);
DROP FUNCTION IF EXISTS generate_random_interval();

-- Verify the data
SELECT 
    COUNT(*) as total_records,
    COUNT(*) FILTER (WHERE absolute_expiration IS NOT NULL) as with_absolute_expiration,
    COUNT(*) FILTER (WHERE sliding_interval IS NOT NULL) as with_sliding_expiration,
    COUNT(*) FILTER (WHERE value_type IS NOT NULL) as with_value_type,
    AVG(value_size) as avg_value_size,
    MIN(value_size) as min_value_size,
    MAX(value_size) as max_value_size
FROM glacial.cache;

-- Show sample of generated data
SELECT 
    key,
    value_type,
    value_size,
    absolute_expiration,
    sliding_interval,
    next_expiration
FROM glacial.cache 
ORDER BY RANDOM() 
LIMIT 10;

-- Show distribution by value type
SELECT 
    value_type,
    COUNT(*) as count,
    ROUND(COUNT(*) * 100.0 / SUM(COUNT(*)) OVER (), 2) as percentage
FROM glacial.cache 
GROUP BY value_type 
ORDER BY count DESC;
